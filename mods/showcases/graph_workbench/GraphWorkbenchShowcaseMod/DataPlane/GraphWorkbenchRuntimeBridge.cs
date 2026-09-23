using GraphWorkbenchShowcaseMod.Domain;
using Ludots.Core.Engine;

namespace GraphWorkbenchShowcaseMod.DataPlane;

public sealed class GraphWorkbenchRuntimeBridge
{
    private const string InactiveSource = "no-active-3d-showcase";
    private const string LevelGraphId = "level_blueprint_opening";
    private const string StanceGraphId = "rts_stance_fsm";
    private const string StanceFsmId = "rts.stance";
    private const string BehaviorGraphId = "complex_bt_selector";
    private const string BehaviorTreeId = "unit.assault_bt";
    private const string StressFsmGraphId = "stress_field_fsm";
    private const string StressBtGraphId = "stress_field_bt";

    private static readonly LiveRuntimeDescriptor[] RuntimeDescriptors =
    [
        new("GraphAiShowcase.LevelBlueprint.Runtime", "Level Blueprint"),
        new("GraphAiShowcase.StanceFsm.Runtime", "RTS Stance FSM"),
        new("GraphAiShowcase.ComplexBt.Runtime", "Complex Behavior Tree"),
        new("GraphAiShowcase.StressField.Runtime", "50k FSM+BT Benchmark")
    ];

    private readonly GameEngine? _engine;
    private string _selectedEntityId = string.Empty;
    private int _tick;
    private int _appliedRevision;

    public GraphWorkbenchRuntimeBridge(GameEngine? engine)
    {
        _engine = engine;
    }

    public int Tick => _tick;

    public string ResolveActiveDocumentScope()
    {
        if (!TryReadActiveRuntime(out _, out object runtime))
        {
            return GraphWorkbenchDocumentScopes.Library;
        }

        object runtimeSnapshot = ReadRequired<object>(runtime, "Snapshot");
        string mode = ReadRequired<string>(runtimeSnapshot, "Mode");
        return ResolveDocumentScope(mode);
    }

    public bool TrySelectEntity(GraphWorkbenchDocument document, int appliedRevision, string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return false;
        }

        GraphWorkbenchRuntimeSnapshot snapshot = CreateSnapshot(document, appliedRevision);
        for (int i = 0; i < snapshot.Entities.Length; i++)
        {
            if (string.Equals(snapshot.Entities[i].Id, entityId, StringComparison.Ordinal))
            {
                _selectedEntityId = entityId;
                return true;
            }
        }

