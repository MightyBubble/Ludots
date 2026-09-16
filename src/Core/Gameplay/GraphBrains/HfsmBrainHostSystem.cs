using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.GraphBrains;

/// <summary>
/// Entity-driven HFSM brain host. For every entity carrying a <see cref="GraphActionBrain"/>
/// with an <see cref="GraphActionBrain.HfsmId"/>, one agent of the named HFSM is kept alive
/// and ticked through the existing <see cref="HfsmWorld"/> + <see cref="GraphProgramHfsmHost"/>.
///
/// Engine-side the system keeps ONE SoA slot-pool per HFSM definition, shared by all entities
/// that instantiate that definition; author-side each entity still owns its own FSM instance
/// (its own agent slot, caster, birth blackboard) — SoA layout never leaks into the authoring
/// model. The system owns only entity/agent binding and the order glue: it refreshes
/// OrderGlueKeys entity-blackboard values, points the agent's caster at the entity, and calls
/// <see cref="HfsmWorld.TickAll"/>. State lives in the HFSM stack; counters live on the
/// entity blackboard; the state machine itself is never re-implemented here.
///
/// Dynamic RTS lifecycles are handled through slot reuse: <see cref="HfsmWorld"/> is
/// constructed with <c>reuseSlots: true</c>, entities released on death return their slot
/// to the pool (running OnExit), and new entities re-acquire it. This keeps one SoA world
/// per definition without leaking agent slots as units come and go.
/// </summary>
public sealed class HfsmBrainHostSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription BrainQuery = new QueryDescription()
        .WithAll<GraphActionBrain, OrderBuffer, PlayerOwner, BlackboardIntBuffer, BlackboardEntityBuffer>();

    /// <summary>
    /// Per-lifecycle step budget. State graphs that mirror a legacy monolithic behavior slice
    /// (e.g. standoff pursuit routing) can legitimately reach ~85 instructions, so the HFSM
    /// default of 64 is raised here while still guarding against runaway graphs.
    /// </summary>
    private const int LifecycleStepBudget = 128;

    private readonly GraphProgramRegistry _programs;
    private readonly IGraphRuntimeApi _api;
    private readonly IGameplayAdvanceGate _gate;
    private readonly GraphBehaviorCatalog _behavior;
    private readonly Dictionary<string, HfsmDefinition> _definitionsByHfsmId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DefinitionPool> _poolsByHfsmId = new(StringComparer.Ordinal);
    private readonly Dictionary<Entity, EntityAgent> _agents = new();
    private readonly List<Entity> _sweepRemovals = new();
    private int _tick;

    public HfsmBrainHostSystem(
        World world,
        GraphProgramRegistry programs,
        IGraphRuntimeApi api,
        IGameplayAdvanceGate gate,
        GraphBehaviorCatalog behavior) : base(world)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    public override void Update(in float dt)
    {
        if (!_gate.CanAdvanceGameplay)
        {
            return;
        }

        _tick++;
        foreach (ref Chunk chunk in World.Query(in BrainQuery))
        {
            Span<GraphActionBrain> brains = chunk.GetSpan<GraphActionBrain>();
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity actor = Unsafe.Add(ref first, index);
                ref readonly GraphActionBrain brain = ref brains[index];
                if (string.IsNullOrEmpty(brain.HfsmId))
                {
                    continue;
                }

                WriteOrderGlue(actor, in buffers[index]);
                EntityAgent slot = ResolveSlot(actor, in brain);
                slot.Stamp = _tick;
                if ((_tick % Math.Max(1, brain.ThinkEveryNTicks)) != 0)
                {
                    continue;
                }
            }
        }

        // Tick each shared per-definition world once (author-side each entity still has its own
        // agent slot; engine-side SoA pool is advanced in one pass per definition).
        foreach (KeyValuePair<string, DefinitionPool> pair in _poolsByHfsmId)
        {
            DefinitionPool pool = pair.Value;
            pool.World.TickAll(pool.Host);
        }

        SweepStaleSlots();
    }

    /// <summary>Number of entities currently bound to an HFSM agent.</summary>
    public int AgentCount => _agents.Count;

    /// <summary>Current leaf state name for an entity's HFSM agent (diagnostics / acceptance assertions).</summary>
    public bool TryGetLeafStateName(Entity actor, out string stateName)
    {
        if (_agents.TryGetValue(actor, out EntityAgent slot))
        {
            stateName = slot.Pool.World.GetLeafStateName(slot.AgentIndex);
            return true;
        }

        stateName = string.Empty;
        return false;
    }

    private EntityAgent ResolveSlot(Entity actor, in GraphActionBrain brain)
    {
        if (_agents.TryGetValue(actor, out EntityAgent existing))
        {
            return existing;
        }

        DefinitionPool pool = ResolvePool(brain.HfsmId);
        int agentIndex = pool.World.AcquireAgent(pool.Host);
        pool.Host.SetAgentCaster(agentIndex, actor);
        WriteBirthState(actor, in brain);
        var slot = new EntityAgent(pool, agentIndex);
        _agents.Add(actor, slot);
        return slot;
    }

    private DefinitionPool ResolvePool(string hfsmId)
    {
        if (_poolsByHfsmId.TryGetValue(hfsmId, out DefinitionPool? existing))
        {
            return existing;
        }

        HfsmDefinition definition = ResolveDefinition(hfsmId);
        // One SoA world per definition; capacity is pooled and re-acquired as units come and go.
        int capacity = DefinitionPool.AgentCapacityPerDefinition;
        var world = new HfsmWorld(definition, capacity: capacity, reuseSlots: true);
        var host = new GraphProgramHfsmHost(_programs, World, _api, budgetSteps: LifecycleStepBudget);
        host.EnsureAgentCasterCapacity(capacity);
        var pool = new DefinitionPool(hfsmId, world, host);
        _poolsByHfsmId[hfsmId] = pool;
        return pool;
    }

    private HfsmDefinition ResolveDefinition(string hfsmId)
    {
        if (_definitionsByHfsmId.TryGetValue(hfsmId, out HfsmDefinition? definition))
        {
            return definition;
        }

        definition = _behavior.RequireHfsm(hfsmId);
        _definitionsByHfsmId[hfsmId] = definition;
        return definition;
    }

    private void WriteOrderGlue(Entity actor, in OrderBuffer buffer)
    {
        ref BlackboardIntBuffer ints = ref World.Get<BlackboardIntBuffer>(actor);
        ref BlackboardEntityBuffer entities = ref World.Get<BlackboardEntityBuffer>(actor);
        ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.PlayerId, World.Get<PlayerOwner>(actor).PlayerId);
        if (buffer.HasActive)
        {
            ref readonly Order order = ref buffer.ActiveOrder.Order;
            ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.ActiveTypeId, order.OrderTypeId);
            ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.SpatialXCm, (int)order.Args.Spatial.WorldCm.X);
            ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.SpatialYCm, (int)order.Args.Spatial.WorldCm.Z);
            ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.HasActive, 1);
            entities.Set(GraphActionBrainHostSystem.OrderGlueKeys.ActiveTarget, order.Target);
        }
        else
        {
            ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.ActiveTypeId, 0);
            ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.SpatialXCm, 0);
            ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.SpatialYCm, 0);
            ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.HasActive, 0);
            entities.Set(GraphActionBrainHostSystem.OrderGlueKeys.ActiveTarget, Entity.Null);
        }

        ints.Set(GraphActionBrainHostSystem.OrderGlueKeys.HasPending, buffer.HasPending ? 1 : 0);
    }

    private void WriteBirthState(Entity actor, in GraphActionBrain brain)
    {
        if (brain.BlackboardIntDefaults != null)
        {
            ref BlackboardIntBuffer buffer = ref World.Get<BlackboardIntBuffer>(actor);
            foreach ((string key, int value) in brain.BlackboardIntDefaults)
            {
                buffer.Set(ConfigKeyRegistry.Register(key), value);
            }
        }

        if (brain.BlackboardEntityDefaults != null)
        {
            ref BlackboardEntityBuffer entities = ref World.Get<BlackboardEntityBuffer>(actor);
            foreach (string key in brain.BlackboardEntityDefaults)
            {
                entities.Set(ConfigKeyRegistry.Register(key), Entity.Null);
            }
        }
    }

    private void SweepStaleSlots()
    {
        if (_agents.Count == 0)
        {
            return;
        }

        _sweepRemovals.Clear();
        foreach (KeyValuePair<Entity, EntityAgent> entry in _agents)
        {
            if (entry.Value.Stamp != _tick)
            {
                _sweepRemovals.Add(entry.Key);
            }
        }

        for (int i = 0; i < _sweepRemovals.Count; i++)
        {
            Entity actor = _sweepRemovals[i];
            if (_agents.Remove(actor, out EntityAgent? slot))
            {
                slot.Pool.World.ReleaseAgent(slot.AgentIndex, slot.Pool.Host);
            }
        }
    }

    private sealed class DefinitionPool
    {
        public const int AgentCapacityPerDefinition = 128;

        public DefinitionPool(string hfsmId, HfsmWorld world, GraphProgramHfsmHost host)
        {
            HfsmId = hfsmId;
            World = world;
            Host = host;
        }

        public string HfsmId { get; }
        public HfsmWorld World { get; }
        public GraphProgramHfsmHost Host { get; }
    }

    private sealed class EntityAgent
    {
        public EntityAgent(DefinitionPool pool, int agentIndex)
        {
            Pool = pool;
            AgentIndex = agentIndex;
        }

        public DefinitionPool Pool { get; }
        public int AgentIndex { get; }
        public int Stamp;
    }
}
