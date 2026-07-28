using System.Text.Json;
using Arch.Core;
using GraphWorkbenchShowcaseMod.Domain;
using Ludots.WebUI.DataPlane;

namespace GraphWorkbenchShowcaseMod.DataPlane;

public sealed class GraphWorkbenchDataPlane : IWebUiTopicProducer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly GraphWorkbenchRuntimeBridge _runtime;
    private GraphWorkbenchDocument _draft;
    private GraphWorkbenchDocument _running;
    private GraphWorkbenchCompileResult _compileResult;
    private string _lastCommand = "startup";
    private string _lastCommandStatus = "idle";
    private int _appliedRevision;

    public GraphWorkbenchDataPlane()
        : this(World.Create())
    {
    }

    public GraphWorkbenchDataPlane(World world)
    {
        _draft = GraphWorkbenchSeedDocument.Create();
        _running = CloneDocument(_draft);
        _compileResult = GraphWorkbenchDocumentCompiler.Compile(_draft, appliedRevision: 0);
        if (!_compileResult.Success)
        {
            throw new InvalidOperationException("Seed Graph workbench document failed to compile.");
        }

        _appliedRevision = _draft.Revision;
        _runtime = new GraphWorkbenchRuntimeBridge(world, _running, _appliedRevision);
    }

    public string Topic => GraphWorkbenchShowcaseIds.WebUiTopic;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        lock (_sync)
        {
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
        return _runtime.TrySelectEntity(entityId)
            ? WebUiCommandResult.Ok()
            : WebUiCommandResult.Fail("unknown_entity", $"Graph workbench entity '{entityId}' does not exist.");
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
            ["ConstInt", "AddInt", "CompareLtInt", "CompareEqInt", "ConstBool", "ConstFloat"],
            ["State", "Transition", "Entry"],
            ["Root", "Selector", "Sequence", "Condition", "Task"]);
}