        return false;
    }

    public void ApplyDocument(GraphWorkbenchDocument document, int appliedRevision)
    {
        ArgumentNullException.ThrowIfNull(document);
        _appliedRevision = appliedRevision;
        _tick++;
    }

    public void Advance(GraphWorkbenchDocument document, int appliedRevision, float dt)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!float.IsFinite(dt) || dt < 0f)
        {
            throw new InvalidOperationException($"Graph Workbench received invalid runtime dt '{dt}'.");
        }

        _appliedRevision = appliedRevision;
        _tick++;
    }

    public GraphWorkbenchRuntimeSnapshot CreateSnapshot(GraphWorkbenchDocument document, int appliedRevision)
    {
        ArgumentNullException.ThrowIfNull(document);
        _appliedRevision = appliedRevision;
        if (TryCreateLiveSnapshot(document, out GraphWorkbenchRuntimeSnapshot? snapshot) && snapshot != null)
        {
            return snapshot;
        }

        return new GraphWorkbenchRuntimeSnapshot(
            InactiveSource,
            string.Empty,
            _appliedRevision,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<GraphWorkbenchEntityDebug>(),
            Array.Empty<GraphWorkbenchAggregate>());
    }

    private bool TryCreateLiveSnapshot(
        GraphWorkbenchDocument document,
        out GraphWorkbenchRuntimeSnapshot? snapshot)
    {
        snapshot = null;
        if (!TryReadActiveRuntime(out LiveRuntimeDescriptor descriptor, out object runtime))
        {
            return false;
        }

        object runtimeSnapshot = ReadRequired<object>(runtime, "Snapshot");
        LiveShowcaseSnapshot live = ReadLiveSnapshot(runtimeSnapshot, descriptor.DisplayDomain);
        _tick = Math.Max(_tick, live.Tick);
        snapshot = live.Mode switch
        {
            "LevelBlueprint" => CreateLevelBlueprintSnapshot(document, live),
            "StanceFsm" => CreateStanceFsmSnapshot(document, live),
            "ComplexBt" => CreateComplexBtSnapshot(document, live),
            "StressField" => CreateStressFieldSnapshot(document, live),
            _ => throw new InvalidOperationException($"Graph Workbench does not understand graph showcase mode '{live.Mode}'.")
        };
        return true;
    }

    private bool TryReadActiveRuntime(out LiveRuntimeDescriptor descriptor, out object runtime)
    {
        descriptor = default;
        runtime = null!;
        if (_engine == null)
        {
            return false;
        }

        for (int i = 0; i < RuntimeDescriptors.Length; i++)
        {
            LiveRuntimeDescriptor candidate = RuntimeDescriptors[i];
            if (!_engine.GlobalContext.TryGetValue(candidate.RuntimeKey, out object? candidateRuntime) ||
                candidateRuntime == null)
            {
                continue;
            }

            if (!ReadRequired<bool>(candidateRuntime, "IsActive"))
            {
                continue;
            }

            descriptor = candidate;
            runtime = candidateRuntime;
            return true;
        }

        return false;
    }

    private GraphWorkbenchRuntimeSnapshot CreateLevelBlueprintSnapshot(
        GraphWorkbenchDocument document,
        LiveShowcaseSnapshot live)
    {
        string graphNodeId = ResolveLevelGraphNode(live.State);
        return new GraphWorkbenchRuntimeSnapshot(
            SourceName(live),
            string.Empty,
            _appliedRevision,
            LevelGraphId,
            graphNodeId,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<GraphWorkbenchEntityDebug>(),
            [
                new("3D scene", 1),
                new("completed triggers", live.CompletedTasks)
            ]);
    }

    private GraphWorkbenchRuntimeSnapshot CreateStanceFsmSnapshot(
        GraphWorkbenchDocument document,
        LiveShowcaseSnapshot live)
    {
        var rows = new GraphWorkbenchEntityDebug[live.Actors.Length];
        for (int i = 0; i < live.Actors.Length; i++)
        {
            LiveActorSnapshot actor = live.Actors[i];
            string fsmNodeId = ResolveStanceNode(actor.State);
            rows[i] = new GraphWorkbenchEntityDebug(
                actor.InstanceId,
                actor.Name,
                live.Title,
                actor.WorldXCm,
                actor.WorldYCm,
                StanceGraphId,
                ResolveStanceGraphNode(actor.State),
                ResolveNodeLabel(document, StanceGraphId, ResolveStanceGraphNode(actor.State), actor.StateLabel),
                StanceFsmId,
                fsmNodeId,
                actor.StateLabel,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        return CreateSelectedSnapshot(SourceName(live), rows, StanceGraphId, StanceFsmId, string.Empty);
    }

    private GraphWorkbenchRuntimeSnapshot CreateComplexBtSnapshot(
        GraphWorkbenchDocument document,
        LiveShowcaseSnapshot live)
    {
        var rows = new GraphWorkbenchEntityDebug[live.Actors.Length];
        for (int i = 0; i < live.Actors.Length; i++)
        {
            LiveActorSnapshot actor = live.Actors[i];
            string behaviorNodeId = ResolveBehaviorNode(actor.TaskId);
            string graphNodeId = ResolveBehaviorGraphNode(actor.TaskId);
            rows[i] = new GraphWorkbenchEntityDebug(
                actor.InstanceId,
                actor.Name,
                live.Title,
                actor.WorldXCm,
                actor.WorldYCm,
                BehaviorGraphId,
                graphNodeId,
                ResolveNodeLabel(document, BehaviorGraphId, graphNodeId, actor.TaskLabel),
                string.Empty,
                string.Empty,
                string.Empty,
                BehaviorTreeId,
                behaviorNodeId,
                actor.TaskLabel);
        }

        return CreateSelectedSnapshot(SourceName(live), rows, BehaviorGraphId, string.Empty, BehaviorTreeId);
    }

    private GraphWorkbenchRuntimeSnapshot CreateStressFieldSnapshot(
        GraphWorkbenchDocument document,
        LiveShowcaseSnapshot live)
    {
        LiveStressFieldSnapshot stress = live.StressField;
        string currentGraphId = stress.BtGraphExecutionsLastTick > 0 ? StressBtGraphId : StressFsmGraphId;
        string currentGraphNodeId = string.Equals(currentGraphId, StressBtGraphId, StringComparison.Ordinal)
            ? "stress.bt.task"
            : "stress.fsm.branch";

        return new GraphWorkbenchRuntimeSnapshot(
            SourceName(live),
            string.Empty,
            _appliedRevision,
            currentGraphId,
            currentGraphNodeId,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<GraphWorkbenchEntityDebug>(),
            [
                new("ECS entities", stress.EcsEntityCount),
                new("visible dots", stress.VisiblePrimitiveCount),
                new("FSM branches", stress.FsmBranchMask),
                new("BT tasks", stress.BtTaskMask)
            ]);
    }

    private GraphWorkbenchRuntimeSnapshot CreateSelectedSnapshot(
        string source,
        GraphWorkbenchEntityDebug[] rows,
        string fallbackGraphId,
        string fallbackFsmId,
        string fallbackBehaviorTreeId)
    {
        GraphWorkbenchEntityDebug? selected = null;
        for (int i = 0; i < rows.Length; i++)
        {
            if (string.Equals(rows[i].Id, _selectedEntityId, StringComparison.Ordinal))
            {
                selected = rows[i];
                break;
            }
        }

        selected ??= rows.Length > 0 ? rows[0] : null;
        _selectedEntityId = selected?.Id ?? string.Empty;
        return new GraphWorkbenchRuntimeSnapshot(
            source,
            _selectedEntityId,
            _appliedRevision,
            selected?.CurrentGraphId ?? fallbackGraphId,
            selected?.CurrentGraphNodeId ?? string.Empty,
            selected?.CurrentStateMachineId ?? fallbackFsmId,
            selected?.CurrentStateNodeId ?? string.Empty,
            selected?.CurrentBehaviorTreeId ?? fallbackBehaviorTreeId,
            selected?.CurrentBehaviorNodeId ?? string.Empty,
            rows,
            CountByDomain(rows));
    }

    private static LiveShowcaseSnapshot ReadLiveSnapshot(object snapshot, string displayDomain)
    {
        object[] actorObjects = ReadArray(snapshot, "Actors");
        var actors = new LiveActorSnapshot[actorObjects.Length];
        for (int i = 0; i < actorObjects.Length; i++)
        {
            object actor = actorObjects[i];
            actors[i] = new LiveActorSnapshot(
                ReadRequired<string>(actor, "Name"),
                ReadRequired<string>(actor, "InstanceId"),
                ReadRequired<int>(actor, "State"),
                ReadRequired<string>(actor, "StateLabel"),
                ReadRequired<int>(actor, "Intent"),
                ReadRequired<string>(actor, "IntentLabel"),
                ReadRequired<string>(actor, "ActionLabel"),
                ReadRequired<int>(actor, "BtNode"),
                ReadRequired<int>(actor, "TaskId"),
                ReadRequired<string>(actor, "TaskLabel"),
                ReadRequired<int>(actor, "TaskRemainingTicks"),
                ReadRequired<int>(actor, "Health"),
                ReadRequired<int>(actor, "EnemyDistanceCm"),
                ReadRequired<int>(actor, "WorldXCm"),
                ReadRequired<int>(actor, "WorldYCm"));
        }

        object stress = ReadRequired<object>(snapshot, "StressField");
        return new LiveShowcaseSnapshot(
            ReadRequired<string>(snapshot, "ShowcaseId"),
            ReadRequired<string>(snapshot, "Mode"),
            ReadRequired<string>(snapshot, "Title"),
            ReadRequired<string>(snapshot, "GraphProgramId"),
            ReadRequired<int>(snapshot, "Tick"),
            ReadRequired<int>(snapshot, "State"),
            ReadRequired<string>(snapshot, "StateLabel"),
            ReadRequired<int>(snapshot, "Intent"),
            ReadRequired<string>(snapshot, "IntentLabel"),
            ReadRequired<int>(snapshot, "CompletedTasks"),
            string.IsNullOrWhiteSpace(displayDomain) ? ReadRequired<string>(snapshot, "Title") : displayDomain,
            ReadStressField(stress),
            actors);
    }

    private static LiveStressFieldSnapshot ReadStressField(object stress)
    {
        return new LiveStressFieldSnapshot(
            ReadRequired<int>(stress, "EcsEntityCount"),
            ReadRequired<int>(stress, "VisiblePrimitiveCount"),
            ReadRequired<long>(stress, "FsmGraphExecutionsLastTick"),
            ReadRequired<long>(stress, "BtGraphExecutionsLastTick"),
            ReadRequired<int>(stress, "FsmBranchMask"),
            ReadRequired<int>(stress, "BtTaskMask"));
    }

    private static string ResolveDocumentScope(string mode) => mode switch
    {
        "LevelBlueprint" => GraphWorkbenchDocumentScopes.LevelBlueprint,
        "StanceFsm" => GraphWorkbenchDocumentScopes.StanceFsm,
        "ComplexBt" => GraphWorkbenchDocumentScopes.ComplexBt,
        "StressField" => GraphWorkbenchDocumentScopes.StressField,
        _ => throw new InvalidOperationException($"Graph Workbench cannot resolve a document scope for graph showcase mode '{mode}'.")
    };

    private static string ResolveLevelGraphNode(int state) => state switch
    {
        0 => "level.door_trigger",
        1 => "level.patrol_trigger",
        2 => "level.beacon_trigger",
        3 => "level.exit_trigger",
        _ => "level.door_trigger"
    };

    private static string ResolveStanceNode(int state) => state switch
    {
        0 => "stance.hold",
        1 => "stance.return",
        2 => "stance.defend",
        3 => "stance.attack",
        _ => "stance.hold"
    };

    private static string ResolveStanceGraphNode(int state) => state switch
    {
        0 => "rts.write.hold_state",
        1 => "rts.write.return_state",
        2 => "rts.write.defend_state",
        3 => "rts.write.attack_state",
        _ => "rts.write.hold_state"
    };

    private static string ResolveBehaviorNode(int taskId) => taskId switch
    {
        1 => "bt.select_cover",
        2 => "bt.suppress_target",
        3 => "bt.call_reinforcement",
        4 => "bt.scout_sweep",
        5 => "bt.reposition",
        _ => "bt.selector"
    };

    private static string ResolveBehaviorGraphNode(int taskId) => taskId switch
    {
        1 => "bt.task.select_cover",
        2 => "bt.task.suppress_target",
        3 => "bt.task.call_reinforcement",
        4 => "bt.task.scout_sweep",
        5 => "bt.task.reposition",
        _ => "bt.health"
    };

    private static string ResolveStressFsmNode(int branchMask)
    {
        if ((branchMask & 8) != 0)
        {
            return "stance.attack";
        }

        if ((branchMask & 4) != 0)
        {
            return "stance.defend";
        }

        if ((branchMask & 2) != 0)
        {
            return "stance.return";
        }

        return "stance.hold";
    }

    private static string ResolveStressBehaviorNode(int taskMask)
    {
        if ((taskMask & 16) != 0)
        {
            return "bt.reposition";
        }

        if ((taskMask & 8) != 0)
        {
            return "bt.scout_sweep";
        }

        if ((taskMask & 4) != 0)
        {
            return "bt.call_reinforcement";
        }

        if ((taskMask & 2) != 0)
        {
            return "bt.suppress_target";
        }

        return "bt.select_cover";
    }

    private static string ResolveNodeLabel(
        GraphWorkbenchDocument document,
        string graphId,
        string nodeId,
        string fallback)
    {
        for (int i = 0; i < document.Graphs.Count; i++)
        {
            GraphWorkbenchGraphDocument graph = document.Graphs[i];
            if (!string.Equals(graph.Id, graphId, StringComparison.Ordinal))
            {
                continue;
            }

            for (int n = 0; n < graph.Nodes.Count; n++)
            {
                if (string.Equals(graph.Nodes[n].Id, nodeId, StringComparison.Ordinal))
                {
                    return graph.Nodes[n].Label;
                }
            }
        }

        return fallback;
    }

    private static GraphWorkbenchAggregate[] CountByDomain(GraphWorkbenchEntityDebug[] rows)
    {
        return rows
            .GroupBy(static row => row.Domain, StringComparer.Ordinal)
            .Select(static group => new GraphWorkbenchAggregate(group.Key, group.Count()))
            .OrderBy(static row => row.Domain, StringComparer.Ordinal)
            .ToArray();
    }

    private static string SourceName(LiveShowcaseSnapshot live) =>
        $"live-3d:{live.ShowcaseId}";

    private static object? ReadProperty(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"Graph Workbench expected property '{propertyName}' on runtime type '{source.GetType().FullName}'.");
        }

        return property.GetValue(source);
    }

    private static T ReadRequired<T>(object source, string propertyName)
    {
        object? value = ReadProperty(source, propertyName);
        if (value == null)
        {
            throw new InvalidOperationException($"Graph Workbench runtime property '{propertyName}' was null.");
        }

        if (value is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"Graph Workbench expected property '{propertyName}' on '{source.GetType().FullName}' to be {typeof(T).Name}, got {value.GetType().Name}.");
    }

    private static object[] ReadArray(object source, string propertyName)
    {
        object value = ReadRequired<object>(source, propertyName);
        if (value is not Array array)
        {
            throw new InvalidOperationException($"Graph Workbench expected property '{propertyName}' to be an array.");
        }

        var items = new object[array.Length];
        for (int i = 0; i < array.Length; i++)
        {
            items[i] = array.GetValue(i)
                ?? throw new InvalidOperationException($"Graph Workbench runtime array '{propertyName}' contained null.");
        }

        return items;
    }

    private readonly record struct LiveRuntimeDescriptor(string RuntimeKey, string DisplayDomain);

    private sealed record LiveShowcaseSnapshot(
        string ShowcaseId,
        string Mode,
        string Title,
        string GraphProgramId,
        int Tick,
        int State,
        string StateLabel,
        int Intent,
        string IntentLabel,
        int CompletedTasks,
        string DisplayDomain,
        LiveStressFieldSnapshot StressField,
        LiveActorSnapshot[] Actors);

    private sealed record LiveActorSnapshot(
        string Name,
        string InstanceId,
        int State,
        string StateLabel,
        int Intent,
        string IntentLabel,
        string ActionLabel,
        int BtNode,
        int TaskId,
        string TaskLabel,
        int TaskRemainingTicks,
        int Health,
        int EnemyDistanceCm,
        int WorldXCm,
        int WorldYCm);

    private sealed record LiveStressFieldSnapshot(
        int EcsEntityCount,
        int VisiblePrimitiveCount,
        long FsmGraphExecutionsLastTick,
        long BtGraphExecutionsLastTick,
        int FsmBranchMask,
        int BtTaskMask);
}
