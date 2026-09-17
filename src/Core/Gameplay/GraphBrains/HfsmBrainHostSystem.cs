using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
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
/// Per-entity HFSM behavior driver, following the component-carried FSM pattern that
/// animator already establishes (see <c>Presentation.Components.AnimatorRuntimeState</c>):
/// each behavior entity carries its own <see cref="HfsmState"/> component; the system
/// queries those entities directly and advances each from its own leaf using the shared
/// <see cref="HfsmDefinition"/> transition table. Entity lifecycle is entirely ECS —
/// adding the component binds behavior, removing it (entity death) drops the state with no
/// agent pool, no index, no release, no private registry.
///
/// Author-side each unit still "owns one FSM"; engine-side we never recreate an entity
/// lifecycle manager — Arch already provides it. The legacy HfsmWorld (a self-contained
/// SoA multi-agent engine) remains available for showcase / stress driving, but is not
/// used to manage per-entity behavior state here.
/// </summary>
public sealed class HfsmBrainHostSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription BrainQuery = new QueryDescription()
        .WithAll<GraphActionBrain, HfsmState, OrderBuffer, PlayerOwner, BlackboardIntBuffer, BlackboardEntityBuffer>();

    /// <summary>
    /// Per-lifecycle step budget. State graphs mirroring a legacy monolithic behavior slice
    /// (e.g. standoff pursuit routing) can legitimately reach ~85 instructions, so the HFSM
    /// default of 64 is raised here while still guarding against runaway graphs.
    /// </summary>
    private const int LifecycleStepBudget = 128;

    private readonly GraphProgramRegistry _programs;
    private readonly IGraphRuntimeApi _api;
    private readonly IGameplayAdvanceGate _gate;
    private readonly GraphBehaviorCatalog _behavior;
    private readonly Dictionary<string, HfsmDefinition> _definitionsByHfsmId = new(StringComparer.Ordinal);

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

        ActiveCount = 0;
        foreach (ref Chunk chunk in World.Query(in BrainQuery))
        {
            Span<GraphActionBrain> brains = chunk.GetSpan<GraphActionBrain>();
            Span<HfsmState> states = chunk.GetSpan<HfsmState>();
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

                ref HfsmState state = ref states[index];
                WriteOrderGlue(actor, in buffers[index]);
                if (!state.Bound)
                {
                    // Bind on first sight: enter the definition's default leaf path.
                    WriteBirthState(actor, in brain);
                    state.LeafIndex = EnterDefaultPath(actor, brain.HfsmId);
                    ActiveCount++;
                    state.Bound = true;
                    state.StateTicks = 0;
                    // fall through so an active order on the bind frame transitions same-tick
                }

                Advance(actor, in brain, ref state);
                state.StateTicks++;
            }
        }
    }

    /// <summary>Current leaf state name for an entity (diagnostics / acceptance assertions).</summary>
    public bool TryGetLeafStateName(Entity actor, out string stateName)
    {
        if (World.TryGet<GraphActionBrain>(actor, out var brain) &&
            !string.IsNullOrEmpty(brain.HfsmId) &&
            World.TryGet<HfsmState>(actor, out var state) &&
            state.LeafIndex != HfsmState.NoState)
        {
            HfsmDefinition definition = ResolveDefinition(brain.HfsmId);
            stateName = definition.States[state.LeafIndex].Name;
            return true;
        }

        stateName = string.Empty;
        return false;
    }

    /// <summary>How many entities currently carry an HFSM behavior (diagnostics).</summary>
    public int ActiveCount { get; private set; }

    private int EnterDefaultPath(Entity actor, string hfsmId)
    {
        HfsmDefinition definition = ResolveDefinition(hfsmId);
        int leaf = definition.ResolveDefaultLeaf(definition.RootIndex);
        RunEnterPath(actor, leaf, definition, Array.Empty<int>()); // from root (no prior path)
        ActiveCount++;
        return leaf;
    }

    private void Advance(Entity actor, in GraphActionBrain brain, ref HfsmState state)
    {
        HfsmDefinition definition = ResolveDefinition(brain.HfsmId);
        int oldLeaf = state.LeafIndex;
        if (TryPickTransition(actor, oldLeaf, definition, out HfsmTransition? chosen))
        {
            int[] priorPath = PathToTarget(oldLeaf, definition);
            state.LeafIndex = chosen.Value.ToState;
            RunEnterPath(actor, state.LeafIndex, definition, priorPath);
            state.StateTicks = 0;
            RunTickCallbacks(actor, state.LeafIndex, definition);
            return;
        }

        // No transition from the leaf: walk ancestors and try their transitions.
        int parent = definition.States[oldLeaf].ParentIndex;
        while (parent >= 0)
        {
            if (TryPickTransition(actor, parent, definition, out chosen))
            {
                int[] priorPath = PathToTarget(oldLeaf, definition);
                state.LeafIndex = chosen.Value.ToState;
                RunEnterPath(actor, state.LeafIndex, definition, priorPath);
                state.StateTicks = 0;
                RunTickCallbacks(actor, state.LeafIndex, definition);
                return;
            }

            parent = definition.States[parent].ParentIndex;
        }

        RunTickCallbacks(actor, state.LeafIndex, definition);
    }

    private bool TryPickTransition(Entity actor, int fromState, HfsmDefinition definition, out HfsmTransition? chosen)
    {
        var host = new GraphProgramHfsmHost(_programs, World, _api, budgetSteps: LifecycleStepBudget);
        host.SetAgentCaster(0, actor);
        ReadOnlySpan<HfsmTransition> span = definition.GetTransitionsFromState(fromState);
        int bestPriority = int.MinValue;
        int best = -1;
        for (int i = 0; i < span.Length; i++)
        {
            HfsmTransition tr = span[i];
            if (tr.ConditionGraphId > 0 && !host.EvalCondition(0, tr.ConditionGraphId))
            {
                continue;
            }

            if (best < 0 || tr.Priority >= bestPriority)
            {
                bestPriority = tr.Priority;
                best = i;
            }
        }

        chosen = best < 0 ? null : span[best];
        return chosen != null;
    }

    private void RunEnterPath(Entity actor, int toLeaf, HfsmDefinition definition, int[] priorPath)
    {
        var host = new GraphProgramHfsmHost(_programs, World, _api, budgetSteps: LifecycleStepBudget);
        host.SetAgentCaster(0, actor);
        int[] target = PathToTarget(toLeaf, definition);
        // Enter only states not already present in the prior path (deep to shallow).
        int enterAt = 0;
        while (enterAt < target.Length && enterAt < priorPath.Length && target[enterAt] == priorPath[enterAt])
        {
            enterAt++;
        }

        for (int i = target.Length - 1; i >= enterAt; i--)
        {
            int enterGraph = definition.States[target[i]].OnEnterGraphId;
            if (enterGraph > 0)
            {
                host.RunAction(0, enterGraph);
            }
        }
    }

    private void ExitUpTo(Entity actor, int fromLeaf, HfsmDefinition definition)
    {
        var host = new GraphProgramHfsmHost(_programs, World, _api, budgetSteps: LifecycleStepBudget);
        host.SetAgentCaster(0, actor);
        int[] path = PathToTarget(fromLeaf, definition);
        for (int i = path.Length - 1; i >= 0; i--)
        {
            int exitGraph = definition.States[path[i]].OnExitGraphId;
            if (exitGraph > 0)
            {
                host.RunAction(0, exitGraph);
            }
        }
    }

    private void RunTickCallbacks(Entity actor, int leaf, HfsmDefinition definition)
    {
        var host = new GraphProgramHfsmHost(_programs, World, _api, budgetSteps: LifecycleStepBudget);
        host.SetAgentCaster(0, actor);
        int[] path = PathToTarget(leaf, definition);
        for (int i = path.Length - 1; i >= 0; i--)
        {
            int tickGraph = definition.States[path[i]].OnTickGraphId;
            if (tickGraph > 0)
            {
                host.RunAction(0, tickGraph);
            }
        }
    }

    private static int[] PathToTarget(int leaf, HfsmDefinition definition)
    {
        var path = new List<int>();
        int current = leaf;
        while (current >= 0)
        {
            path.Add(current);
            current = definition.States[current].ParentIndex;
        }

        path.Reverse();
        return path.ToArray();
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
}