internal static class GraphWorkbenchSeedDocument
{
    public static GraphWorkbenchDocument Create()
    {
        return new GraphWorkbenchDocument
        {
            SchemaVersion = 1,
            Revision = 1,
            ActiveGraphId = "level.open_gate_impl",
            ActiveStateMachineId = "rts.stance",
            ActiveBehaviorTreeId = "unit.assault_bt",
            Graphs =
            [
                Graph(
                    "level.open_gate_impl",
                    "关卡蓝图：开门流程",
                    "关卡蓝图",
                    [
                        Node("level.read_switch", "读触发器", "GraphOp", "ConstInt", 80, 80, intValue: 1),
                        Node("level.open_door", "打开门", "GraphOp", "ConstInt", 330, 80, intValue: 1),
                        Node("level.rally_patrol", "召集巡逻队", "GraphOp", "ConstInt", 580, 80, intValue: 2),
                        Node("level.light_beacon", "点亮信标", "GraphOp", "ConstInt", 830, 80, intValue: 3)
                    ],
                    [
                        Edge("level.e1", "level.read_switch", "level.open_door", "next"),
                        Edge("level.e2", "level.open_door", "level.rally_patrol", "next"),
                        Edge("level.e3", "level.rally_patrol", "level.light_beacon", "next")
                    ],
                    "level.read_switch"),
                Graph(
                    "gas.fireball_cost_impl",
                    "技能 GAS：火球消耗",
                    "技能 GAS",
                    [
                        Node("gas.mana_cost", "读取蓝耗", "GraphOp", "ConstInt", 80, 250, intValue: 35),
                        Node("gas.cooldown_cost", "读取冷却", "GraphOp", "ConstInt", 330, 250, intValue: 8),
                        Node("gas.total_cost", "合并成本", "GraphOp", "AddInt", 580, 250, inputs: ["gas.mana_cost", "gas.cooldown_cost"])
                    ],
                    [
                        Edge("gas.e1", "gas.mana_cost", "gas.cooldown_cost", "next"),
                        Edge("gas.e2", "gas.cooldown_cost", "gas.total_cost", "next")
                    ],
                    "gas.mana_cost"),
                Graph(
                    "fsm.return_fire_impl",
                    "FSM 实现：还击姿态",
                    "FSM 实现",
                    [
                        Node("fsm.read_damage", "读取受击信号", "GraphOp", "ConstInt", 80, 420, intValue: 1),
                        Node("fsm.set_return_fire", "切到还击", "GraphOp", "ConstInt", 330, 420, intValue: 2),
                        Node("fsm.write_intent", "写入意图", "GraphOp", "AddInt", 580, 420, inputs: ["fsm.read_damage", "fsm.set_return_fire"])
                    ],
                    [
                        Edge("fsm.g1", "fsm.read_damage", "fsm.set_return_fire", "next"),
                        Edge("fsm.g2", "fsm.set_return_fire", "fsm.write_intent", "next")
                    ],
                    "fsm.read_damage"),
                Graph(
                    "bt.pick_target_impl",
                    "BT 任务：选择目标",
                    "BT 任务",
                    [
                        Node("bt.scan", "扫描敌人", "GraphOp", "ConstInt", 80, 590, intValue: 1),
                        Node("bt.score", "计算威胁", "GraphOp", "ConstInt", 330, 590, intValue: 4),
                        Node("bt.commit", "锁定目标", "GraphOp", "AddInt", 580, 590, inputs: ["bt.scan", "bt.score"])
                    ],
                    [
                        Edge("bt.g1", "bt.scan", "bt.score", "next"),
                        Edge("bt.g2", "bt.score", "bt.commit", "next")
                    ],
                    "bt.scan")
            ],
            StateMachines =
            [
                new GraphWorkbenchStateMachineDocument
                {
                    Id = "rts.stance",
                    Title = "RTS 姿态 FSM",
                    Nodes =
                    [
                        Node("stance.hold", "Hold Fire", "State", string.Empty, 100, 120),
                        Node("stance.return", "Return Fire", "State", string.Empty, 380, 120, implementationGraphId: "fsm.return_fire_impl"),
                        Node("stance.defend", "Defend", "State", string.Empty, 660, 120, implementationGraphId: "fsm.return_fire_impl"),
                        Node("stance.attack", "Attack Anything", "State", string.Empty, 940, 120, implementationGraphId: "fsm.return_fire_impl")
                    ],
                    Edges =
                    [
                        Edge("stance.e1", "stance.hold", "stance.return", "受击"),
                        Edge("stance.e2", "stance.return", "stance.defend", "守点"),
                        Edge("stance.e3", "stance.defend", "stance.attack", "发现目标"),
                        Edge("stance.e4", "stance.attack", "stance.hold", "脱战")
                    ]
                }
            ],
            BehaviorTrees =
            [
                new GraphWorkbenchBehaviorTreeDocument
                {
                    Id = "unit.assault_bt",
                    Title = "复杂 BT：压制推进",
                    Nodes =
                    [
                        Node("bt.root", "Root", "Root", string.Empty, 120, 100),
                        Node("bt.selector", "选择战术", "Selector", string.Empty, 360, 100),
                        Node("bt.sequence", "推进序列", "Sequence", string.Empty, 600, 100),
                        Node("bt.has_target", "有可攻击目标", "Condition", string.Empty, 840, 20, implementationGraphId: "bt.pick_target_impl"),
                        Node("bt.fire", "开火", "Task", string.Empty, 840, 180, implementationGraphId: "gas.fireball_cost_impl")
                    ],
                    Edges =
                    [
                        Edge("bt.e1", "bt.root", "bt.selector", "child"),
                        Edge("bt.e2", "bt.selector", "bt.sequence", "child"),
                        Edge("bt.e3", "bt.sequence", "bt.has_target", "condition"),
                        Edge("bt.e4", "bt.sequence", "bt.fire", "task")
                    ]
                }
            ]
        };
    }

    private static GraphWorkbenchGraphDocument Graph(
        string id,
        string title,
        string domain,
        List<GraphWorkbenchNodeDocument> nodes,
        List<GraphWorkbenchEdgeDocument> edges,
        string entryNodeId)
    {
        return new GraphWorkbenchGraphDocument
        {
            Id = id,
            Title = title,
            Domain = domain,
            Nodes = nodes,
            Edges = edges,
            EntryNodeId = entryNodeId
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
        List<string>? inputs = null)
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
            ImplementationGraphId = implementationGraphId,
            Inputs = inputs ?? new List<string>()
        };
    }

    private static GraphWorkbenchEdgeDocument Edge(string id, string source, string target, string label)
    {
        return new GraphWorkbenchEdgeDocument
        {
            Id = id,
            Source = source,
            Target = target,
            Label = label,
            Role = "next"
        };
    }
}
