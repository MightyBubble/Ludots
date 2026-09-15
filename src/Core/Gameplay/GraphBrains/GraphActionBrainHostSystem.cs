using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
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
/// Order-driven graph brain host (issue #1536): one resident Script-graph execution
/// frame per entity carrying a <see cref="GraphActionBrain"/>. Each think tick
/// refreshes the <see cref="OrderGlueKeys"/> entity-blackboard values (active order
/// type/spatial/target, player id, pending flag), sets the slice caster to the actor,
/// then runs one slice with the production runtime API so behavior graphs can submit
/// orders, complete them, and apply effects through the existing pipelines. Halted
/// frames restart from the program root on the next think tick; suspended frames
/// (Yield/budget) resume in place. Slots are entity-keyed and swept when an entity
/// leaves the query.
/// </summary>
public sealed class GraphActionBrainHostSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription BrainQuery = new QueryDescription()
        .WithAll<GraphActionBrain, OrderBuffer, PlayerOwner, BlackboardIntBuffer, BlackboardEntityBuffer>();

    private readonly GraphProgramRegistry _programs;
    private readonly IGraphRuntimeApi _api;
    private readonly IGameplayAdvanceGate _gate;
    private readonly int _budgetStepsPerTick;
    private readonly Dictionary<int, BrainPool> _poolsByGraphId = new();
    private readonly Dictionary<string, int> _graphIdByScriptKey = new(StringComparer.Ordinal);
    private readonly List<Entity> _sweepRemovals = new();
    private int _tick;

    public GraphActionBrainHostSystem(
        World world,
        GraphProgramRegistry programs,
        IGraphRuntimeApi api,
        IGameplayAdvanceGate gate,
        int budgetStepsPerTick = 256) : base(world)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        if (budgetStepsPerTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetStepsPerTick));
        }

        _budgetStepsPerTick = budgetStepsPerTick;
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
                BrainPool pool = ResolvePool(brain.ScriptKey);
                int slot = pool.EnsureSlot(actor, _tick, in brain);
                if ((_tick % Math.Max(1, brain.ThinkEveryNTicks)) != 0)
                {
                    continue;
                }

                ref OrderBuffer buffer = ref buffers[index];
                Think(pool, slot, actor, in buffer);
            }
        }

        SweepStaleSlots();
    }

    private void Think(BrainPool pool, int slot, Entity actor, in OrderBuffer buffer)
    {
        ref GraphExecutionCursor cursor = ref pool.Cursors[slot];
        if (!cursor.IsSuspended)
        {
            cursor.Reset();
            pool.ClearRegisters(slot);
            ref BlackboardIntBuffer ints = ref World.Get<BlackboardIntBuffer>(actor);
            ref BlackboardEntityBuffer entities = ref World.Get<BlackboardEntityBuffer>(actor);
            ints.Set(OrderGlueKeys.PlayerId, World.Get<Ludots.Core.Gameplay.Components.PlayerOwner>(actor).PlayerId);
            if (buffer.HasActive)
            {
                ref readonly Order order = ref buffer.ActiveOrder.Order;
                ints.Set(OrderGlueKeys.ActiveTypeId, order.OrderTypeId);
                ints.Set(OrderGlueKeys.SpatialXCm, (int)order.Args.Spatial.WorldCm.X);
                ints.Set(OrderGlueKeys.SpatialYCm, (int)order.Args.Spatial.WorldCm.Z);
                ints.Set(OrderGlueKeys.HasActive, 1);
                entities.Set(OrderGlueKeys.ActiveTarget, order.Target);
            }
            else
            {
                ints.Set(OrderGlueKeys.ActiveTypeId, 0);
                ints.Set(OrderGlueKeys.SpatialXCm, 0);
                ints.Set(OrderGlueKeys.SpatialYCm, 0);
                ints.Set(OrderGlueKeys.HasActive, 0);
                entities.Set(OrderGlueKeys.ActiveTarget, Entity.Null);
            }

            ints.Set(OrderGlueKeys.HasPending, buffer.HasPending ? 1 : 0);
        }

        GraphSliceResult result = GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
            _programs,
            pool.Program,
            pool.FloatRow(slot),
            pool.IntRow(slot),
            pool.BoolRow(slot),
            pool.EntityRow(slot),
            pool.TargetRow(slot),
            pool.CallStackRow(slot),
            ref cursor,
            _budgetStepsPerTick,
            World,
            actor,
            default,
            _api);
        pool.Steps += result.Steps;
    }

    private BrainPool ResolvePool(string scriptKey)
    {
        if (string.IsNullOrWhiteSpace(scriptKey))
        {
            throw new InvalidOperationException(
                "GAS.GRAPH_BRAIN.ERR.EmptyScriptKey: GraphActionBrain carries an empty script key.");
        }

        if (!_graphIdByScriptKey.TryGetValue(scriptKey, out int graphId))
        {
            graphId = GraphIdRegistry.GetId(scriptKey);
            if (graphId <= 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH_BRAIN.ERR.UnknownScriptKey: GraphActionBrain references unknown graph '{scriptKey}'.");
            }

            _graphIdByScriptKey[scriptKey] = graphId;
        }

        if (!_poolsByGraphId.TryGetValue(graphId, out BrainPool? pool))
        {
            pool = new BrainPool(
                this,
                scriptKey,
                _programs.RequireProgramArray(graphId, GraphKind.Script, nameof(GraphActionBrainHostSystem)));
            _poolsByGraphId[graphId] = pool;
        }

        return pool;
    }

    private void SweepStaleSlots()
    {
        foreach (KeyValuePair<int, BrainPool> pair in _poolsByGraphId)
        {
            BrainPool pool = pair.Value;
            _sweepRemovals.Clear();
            foreach (KeyValuePair<Entity, int> entry in pool.Slots)
            {
                if (pool.Stamps[entry.Value] != _tick)
                {
                    _sweepRemovals.Add(entry.Key);
                }
            }

            for (int i = 0; i < _sweepRemovals.Count; i++)
            {
                pool.ReleaseSlot(_sweepRemovals[i]);
            }
        }
    }


        private static void WriteBirthState(World world, Entity actor, in GraphActionBrain brain)
        {
            if (brain.BlackboardIntDefaults != null)
            {
                ref BlackboardIntBuffer buffer = ref world.Get<BlackboardIntBuffer>(actor);
                foreach ((string key, int value) in brain.BlackboardIntDefaults)
                {
                    buffer.Set(Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(key), value);
                }
            }

            if (brain.BlackboardEntityDefaults != null)
            {
                ref BlackboardEntityBuffer entities = ref world.Get<BlackboardEntityBuffer>(actor);
                foreach (string key in brain.BlackboardEntityDefaults)
                {
                    entities.Set(Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(key), Entity.Null);
                }
            }
        }
    /// <summary>Total graph steps executed since construction (metrics/diagnostics surface).</summary>
    public long TotalSteps
    {
        get
        {
            long total = 0;
            foreach (KeyValuePair<int, BrainPool> pair in _poolsByGraphId)
            {
                total += pair.Value.Steps;
            }

            return total;
        }
    }

    /// <summary>
    /// Fixed entity-blackboard keys carrying the order glue each think tick (ConfigKeyRegistry
    /// id space, the same one graph blackboard ops resolve against). Brains read these via
    /// ReadBlackboardInt/Entity; live debug sees them like any blackboard write.
    /// </summary>
    public static class OrderGlueKeys
    {
        public const string ActiveTypeIdName = "Order.ActiveTypeId";
        public const string SpatialXCmName = "Order.SpatialXCm";
        public const string SpatialYCmName = "Order.SpatialYCm";
        public const string HasActiveName = "Order.HasActive";
        public const string HasPendingName = "Order.HasPending";
        public const string ActiveTargetName = "Order.ActiveTarget";
        public const string PlayerIdName = "Order.PlayerId";

        public static readonly int ActiveTypeId = ConfigKeyRegistry.Register(ActiveTypeIdName);
        public static readonly int SpatialXCm = ConfigKeyRegistry.Register(SpatialXCmName);
        public static readonly int SpatialYCm = ConfigKeyRegistry.Register(SpatialYCmName);
        public static readonly int HasActive = ConfigKeyRegistry.Register(HasActiveName);
        public static readonly int HasPending = ConfigKeyRegistry.Register(HasPendingName);
        public static readonly int ActiveTarget = ConfigKeyRegistry.Register(ActiveTargetName);
        public static readonly int PlayerId = ConfigKeyRegistry.Register(PlayerIdName);
    }

    private sealed class BrainPool
    {
        public const int InitialCapacity = 64;

        public readonly string ScriptKey;
        public readonly GraphInstruction[] Program;
        public readonly Dictionary<Entity, int> Slots = new();

        public GraphExecutionCursor[] Cursors = new GraphExecutionCursor[InitialCapacity];
        public int[] Ints = new int[InitialCapacity * GraphVmLimits.MaxIntRegisters];
        public byte[] Bools = new byte[InitialCapacity * GraphVmLimits.MaxBoolRegisters];
        public float[] Floats = new float[InitialCapacity * GraphVmLimits.MaxFloatRegisters];
        public Entity[] Entities = new Entity[InitialCapacity * GraphVmLimits.MaxEntityRegisters];
        public Entity[] Targets = new Entity[InitialCapacity * GraphVmLimits.MaxTargets];
        public int[] CallStacks = new int[InitialCapacity * GraphVmLimits.MaxCallStackDepth];
        public int[] Stamps = new int[InitialCapacity];
        public long Steps;
        private readonly List<int> _freeSlots = new();
        private readonly GraphActionBrainHostSystem _owner;
        private int _capacity = InitialCapacity;

        public BrainPool(GraphActionBrainHostSystem owner, string scriptKey, GraphInstruction[] program)
        {
            _owner = owner;
            ScriptKey = scriptKey;
            Program = program;
        }

        public Span<int> IntRow(int slot) => Ints.AsSpan(slot * GraphVmLimits.MaxIntRegisters, GraphVmLimits.MaxIntRegisters);
        public Span<byte> BoolRow(int slot) => Bools.AsSpan(slot * GraphVmLimits.MaxBoolRegisters, GraphVmLimits.MaxBoolRegisters);
        public Span<float> FloatRow(int slot) => Floats.AsSpan(slot * GraphVmLimits.MaxFloatRegisters, GraphVmLimits.MaxFloatRegisters);
        public Span<Entity> EntityRow(int slot) => Entities.AsSpan(slot * GraphVmLimits.MaxEntityRegisters, GraphVmLimits.MaxEntityRegisters);
        public Span<Entity> TargetRow(int slot) => Targets.AsSpan(slot * GraphVmLimits.MaxTargets, GraphVmLimits.MaxTargets);
        public Span<int> CallStackRow(int slot) => CallStacks.AsSpan(slot * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth);

        public int EnsureSlot(Entity actor, int tick, in GraphActionBrain brain)
        {
            if (Slots.TryGetValue(actor, out int slot))
            {
                Stamps[slot] = tick;
                return slot;
            }

            if (_freeSlots.Count > 0)
            {
                slot = _freeSlots[^1];
                _freeSlots.RemoveAt(_freeSlots.Count - 1);
            }
            else
            {
                slot = Slots.Count;
                if (slot >= _capacity)
                {
                    Grow();
                }
            }

            Cursors[slot].Reset();
            ClearRegisters(slot);
            WriteBirthState(_owner.World, actor, in brain);
            Slots[actor] = slot;
            Stamps[slot] = tick;
            return slot;
        }

        public void ReleaseSlot(Entity actor)
        {
            int slot = Slots[actor];
            Cursors[slot].Reset();
            ClearRegisters(slot);
            Stamps[slot] = 0;
            _freeSlots.Add(slot);
            Slots.Remove(actor);
        }

        public void ClearRegisters(int slot)
        {
            Array.Clear(Ints, slot * GraphVmLimits.MaxIntRegisters, GraphVmLimits.MaxIntRegisters);
            Array.Clear(Bools, slot * GraphVmLimits.MaxBoolRegisters, GraphVmLimits.MaxBoolRegisters);
            Array.Clear(Floats, slot * GraphVmLimits.MaxFloatRegisters, GraphVmLimits.MaxFloatRegisters);
            Entities.AsSpan(slot * GraphVmLimits.MaxEntityRegisters, GraphVmLimits.MaxEntityRegisters).Fill(Entity.Null);
            Targets.AsSpan(slot * GraphVmLimits.MaxTargets, GraphVmLimits.MaxTargets).Fill(Entity.Null);
            Array.Clear(CallStacks, slot * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth);
        }

        private void Grow()
        {
            int newCapacity = _capacity * 2;
            Array.Resize(ref Cursors, newCapacity);
            Array.Resize(ref Ints, newCapacity * GraphVmLimits.MaxIntRegisters);
            Array.Resize(ref Bools, newCapacity * GraphVmLimits.MaxBoolRegisters);
            Array.Resize(ref Floats, newCapacity * GraphVmLimits.MaxFloatRegisters);
            Array.Resize(ref Entities, newCapacity * GraphVmLimits.MaxEntityRegisters);
            Array.Resize(ref Targets, newCapacity * GraphVmLimits.MaxTargets);
            Array.Resize(ref CallStacks, newCapacity * GraphVmLimits.MaxCallStackDepth);
            Array.Resize(ref Stamps, newCapacity);
            _capacity = newCapacity;
        }
    }
}
