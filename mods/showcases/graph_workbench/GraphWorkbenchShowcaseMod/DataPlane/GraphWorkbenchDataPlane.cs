using System.Text.Json;
using GraphWorkbenchShowcaseMod.Domain;
using Ludots.Core.Engine;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.WebUI.DataPlane;

namespace GraphWorkbenchShowcaseMod.DataPlane;

public sealed class GraphWorkbenchDataPlane : IWebUiTopicProducer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly GraphWorkbenchRuntimeBridge _runtime;
    private GraphWorkbenchDocument _draft = null!;
    private GraphWorkbenchDocument _running = null!;
    private GraphWorkbenchCompileResult _compileResult = null!;
    private string _documentScope = GraphWorkbenchDocumentScopes.Library;
    private string _lastCommand = "startup";
    private string _lastCommandStatus = "idle";
    private int _appliedRevision;

    public GraphWorkbenchDataPlane()
        : this(null)
    {
    }

    public GraphWorkbenchDataPlane(GameEngine? engine)
    {
        _runtime = new GraphWorkbenchRuntimeBridge(engine);
        ReplaceDocumentForScope(_runtime.ResolveActiveDocumentScope(), notifyRuntime: false);
    }

    public string Topic => GraphWorkbenchShowcaseIds.WebUiTopic;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        lock (_sync)
        {
            EnsureRuntimeScopedDocument();
            GraphWorkbenchSnapshot snapshot = CreateSnapshot();
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            packet = new WebUiOutboundPacket(
                context.SessionId,
                Topic,
                context.RequestId == 0 ? WebUiPacketKind.Delta : WebUiPacketKind.Snapshot,
                WebUiDeliverySemantics.LatestWins,
                payload,
                "application/json",
                context.RequestId,
                _runtime.Tick);
            return true;
        }
    }

    public void AdvanceRuntime(float dt)
    {
        lock (_sync)
        {
            EnsureRuntimeScopedDocument();
            _runtime.Advance(_running, _appliedRevision, dt);
        }
    }

    public WebUiCommandResult ApplyCommand(WebUiCommandRequest request)
    {
        lock (_sync)
        {
            _lastCommand = request.Name;
            WebUiCommandResult result = request.Name switch
            {
                GraphWorkbenchShowcaseIds.SelectEntityCommand => SelectEntity(request.Payload),
                GraphWorkbenchShowcaseIds.EditDocumentCommand => EditDocument(request.Payload),
                GraphWorkbenchShowcaseIds.CompileDocumentCommand => CompileDocument(request.Payload),
                GraphWorkbenchShowcaseIds.SetActiveDocumentCommand => SetActiveDocument(request.Payload),
                _ => WebUiCommandResult.Fail("unknown_command", $"Unsupported Graph workbench command '{request.Name}'.")
            };
            _lastCommandStatus = result.Success ? "ack" : $"{result.ErrorCode}: {result.Message}";
            return result;
        }
    }

    private WebUiCommandResult SelectEntity(JsonElement payload)
    {
        if (!payload.TryGetProperty("entityId", out JsonElement entityIdElement) ||
            entityIdElement.ValueKind != JsonValueKind.String)
        {
            return WebUiCommandResult.Fail("invalid_payload", "selectEntity requires entityId.");
        }

        string entityId = entityIdElement.GetString() ?? string.Empty;
        return _runtime.TrySelectEntity(_running, _appliedRevision, entityId)
            ? WebUiCommandResult.Ok()
            : WebUiCommandResult.Fail("unknown_entity", $"Graph workbench live 3D entity '{entityId}' does not exist in the active showcase.");
    }

    private WebUiCommandResult EditDocument(JsonElement payload)
    {
        if (!TryReadDocument(payload, out GraphWorkbenchDocument? document, out string error))
        {
            return WebUiCommandResult.Fail("invalid_document", error);
        }

        _draft = document;
        _compileResult = GraphWorkbenchCompileResult.Pending(_draft.Revision);
        return WebUiCommandResult.Ok();
    }

    private WebUiCommandResult CompileDocument(JsonElement payload)
    {
        GraphWorkbenchDocument document = _draft;
        if (payload.TryGetProperty("document", out _))
        {
            if (!TryReadDocument(payload, out GraphWorkbenchDocument? parsed, out string error))
            {
                return WebUiCommandResult.Fail("invalid_document", error);
            }

            document = parsed;
            _draft = parsed;
        }

        GraphWorkbenchCompileResult result = GraphWorkbenchDocumentCompiler.Compile(document, _appliedRevision);
        _compileResult = result;
        if (!result.Success)
        {
            return WebUiCommandResult.Fail("compile_failed", result.Summary);
        }

        _running = CloneDocument(document);
        _appliedRevision = document.Revision;
        _compileResult = result;
        _runtime.ApplyDocument(_running, _appliedRevision);
        return WebUiCommandResult.Ok();
    }

    private void EnsureRuntimeScopedDocument()
    {
        _ = EnsureRuntimeScopedDocument(_runtime.ResolveActiveDocumentScope());
    }

    private bool EnsureRuntimeScopedDocument(string activeScope)
    {
        if (string.Equals(_documentScope, activeScope, StringComparison.Ordinal))
        {
            return false;
        }

        if (HasUnappliedDraft())
        {
            _lastCommand = "runtimeScope";
            _lastCommandStatus = $"blocked: draft revision {_draft.Revision} is not compiled.";
            return false;
        }

        ReplaceDocumentForScope(activeScope, notifyRuntime: true);
        _lastCommand = "runtimeScope";
        _lastCommandStatus = "ack";
        return true;
    }

    private void ReplaceDocumentForScope(string scope, bool notifyRuntime)
    {
        GraphWorkbenchDocument document = GraphWorkbenchSeedDocument.CreateForRuntimeScope(scope);
        GraphWorkbenchCompileResult compileResult = GraphWorkbenchDocumentCompiler.Compile(document, appliedRevision: 0);
        if (!compileResult.Success)
        {
            throw new InvalidOperationException($"Seed Graph workbench document for scope '{scope}' failed to compile.");
        }

        _draft = document;
        _running = CloneDocument(document);
        _compileResult = compileResult;
        _appliedRevision = document.Revision;
        _documentScope = GraphWorkbenchSeedDocument.NormalizeRuntimeScope(scope);
        if (notifyRuntime)
        {
            _runtime.ApplyDocument(_running, _appliedRevision);
        }
    }

    private bool HasUnappliedDraft() =>
        !_compileResult.Success || _draft.Revision != _running.Revision;

    private WebUiCommandResult SetActiveDocument(JsonElement payload)
    {
        if (payload.TryGetProperty("graphId", out JsonElement graphElement) &&
            graphElement.ValueKind == JsonValueKind.String)
        {
            _draft.ActiveGraphId = graphElement.GetString() ?? _draft.ActiveGraphId;
        }

        if (payload.TryGetProperty("stateMachineId", out JsonElement fsmElement) &&
            fsmElement.ValueKind == JsonValueKind.String)
        {
            _draft.ActiveStateMachineId = fsmElement.GetString() ?? _draft.ActiveStateMachineId;
        }

        if (payload.TryGetProperty("behaviorTreeId", out JsonElement btElement) &&
            btElement.ValueKind == JsonValueKind.String)
        {
            _draft.ActiveBehaviorTreeId = btElement.GetString() ?? _draft.ActiveBehaviorTreeId;
        }

        return WebUiCommandResult.Ok();
    }

    private GraphWorkbenchSnapshot CreateSnapshot()
    {
        GraphWorkbenchRuntimeSnapshot runtime = CreateRuntimeSnapshot();
        if (EnsureRuntimeScopedDocument(GraphWorkbenchSeedDocument.ResolveRuntimeScope(runtime)))
        {
            runtime = CreateRuntimeSnapshot();
        }

        return new GraphWorkbenchSnapshot(
            _runtime.Tick,
            _draft,
            _compileResult,
            runtime,
            new GraphWorkbenchCommandSnapshot(_lastCommand, _lastCommandStatus),
            GraphWorkbenchPalette.Create());
    }

    private GraphWorkbenchRuntimeSnapshot CreateRuntimeSnapshot()
    {
        return _runtime.CreateSnapshot(_running, _appliedRevision);
    }

    private static bool TryReadDocument(JsonElement payload, out GraphWorkbenchDocument document, out string error)
    {
        document = null!;
        error = string.Empty;
        if (!payload.TryGetProperty("document", out JsonElement documentElement))
        {
            error = "Command payload requires document.";
            return false;
        }

        try
        {
            GraphWorkbenchDocument? parsed = documentElement.Deserialize<GraphWorkbenchDocument>(JsonOptions);
            if (parsed == null)
            {
                error = "Document payload is empty.";
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static GraphWorkbenchDocument CloneDocument(GraphWorkbenchDocument document)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        return JsonSerializer.Deserialize<GraphWorkbenchDocument>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Could not clone Graph workbench document.");
    }
}

public sealed class GraphWorkbenchCommandHandler : IWebUiCommandHandler
{
    private readonly GraphWorkbenchDataPlane _dataPlane;

    public GraphWorkbenchCommandHandler(GraphWorkbenchDataPlane dataPlane)
    {
        _dataPlane = dataPlane ?? throw new ArgumentNullException(nameof(dataPlane));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(
        WebUiCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_dataPlane.ApplyCommand(request));
    }
}

public sealed class GraphWorkbenchGenerationResolver : IWebUiEntityGenerationResolver
{
    public bool IsCurrent(WebUiEntityRef entityRef) =>
        entityRef.StableId <= 0 && entityRef.Generation <= 0;
}

public sealed class GraphWorkbenchPermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        GraphWorkbenchShowcaseIds.SelectEntityCommand,
        GraphWorkbenchShowcaseIds.EditDocumentCommand,
        GraphWorkbenchShowcaseIds.CompileDocumentCommand,
        GraphWorkbenchShowcaseIds.SetActiveDocumentCommand
    };

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (AllowedCommands.Contains(request.Name))
        {
            error = string.Empty;
            return true;
        }

        error = $"Command '{request.Name}' is not allowed in GraphWorkbenchShowcaseMod.";
        return false;
    }
}

