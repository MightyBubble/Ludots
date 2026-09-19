using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.AI.BehaviorTree;
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
/// Per-entity behavior tree driver, following the same component-carried pattern as
/// HfsmBrainHostSystem (and, before both, AnimatorRuntimeState): each behavior entity
/// carries its own <see cref="BtState"/> component; the system queries those entities
/// directly and ticks each one's tree from the root using the shared
/// <see cref="BehaviorTreeDefinition"/>. Entity lifecycle is entirely ECS — no parallel
/// agent pool, no index, no release.
///
/// Tree evaluation per tick:
///   Sequence  → children in order; any Failure → Failure; all Success → Success
///   Selector  → children in order; any Success → Success; all Failure → Failure
///   Condition → func_lib pure graph (zero side effects, must halt returning bool)
///   Action    → action_lib graph (side effects allowed, must halt within budget)
///
/// Condition nodes resolve from FuncLib at load time (same chain as HFSM transition
/// conditions); Action nodes resolve from ActionLib. All leaves must halt — Yield-resume
/// is deferred until a per-entity resident-frame need arises.
/// </summary>
public sealed class BtBrainHostSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription BrainQuery = new QueryDescription()
        .WithAll<GraphActionBrain, BtState, OrderBuffer, PlayerOwner, BlackboardIntBuffer, BlackboardEntityBuffer>();

    private const int LeafStepBudget = 128;

    private readonly GraphProgramRegistry _programs;
    private readonly IGraphRuntimeApi _api;
    private readonly IGameplayAdvanceGate _gate;
    private readonly GraphBehaviorCatalog _behavior;
    private readonly Dictionary<string, BehaviorTreeDefinition> _definitionsByBtId = new(StringComparer.Ordinal);
    private readonly GraphProgramHfsmHost _host;
    private int _tick;

    public BtBrainHostSystem(
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
        _host = new GraphProgramHfsmHost(_programs, World, _api, budgetSteps: LeafStepBudget);
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
            Span<BtState> states = chunk.GetSpan<BtState>();
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity actor = Unsafe.Add(ref first, index);
                ref readonly GraphActionBrain brain = ref brains[index];
                if (string.IsNullOrEmpty(brain.BtId))
                {
                    continue;
                }

                if ((_tick % Math.Max(1, brain.ThinkEveryNTicks)) != 0)
                {
                    continue;
                }

                ref BtState state = ref states[index];
                WriteOrderGlue(actor, in buffers[index]);
                if (!state.Bound)
                {
                    WriteBirthState(actor, in brain);
                    state.Bound = true;
                    state.Status = (byte)BehaviorTreeStatus.Running;
                    state.StateTicks = 0;
                }

                BehaviorTreeDefinition definition = ResolveDefinition(brain.BtId);
                _host.SetAgentCaster(0, actor);
                state.Status = (byte)TickNode(definition, definition.RootIndex);
                state.StateTicks++;
            }
        }
    }

    /// <summary>Current tree status for an entity (diagnostics / acceptance).</summary>
    public bool TryGetStatus(Entity actor, out BehaviorTreeStatus status)
    {
        if (World.TryGet<GraphActionBrain>(actor, out var brain) &&
            !string.IsNullOrEmpty(brain.BtId) &&
            World.TryGet<BtState>(actor, out var state) &&
            state.Bound)
        {
            status = (BehaviorTreeStatus)state.Status;
            return true;
        }

        status = BehaviorTreeStatus.Inactive;
        return false;
    }

    private BehaviorTreeStatus TickNode(BehaviorTreeDefinition definition, int nodeIndex)
    {
        BehaviorTreeNode node = definition.Nodes[nodeIndex];
        switch (node.Kind)
        {
            case BehaviorTreeNodeKind.Sequence:
                for (int i = 0; i < node.ChildCount; i++)
                {
                    var child = TickNode(definition, node.ChildStart + i);
                    if (child == BehaviorTreeStatus.Failure)
                    {
                        return BehaviorTreeStatus.Failure;
                    }

                    if (child == BehaviorTreeStatus.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }
                }

                return BehaviorTreeStatus.Success;

            case BehaviorTreeNodeKind.Selector:
                for (int i = 0; i < node.ChildCount; i++)
                {
                    var child = TickNode(definition, node.ChildStart + i);
                    if (child == BehaviorTreeStatus.Success)
                    {
                        return BehaviorTreeStatus.Success;
                    }

                    if (child == BehaviorTreeStatus.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }
                }

                return BehaviorTreeStatus.Failure;

            case BehaviorTreeNodeKind.Condition:
                return TickLeaf(node);

            case BehaviorTreeNodeKind.Action:
                return TickLeaf(node);

            default:
                throw new InvalidOperationException($"BT node kind '{node.Kind}' is not supported.");
        }
    }

    private BehaviorTreeStatus TickLeaf(BehaviorTreeNode node)
    {
        switch (node.Leaf)
        {
            case BehaviorTreeLeafBinding.AlwaysSuccess:
                return BehaviorTreeStatus.Success;
            case BehaviorTreeLeafBinding.AlwaysFailure:
                return BehaviorTreeStatus.Failure;
            case BehaviorTreeLeafBinding.HoldRunning:
                return BehaviorTreeStatus.Running;
            case BehaviorTreeLeafBinding.ScriptSlice:
                if (node.GraphId <= 0)
                {
                    throw new InvalidOperationException("BT ScriptSlice leaf has no resolved graph id.");
                }

                // The shared host enforces halt within budget; ReturnInt != 0 → Success.
                int returnInt = _host.RunActionForReturn(0, node.GraphId);
                return returnInt != 0 ? BehaviorTreeStatus.Success : BehaviorTreeStatus.Failure;
            default:
                throw new InvalidOperationException($"BT leaf binding '{node.Leaf}' is not supported.");
        }
    }

    private BehaviorTreeDefinition ResolveDefinition(string btId)
    {
        if (_definitionsByBtId.TryGetValue(btId, out BehaviorTreeDefinition? definition))
        {
            return definition;
        }

        definition = _behavior.RequireTree(btId);
        _definitionsByBtId[btId] = definition;
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