public sealed record GraphWorkbenchSnapshot(
    int Tick,
    GraphWorkbenchDocument Document,
    GraphWorkbenchCompileResult Compile,
    GraphWorkbenchRuntimeSnapshot Runtime,
    GraphWorkbenchCommandSnapshot Command,
    GraphWorkbenchPalette Palette);

public sealed record GraphWorkbenchCommandSnapshot(string LastCommand, string LastCommandStatus);

public sealed record GraphWorkbenchRuntimeSnapshot(
    string Source,
    string SelectedEntityId,
    int AppliedRevision,
    string CurrentGraphId,
    string CurrentGraphNodeId,
    string CurrentStateMachineId,
    string CurrentStateNodeId,
    string CurrentBehaviorTreeId,
    string CurrentBehaviorNodeId,
    GraphWorkbenchEntityDebug[] Entities,
    GraphWorkbenchAggregate[] Aggregates);

public sealed record GraphWorkbenchEntityDebug(
    string Id,
    string Label,
    string Domain,
    int X,
    int Y,
    string CurrentGraphId,
    string CurrentGraphNodeId,
    string CurrentGraphLabel,
    string CurrentStateMachineId,
    string CurrentStateNodeId,
    string CurrentStateLabel,
    string CurrentBehaviorTreeId,
    string CurrentBehaviorNodeId,
    string CurrentBehaviorLabel);

public sealed record GraphWorkbenchAggregate(string Domain, int Count);

public sealed record GraphWorkbenchPalette(string[] GraphOps, string[] FsmNodeKinds, string[] BehaviorNodeKinds)
{
    public static GraphWorkbenchPalette Create() =>
        new(
            Enum.GetNames<GraphNodeOp>().Where(static op => op != nameof(GraphNodeOp.None)).ToArray(),
            ["State", "Transition", "Entry"],
            ["Root", "Selector", "Sequence", "Condition", "Task"]);
}

internal static class GraphWorkbenchDocumentScopes
{
    public const string Library = "Library";
    public const string LevelBlueprint = "LevelBlueprint";
    public const string StanceFsm = "StanceFsm";
    public const string ComplexBt = "ComplexBt";
    public const string StressField = "StressField";
}

internal static class GraphWorkbenchSeedDocument
{
    private static readonly string[] LevelBlueprintGraphs =
    [
        "level_blueprint_opening",
        "level.impl.door_trigger",
        "level.impl.patrol_trigger",
        "level.impl.beacon_trigger",
        "level.impl.exit_trigger"
    ];

    private static readonly string[] StanceFsmGraphs = ["rts_stance_fsm"];
    private static readonly string[] ComplexBtGraphs = ["complex_bt_selector"];
    private static readonly string[] StressFieldGraphs = ["stress_field_fsm", "stress_field_bt"];

    public static GraphWorkbenchDocument Create()
    {
        return CreateLibrary();
    }

    public static GraphWorkbenchDocument CreateForRuntimeScope(string scope)
    {
        return NormalizeRuntimeScope(scope) switch
        {
            GraphWorkbenchDocumentScopes.Library => CreateLibrary(),
            GraphWorkbenchDocumentScopes.LevelBlueprint => Scope(
                LevelBlueprintGraphs,
                Array.Empty<string>(),
                Array.Empty<string>(),
                activeGraphId: "level_blueprint_opening",
                activeStateMachineId: string.Empty,
                activeBehaviorTreeId: string.Empty),
            GraphWorkbenchDocumentScopes.StanceFsm => Scope(
                StanceFsmGraphs,
                ["rts.stance"],
                Array.Empty<string>(),
                activeGraphId: "rts_stance_fsm",
                activeStateMachineId: "rts.stance",
                activeBehaviorTreeId: string.Empty),
            GraphWorkbenchDocumentScopes.ComplexBt => Scope(
                ComplexBtGraphs,
                Array.Empty<string>(),
                ["unit.assault_bt"],
                activeGraphId: "complex_bt_selector",
                activeStateMachineId: string.Empty,
                activeBehaviorTreeId: "unit.assault_bt"),
            GraphWorkbenchDocumentScopes.StressField => Scope(
                StressFieldGraphs,
                Array.Empty<string>(),
                Array.Empty<string>(),
                activeGraphId: "stress_field_bt",
                activeStateMachineId: string.Empty,
                activeBehaviorTreeId: string.Empty),
            string unsupported => throw new InvalidOperationException($"Graph Workbench cannot create a document for runtime scope '{unsupported}'.")
        };
    }

    public static string NormalizeRuntimeScope(string scope) =>
        string.IsNullOrWhiteSpace(scope) ? GraphWorkbenchDocumentScopes.Library : scope;

    public static string ResolveRuntimeScope(GraphWorkbenchRuntimeSnapshot runtime)
    {
        if (runtime.Source.EndsWith(":graph_level_blueprint", StringComparison.Ordinal) ||
            string.Equals(runtime.CurrentGraphId, "level_blueprint_opening", StringComparison.Ordinal))
        {
            return GraphWorkbenchDocumentScopes.LevelBlueprint;
        }

        if (runtime.Source.EndsWith(":graph_stance_fsm", StringComparison.Ordinal) ||
            string.Equals(runtime.CurrentStateMachineId, "rts.stance", StringComparison.Ordinal))
        {
            return GraphWorkbenchDocumentScopes.StanceFsm;
        }

        if (runtime.Source.EndsWith(":graph_complex_bt", StringComparison.Ordinal) ||
            string.Equals(runtime.CurrentBehaviorTreeId, "unit.assault_bt", StringComparison.Ordinal))
        {
            return GraphWorkbenchDocumentScopes.ComplexBt;
        }

        if (runtime.Source.EndsWith(":graph_stress_field", StringComparison.Ordinal) ||
            string.Equals(runtime.CurrentGraphId, "stress_field_fsm", StringComparison.Ordinal) ||
            string.Equals(runtime.CurrentGraphId, "stress_field_bt", StringComparison.Ordinal))
        {
            return GraphWorkbenchDocumentScopes.StressField;
        }

        return GraphWorkbenchDocumentScopes.Library;
    }

    private static GraphWorkbenchDocument Scope(
        string[] graphIds,
        string[] stateMachineIds,
        string[] behaviorTreeIds,
        string activeGraphId,
        string activeStateMachineId,
        string activeBehaviorTreeId)
    {
        GraphWorkbenchDocument document = CreateLibrary();
        var scopedGraphIds = new HashSet<string>(graphIds, StringComparer.Ordinal);
        var scopedStateMachineIds = new HashSet<string>(stateMachineIds, StringComparer.Ordinal);
        var scopedBehaviorTreeIds = new HashSet<string>(behaviorTreeIds, StringComparer.Ordinal);

        IncludeImplementationGraphs(document.Graphs.Where(graph => scopedGraphIds.Contains(graph.Id)).SelectMany(static graph => graph.Nodes), scopedGraphIds);
        IncludeImplementationGraphs(document.StateMachines.Where(fsm => scopedStateMachineIds.Contains(fsm.Id)).SelectMany(static fsm => fsm.Nodes), scopedGraphIds);
        IncludeImplementationGraphs(document.BehaviorTrees.Where(tree => scopedBehaviorTreeIds.Contains(tree.Id)).SelectMany(static tree => tree.Nodes), scopedGraphIds);

        RequireKnownIds("graph", scopedGraphIds, document.Graphs.Select(static graph => graph.Id));
        RequireKnownIds("FSM", scopedStateMachineIds, document.StateMachines.Select(static fsm => fsm.Id));
        RequireKnownIds("BT", scopedBehaviorTreeIds, document.BehaviorTrees.Select(static tree => tree.Id));

        document.Graphs = document.Graphs.Where(graph => scopedGraphIds.Contains(graph.Id)).ToList();
        document.StateMachines = document.StateMachines.Where(fsm => scopedStateMachineIds.Contains(fsm.Id)).ToList();
        document.BehaviorTrees = document.BehaviorTrees.Where(tree => scopedBehaviorTreeIds.Contains(tree.Id)).ToList();
        document.ActiveGraphId = activeGraphId;
        document.ActiveStateMachineId = activeStateMachineId;
        document.ActiveBehaviorTreeId = activeBehaviorTreeId;
        return document;
    }

    private static void IncludeImplementationGraphs(
        IEnumerable<GraphWorkbenchNodeDocument> nodes,
        HashSet<string> graphIds)
    {
        foreach (GraphWorkbenchNodeDocument node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.ImplementationGraphId))
            {
                graphIds.Add(node.ImplementationGraphId);
            }
        }
    }

    private static void RequireKnownIds(string label, HashSet<string> requiredIds, IEnumerable<string> knownIds)
    {
        var known = new HashSet<string>(knownIds, StringComparer.Ordinal);
        foreach (string requiredId in requiredIds)
        {
            if (!known.Contains(requiredId))
            {
                throw new InvalidOperationException($"Graph Workbench scoped document references missing {label} '{requiredId}'.");
            }
        }
    }

    private static GraphWorkbenchDocument CreateLibrary()
    {
        return new GraphWorkbenchDocument
        {
            SchemaVersion = 1,
            Revision = 1,
            ActiveGraphId = "level_blueprint_opening",
            ActiveStateMachineId = "rts.stance",
            ActiveBehaviorTreeId = "unit.assault_bt",
            Graphs =
            [
                Graph(
                    "level_blueprint_opening",
                    "Level Blueprint: trigger flow",
                    "Level Blueprint",
                    [
                        Node("level.door_trigger", "Door Trigger", "Trigger", "ConstInt", 80, 80, intValue: 0, implementationGraphId: "level.impl.door_trigger"),
                        Node("level.patrol_trigger", "Patrol Trigger", "Trigger", "ConstInt", 330, 80, intValue: 1, implementationGraphId: "level.impl.patrol_trigger"),
                        Node("level.beacon_trigger", "Beacon Trigger", "Trigger", "ConstInt", 580, 80, intValue: 2, implementationGraphId: "level.impl.beacon_trigger"),
                        Node("level.exit_trigger", "Exit Trigger", "Trigger", "ConstInt", 830, 80, intValue: 3, implementationGraphId: "level.impl.exit_trigger")
                    ],
                    [
                        Edge("level.live.e1", "level.door_trigger", "level.patrol_trigger", "enter"),
                        Edge("level.live.e2", "level.patrol_trigger", "level.beacon_trigger", "enter"),
                        Edge("level.live.e3", "level.beacon_trigger", "level.exit_trigger", "enter")
                    ],
                    "level.door_trigger"),
                CreateLevelTriggerImplementation("level.impl.door_trigger", "Atomic ops: door trigger", "level.door", phase: 0, nextState: 1, intent: 0, y: 180),
                CreateLevelTriggerImplementation("level.impl.patrol_trigger", "Atomic ops: patrol trigger", "level.patrol", phase: 1, nextState: 2, intent: 1, y: 280),
                CreateLevelTriggerImplementation("level.impl.beacon_trigger", "Atomic ops: beacon trigger", "level.beacon", phase: 2, nextState: 3, intent: 2, y: 380),
                CreateLevelTriggerImplementation("level.impl.exit_trigger", "Atomic ops: exit trigger", "level.exit", phase: 3, nextState: 3, intent: 3, y: 480),
                Graph(
                    "gas.fireball_cost_impl",
                    "Atomic ops: skill GAS cast gate",
                    "Skill GAS",
                    [
                        Node("gas.caster", "LoadCaster", "OpCode", "LoadCaster", 80, 620),
                        Node("gas.self_health", "LoadSelfAttribute Health", "OpCode", "LoadSelfAttribute", 330, 620, attribute: "Health"),
                        Node("gas.min_health", "ConstFloat min=50", "OpCode", "ConstFloat", 580, 620, floatValue: 50f),
                        Node("gas.can_cast", "CompareGtFloat hp>50", "OpCode", "CompareGtFloat", 830, 620, inputs: ["gas.self_health", "gas.min_health"]),
                        Node("gas.guard", "JumpIfFalse skip", "OpCode", "JumpIfFalse", 1080, 620, intValue: 1, inputs: ["gas.can_cast"]),
                        Node("gas.apply_burn", "ApplyEffectTemplate Burn", "OpCode", "ApplyEffectTemplate", 1330, 620, inputs: ["gas.caster", "gas.self_health"], effectTemplate: "Effects.Fireball.Burn")
                    ],
                    [
                        Edge("gas.e1", "gas.caster", "gas.self_health", "next"),
                        Edge("gas.e2", "gas.self_health", "gas.min_health", "next"),
                        Edge("gas.e3", "gas.min_health", "gas.can_cast", "next"),
                        Edge("gas.e4", "gas.can_cast", "gas.guard", "next"),
                        Edge("gas.e5", "gas.guard", "gas.apply_burn", "true")
                    ],
                    "gas.caster",
                    [
                        Output("gas.out.can_cast", "Summary", "Bool", "gas.can_cast", "canCast", "Can cast", "True when caster health passes the gate."),
                        Output("gas.out.health", "Summary", "Float", "gas.self_health", "health", "Caster health", "Health value read by the GAS graph.")
                    ]),
                Graph(
                    "rts_stance_fsm",
                    "Atomic ops: RTS stance FSM",
                    "Graph VM Ops",
                    [
                        Node("rts.health", "ConstInt health", "OpCode", "ConstInt", 80, 120, intValue: 24),
                        Node("rts.low_health_threshold", "ConstInt 30", "OpCode", "ConstInt", 300, 120, intValue: 30),
                        Node("rts.is_low_health", "CompareLtInt health<30", "OpCode", "CompareLtInt", 520, 120, inputs: ["rts.health", "rts.low_health_threshold"]),
                        Node("rts.branch.low_health", "JumpIfFalse", "OpCode", "JumpIfFalse", 740, 120, intValue: 3, inputs: ["rts.is_low_health"]),
                        Node("rts.write.return_state", "ConstInt state=Return", "OpCode", "ConstInt", 960, 120, intValue: 1),
                        Node("rts.write.recover_intent", "ConstInt intent=Recover", "OpCode", "ConstInt", 1180, 120, intValue: 3),
                        Node("rts.enemy_distance", "ConstInt enemyDistance", "OpCode", "ConstInt", 80, 260, intValue: 220),
                        Node("rts.enemy_threshold", "ConstInt 360", "OpCode", "ConstInt", 300, 260, intValue: 360),
                        Node("rts.is_enemy_close", "CompareLtInt range<360", "OpCode", "CompareLtInt", 520, 260, inputs: ["rts.enemy_distance", "rts.enemy_threshold"]),
                        Node("rts.branch.enemy", "JumpIfFalse", "OpCode", "JumpIfFalse", 740, 260, intValue: 3, inputs: ["rts.is_enemy_close"]),
                        Node("rts.write.attack_state", "ConstInt state=Attack", "OpCode", "ConstInt", 960, 260, intValue: 3),
                        Node("rts.write.attack_intent", "ConstInt intent=Engage", "OpCode", "ConstInt", 1180, 260, intValue: 2),
                        Node("rts.morale_threshold", "ConstInt 70", "OpCode", "ConstInt", 80, 400, intValue: 70),
                        Node("rts.morale", "ConstInt morale", "OpCode", "ConstInt", 300, 400, intValue: 82),
                        Node("rts.is_morale_high", "CompareLtInt 70<morale", "OpCode", "CompareLtInt", 520, 400, inputs: ["rts.morale_threshold", "rts.morale"]),
                        Node("rts.branch.morale", "JumpIfFalse", "OpCode", "JumpIfFalse", 740, 400, intValue: 3, inputs: ["rts.is_morale_high"]),
                        Node("rts.write.defend_state", "ConstInt state=Defend", "OpCode", "ConstInt", 960, 400, intValue: 2),
                        Node("rts.write.hold_state", "ConstInt state=Hold", "OpCode", "ConstInt", 1180, 400, intValue: 0)
                    ],
                    [
                        Edge("rts.e1", "rts.health", "rts.low_health_threshold", "next"),
                        Edge("rts.e2", "rts.low_health_threshold", "rts.is_low_health", "next"),
                        Edge("rts.e3", "rts.is_low_health", "rts.branch.low_health", "next"),
                        Edge("rts.e4", "rts.branch.low_health", "rts.write.return_state", "true"),
                        Edge("rts.e5", "rts.write.return_state", "rts.write.recover_intent", "next"),
                        Edge("rts.e6", "rts.write.recover_intent", "rts.enemy_distance", "next"),
                        Edge("rts.e7", "rts.enemy_distance", "rts.enemy_threshold", "next"),
                        Edge("rts.e8", "rts.enemy_threshold", "rts.is_enemy_close", "next"),
                        Edge("rts.e9", "rts.is_enemy_close", "rts.branch.enemy", "next"),
                        Edge("rts.e10", "rts.branch.enemy", "rts.write.attack_state", "true"),
                        Edge("rts.e11", "rts.write.attack_state", "rts.write.attack_intent", "next"),
                        Edge("rts.e12", "rts.write.attack_intent", "rts.morale_threshold", "next"),
                        Edge("rts.e13", "rts.morale_threshold", "rts.morale", "next"),
                        Edge("rts.e14", "rts.morale", "rts.is_morale_high", "next"),
                        Edge("rts.e15", "rts.is_morale_high", "rts.branch.morale", "next"),
                        Edge("rts.e16", "rts.branch.morale", "rts.write.defend_state", "true"),
                        Edge("rts.e17", "rts.write.defend_state", "rts.write.hold_state", "fallback")
                    ],
                    "rts.health",
                    [
                        Output("rts.out.state", "Summary", "Int", "rts.write.attack_state", "nextState", "FSM next state", "State id selected by the stance graph."),
                        Output("rts.out.intent", "Summary", "Int", "rts.write.attack_intent", "intent", "FSM intent", "Order intent selected by the stance graph.")
                    ]),
                Graph(
                    "complex_bt_selector",
                    "Atomic ops: complex BT selector",
                    "Graph VM Ops",
                    [
                        Node("bt.health", "ConstInt health", "OpCode", "ConstInt", 80, 120, intValue: 24),
                        Node("bt.low_health_threshold", "ConstInt 35", "OpCode", "ConstInt", 300, 120, intValue: 35),
                        Node("bt.is_low_health", "CompareLtInt health<35", "OpCode", "CompareLtInt", 520, 120, inputs: ["bt.health", "bt.low_health_threshold"]),
                        Node("bt.branch.low_health", "JumpIfFalse", "OpCode", "JumpIfFalse", 740, 120, intValue: 4, inputs: ["bt.is_low_health"]),
                        Node("bt.task.select_cover", "ConstInt task=SelectCover", "OpCode", "ConstInt", 960, 120, intValue: 1),
                        Node("bt.duration.cover", "ConstInt duration=3", "OpCode", "ConstInt", 1180, 120, intValue: 3),
                        Node("bt.enemy_distance", "ConstInt enemyDistance", "OpCode", "ConstInt", 80, 260, intValue: 180),
                        Node("bt.enemy_threshold", "ConstInt 300", "OpCode", "ConstInt", 300, 260, intValue: 300),
                        Node("bt.is_enemy_close", "CompareLtInt range<300", "OpCode", "CompareLtInt", 520, 260, inputs: ["bt.enemy_distance", "bt.enemy_threshold"]),
                        Node("bt.branch.enemy", "JumpIfFalse", "OpCode", "JumpIfFalse", 740, 260, intValue: 4, inputs: ["bt.is_enemy_close"]),
                        Node("bt.task.suppress_target", "ConstInt task=Suppress", "OpCode", "ConstInt", 960, 260, intValue: 2),
                        Node("bt.duration.suppress", "ConstInt duration=2", "OpCode", "ConstInt", 1180, 260, intValue: 2),
                        Node("bt.morale_threshold", "ConstInt 80", "OpCode", "ConstInt", 80, 400, intValue: 80),
                        Node("bt.morale", "ConstInt morale", "OpCode", "ConstInt", 300, 400, intValue: 94),
                        Node("bt.is_morale_high", "CompareLtInt 80<morale", "OpCode", "CompareLtInt", 520, 400, inputs: ["bt.morale_threshold", "bt.morale"]),
                        Node("bt.branch.morale", "JumpIfFalse", "OpCode", "JumpIfFalse", 740, 400, intValue: 4, inputs: ["bt.is_morale_high"]),
                        Node("bt.task.call_reinforcement", "ConstInt task=Call", "OpCode", "ConstInt", 960, 400, intValue: 3),
                        Node("bt.duration.call", "ConstInt duration=4", "OpCode", "ConstInt", 1180, 400, intValue: 4),
                        Node("bt.previous_task", "ConstInt previousBtNode", "OpCode", "ConstInt", 80, 540, intValue: 0),
                        Node("bt.idle_task", "ConstInt idle=0", "OpCode", "ConstInt", 300, 540, intValue: 0),
                        Node("bt.is_idle", "CompareEqInt idle", "OpCode", "CompareEqInt", 520, 540, inputs: ["bt.previous_task", "bt.idle_task"]),
                        Node("bt.branch.idle", "JumpIfFalse", "OpCode", "JumpIfFalse", 740, 540, intValue: 4, inputs: ["bt.is_idle"]),
                        Node("bt.task.scout_sweep", "ConstInt task=Scout", "OpCode", "ConstInt", 960, 540, intValue: 4),
                        Node("bt.duration.scout", "ConstInt duration=2", "OpCode", "ConstInt", 1180, 540, intValue: 2),
                        Node("bt.task.reposition", "ConstInt task=Reposition", "OpCode", "ConstInt", 960, 680, intValue: 5),
                        Node("bt.duration.reposition", "ConstInt duration=1", "OpCode", "ConstInt", 1180, 680, intValue: 1)
                    ],
                    [
                        Edge("bt.e1", "bt.health", "bt.low_health_threshold", "next"),
                        Edge("bt.e2", "bt.low_health_threshold", "bt.is_low_health", "next"),
                        Edge("bt.e3", "bt.is_low_health", "bt.branch.low_health", "next"),
                        Edge("bt.e4", "bt.branch.low_health", "bt.task.select_cover", "true"),
                        Edge("bt.e5", "bt.task.select_cover", "bt.duration.cover", "next"),
                        Edge("bt.e6", "bt.duration.cover", "bt.enemy_distance", "next"),
                        Edge("bt.e7", "bt.enemy_distance", "bt.enemy_threshold", "next"),
                        Edge("bt.e8", "bt.enemy_threshold", "bt.is_enemy_close", "next"),
                        Edge("bt.e9", "bt.is_enemy_close", "bt.branch.enemy", "next"),
                        Edge("bt.e10", "bt.branch.enemy", "bt.task.suppress_target", "true"),
                        Edge("bt.e11", "bt.task.suppress_target", "bt.duration.suppress", "next"),
                        Edge("bt.e12", "bt.duration.suppress", "bt.morale_threshold", "next"),
                        Edge("bt.e13", "bt.morale_threshold", "bt.morale", "next"),
                        Edge("bt.e14", "bt.morale", "bt.is_morale_high", "next"),
                        Edge("bt.e15", "bt.is_morale_high", "bt.branch.morale", "next"),
                        Edge("bt.e16", "bt.branch.morale", "bt.task.call_reinforcement", "true"),
                        Edge("bt.e17", "bt.task.call_reinforcement", "bt.duration.call", "next"),
                        Edge("bt.e18", "bt.duration.call", "bt.previous_task", "next"),
                        Edge("bt.e19", "bt.previous_task", "bt.idle_task", "next"),
                        Edge("bt.e20", "bt.idle_task", "bt.is_idle", "next"),
                        Edge("bt.e21", "bt.is_idle", "bt.branch.idle", "next"),
                        Edge("bt.e22", "bt.branch.idle", "bt.task.scout_sweep", "true"),
                        Edge("bt.e23", "bt.task.scout_sweep", "bt.duration.scout", "next"),
                        Edge("bt.e24", "bt.duration.scout", "bt.task.reposition", "fallback"),
                        Edge("bt.e25", "bt.task.reposition", "bt.duration.reposition", "next")
                    ],
                    "bt.health",
                    [
                        Output("bt.out.task", "Summary", "Int", "bt.task.suppress_target", "task", "BT task", "Task id selected by the behavior tree graph."),
                        Output("bt.out.duration", "Summary", "Int", "bt.duration.suppress", "duration", "BT duration", "Task duration selected by the behavior tree graph.")
                    ]),
                Graph(
                    "stress_field_fsm",
                    "50k Stress FSM atomic ops",
                    "Benchmark",
                    [
                        Node("stress.fsm.batch", "ConstInt entities=50000", "OpCode", "ConstInt", 80, 820, intValue: 50000),
                        Node("stress.fsm.threshold", "ConstInt threshold", "OpCode", "ConstInt", 330, 820, intValue: 50001),
                        Node("stress.fsm.branch", "CompareLtInt branch mask", "OpCode", "CompareLtInt", 580, 820, inputs: ["stress.fsm.batch", "stress.fsm.threshold"]),
                        Node("stress.fsm.intent", "ConstInt write intent", "OpCode", "ConstInt", 830, 820, intValue: 1)
                    ],
                    [
                        Edge("stress.fsm.e1", "stress.fsm.batch", "stress.fsm.threshold", "next"),
                        Edge("stress.fsm.e2", "stress.fsm.threshold", "stress.fsm.branch", "next"),
                        Edge("stress.fsm.e3", "stress.fsm.branch", "stress.fsm.intent", "chunk")
                    ],
                    "stress.fsm.batch"),
                Graph(
                    "stress_field_bt",
                    "50k Stress BT atomic ops",
                    "Benchmark",
                    [
                        Node("stress.bt.batch", "ConstInt entities=50000", "OpCode", "ConstInt", 80, 920, intValue: 50000),
                        Node("stress.bt.task", "ConstInt task dispatch", "OpCode", "ConstInt", 330, 920, intValue: 5),
                        Node("stress.bt.intent", "ConstInt async task", "OpCode", "ConstInt", 580, 920, intValue: 1)
                    ],
                    [
                        Edge("stress.bt.e1", "stress.bt.batch", "stress.bt.task", "chunk"),
                        Edge("stress.bt.e2", "stress.bt.task", "stress.bt.intent", "next")
                    ],
                    "stress.bt.batch")
            ],
            StateMachines =
            [
                new GraphWorkbenchStateMachineDocument
                {
                    Id = "rts.stance",
                    Title = "RTS Stance FSM",
                    Nodes =
                    [
                        Node("stance.hold", "Hold Fire", "State", string.Empty, 100, 120, implementationGraphId: "rts_stance_fsm"),
                        Node("stance.return", "Return Fire", "State", string.Empty, 380, 120, implementationGraphId: "rts_stance_fsm"),
                        Node("stance.defend", "Defend", "State", string.Empty, 660, 120, implementationGraphId: "rts_stance_fsm"),
                        Node("stance.attack", "Attack Anything", "State", string.Empty, 940, 120, implementationGraphId: "rts_stance_fsm")
                    ],
                    Edges =
                    [
                        Edge("stance.e1", "stance.hold", "stance.return", "damaged"),
                        Edge("stance.e2", "stance.return", "stance.defend", "safe"),
                        Edge("stance.e3", "stance.defend", "stance.attack", "enemy"),
                        Edge("stance.e4", "stance.attack", "stance.hold", "clear")
                    ]
                }
            ],
            BehaviorTrees =
            [
                new GraphWorkbenchBehaviorTreeDocument
                {
                    Id = "unit.assault_bt",
                    Title = "Complex BT: assault squad",
                    Nodes =
                    [
                        Node("bt.root", "Root", "Root", string.Empty, 80, 280),
                        Node("bt.selector", "Select tactic", "Selector", string.Empty, 300, 280),
                        Node("bt.select_cover", "Select Cover", "Task", string.Empty, 560, 40, implementationGraphId: "complex_bt_selector"),
                        Node("bt.suppress_target", "Suppress Target", "Task", string.Empty, 560, 160, implementationGraphId: "complex_bt_selector"),
                        Node("bt.call_reinforcement", "Call Reinforcement", "Task", string.Empty, 560, 280, implementationGraphId: "complex_bt_selector"),
                        Node("bt.scout_sweep", "Scout Sweep", "Task", string.Empty, 560, 400, implementationGraphId: "complex_bt_selector"),
                        Node("bt.reposition", "Reposition Squad", "Task", string.Empty, 560, 520, implementationGraphId: "complex_bt_selector")
                    ],
                    Edges =
                    [
                        Edge("bt.tree.e1", "bt.root", "bt.selector", "child"),
                        Edge("bt.tree.e2", "bt.selector", "bt.select_cover", "health"),
                        Edge("bt.tree.e3", "bt.select_cover", "bt.suppress_target", "range"),
                        Edge("bt.tree.e4", "bt.suppress_target", "bt.call_reinforcement", "morale"),
                        Edge("bt.tree.e5", "bt.call_reinforcement", "bt.scout_sweep", "idle"),
                        Edge("bt.tree.e6", "bt.scout_sweep", "bt.reposition", "fallback")
                    ]
                }
            ]
        };
    }

    private static GraphWorkbenchGraphDocument CreateLevelTriggerImplementation(
        string id,
        string title,
        string prefix,
        int phase,
        int nextState,
        int intent,
        float y)
    {
        return Graph(
            id,
            title,
            "Graph VM Ops",
            [
                Node($"{prefix}.phase", $"ConstInt phase={phase}", "OpCode", "ConstInt", 80, y, intValue: phase),
                Node($"{prefix}.expected", $"ConstInt expected={phase}", "OpCode", "ConstInt", 300, y, intValue: phase),
                Node($"{prefix}.is_current", "CompareEqInt phase", "OpCode", "CompareEqInt", 520, y, inputs: [$"{prefix}.phase", $"{prefix}.expected"]),
                Node($"{prefix}.guard", "JumpIfFalse skip", "OpCode", "JumpIfFalse", 740, y, intValue: 2, inputs: [$"{prefix}.is_current"]),
                Node($"{prefix}.next_state", $"ConstInt next={nextState}", "OpCode", "ConstInt", 960, y, intValue: nextState),
                Node($"{prefix}.intent", $"ConstInt intent={intent}", "OpCode", "ConstInt", 1180, y, intValue: intent)
            ],
            [
                Edge($"{prefix}.e1", $"{prefix}.phase", $"{prefix}.expected", "next"),
                Edge($"{prefix}.e2", $"{prefix}.expected", $"{prefix}.is_current", "next"),
                Edge($"{prefix}.e3", $"{prefix}.is_current", $"{prefix}.guard", "next"),
                Edge($"{prefix}.e4", $"{prefix}.guard", $"{prefix}.next_state", "true"),
                Edge($"{prefix}.e5", $"{prefix}.next_state", $"{prefix}.intent", "next")
            ],
            $"{prefix}.phase",
            [
                Output($"{prefix}.out.next_state", "Summary", "Int", $"{prefix}.next_state", "nextState", "Trigger next state", "Level blueprint state produced by this trigger graph."),
                Output($"{prefix}.out.intent", "Summary", "Int", $"{prefix}.intent", "intent", "Trigger intent", "Intent emitted by this trigger graph.")
            ]);
    }

    private static GraphWorkbenchGraphDocument Graph(
        string id,
        string title,
        string domain,
        List<GraphWorkbenchNodeDocument> nodes,
        List<GraphWorkbenchEdgeDocument> edges,
        string entryNodeId,
        List<GraphWorkbenchGraphOutputDocument>? outputs = null)
    {
        return new GraphWorkbenchGraphDocument
        {
            Id = id,
            Title = title,
            Domain = domain,
            Nodes = nodes,
            Edges = AddVisibleInputEdges(nodes, edges),
            EntryNodeId = entryNodeId,
            Outputs = outputs ?? new List<GraphWorkbenchGraphOutputDocument>()
        };
    }

    private static GraphWorkbenchNodeDocument Node(
        string id,
        string label,
        string kind,
        string op,
        float x,
        float y,
        int intValue = 0,
        string implementationGraphId = "",
        List<string>? inputs = null,
        float floatValue = 0f,
        bool boolValue = false,
        string tag = "",
        string attribute = "",
        string template = "",
        string collectionKey = "",
        string effectTemplate = "",
        string blackboardKey = "",
        string configKey = "",
        string validOutput = "",
        string droppedOutput = "",
        string queryCapacityPolicy = "",
        float radiusCm = 0f,
        float rangeCm = 0f,
        int directionDeg = 0,
        int halfAngleDeg = 0,
        int lengthCm = 0,
        int halfWidthCm = 0,
        int halfHeightCm = 0,
        int rotationDeg = 0,
        int hexRadius = 0,
        uint layerMask = 0,
        string relationshipMode = "",
        int limit = 0,
        int teamId = 0,
        string sort = "",
        string relationshipType = "",
        string metric = "",
        string flag = "",
        string reason = "",
        string payloadPreset = "",
        string builtinHandler = "",
        bool descending = false,
        int slot = 0)
    {
        return new GraphWorkbenchNodeDocument
        {
            Id = id,
            Label = label,
            Kind = kind,
            Op = op,
            X = x,
            Y = y,
            IntValue = intValue,
            FloatValue = floatValue,
            BoolValue = boolValue,
            Tag = tag,
            Attribute = attribute,
            Template = template,
            CollectionKey = collectionKey,
            EffectTemplate = effectTemplate,
            BlackboardKey = blackboardKey,
            ConfigKey = configKey,
            ValidOutput = validOutput,
            DroppedOutput = droppedOutput,
            QueryCapacityPolicy = queryCapacityPolicy,
            RadiusCm = radiusCm,
            RangeCm = rangeCm,
            DirectionDeg = directionDeg,
            HalfAngleDeg = halfAngleDeg,
            LengthCm = lengthCm,
            HalfWidthCm = halfWidthCm,
            HalfHeightCm = halfHeightCm,
            RotationDeg = rotationDeg,
            HexRadius = hexRadius,
            LayerMask = layerMask,
            RelationshipMode = relationshipMode,
            Limit = limit,
            TeamId = teamId,
            Sort = sort,
            RelationshipType = relationshipType,
            Metric = metric,
            Flag = flag,
            Reason = reason,
            PayloadPreset = payloadPreset,
            BuiltinHandler = builtinHandler,
            Descending = descending,
            Slot = slot,
            ImplementationGraphId = implementationGraphId,
            Inputs = inputs ?? new List<string>()
        };
    }

    private static GraphWorkbenchEdgeDocument Edge(
        string id,
        string source,
        string target,
        string label,
        string sourcePort = "exec:next",
        string targetPort = "exec:in")
    {
        return new GraphWorkbenchEdgeDocument
        {
            Id = id,
            Source = source,
            Target = target,
            Label = label,
            Role = "next",
            SourcePort = sourcePort,
            TargetPort = targetPort
        };
    }

    private static GraphWorkbenchGraphOutputDocument Output(
        string id,
        string destination,
        string type,
        string source,
        string key,
        string title,
        string summary,
        string collectionKey = "",
        string role = "")
    {
        return new GraphWorkbenchGraphOutputDocument
        {
            Id = id,
            Destination = destination,
            Type = type,
            Source = source,
            Key = key,
            CollectionKey = collectionKey,
            Role = role,
            Title = title,
            Summary = summary
        };
    }

    private static List<GraphWorkbenchEdgeDocument> AddVisibleInputEdges(
        List<GraphWorkbenchNodeDocument> nodes,
        List<GraphWorkbenchEdgeDocument> edges)
    {
        var result = new List<GraphWorkbenchEdgeDocument>(edges.Count + 16);
        result.AddRange(edges);

        var valueProducers = new Dictionary<string, (string NodeId, string SourcePort)>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            GraphWorkbenchNodeDocument node = nodes[i];
            if (!string.IsNullOrWhiteSpace(node.Id))
            {
                valueProducers[node.Id] = (node.Id, "out:value");
            }

            if (!string.IsNullOrWhiteSpace(node.ValidOutput))
            {
                valueProducers[node.ValidOutput] = (node.Id, "out:valid");
            }

            if (!string.IsNullOrWhiteSpace(node.DroppedOutput))
            {
                valueProducers[node.DroppedOutput] = (node.Id, "out:dropped");
            }
        }

        for (int n = 0; n < nodes.Count; n++)
        {
            GraphWorkbenchNodeDocument target = nodes[n];
            for (int inputIndex = 0; inputIndex < target.Inputs.Count; inputIndex++)
            {
                string sourceValue = target.Inputs[inputIndex];
                if (string.IsNullOrWhiteSpace(sourceValue) ||
                    !valueProducers.TryGetValue(sourceValue, out (string NodeId, string SourcePort) source))
                {
                    continue;
                }

                string targetPort = $"in:{inputIndex}";
                bool alreadyVisible = result.Any(edge =>
                    string.Equals(edge.Target, target.Id, StringComparison.Ordinal) &&
                    string.Equals(edge.TargetPort, targetPort, StringComparison.Ordinal));
                if (alreadyVisible)
                {
                    continue;
                }

                result.Add(new GraphWorkbenchEdgeDocument
                {
                    Id = $"{target.Id}.input.{inputIndex}",
                    Source = source.NodeId,
                    Target = target.Id,
                    Label = $"in[{inputIndex}]",
                    Role = "input",
                    SourcePort = source.SourcePort,
                    TargetPort = targetPort
                });
            }
        }

        return result;
    }
}
