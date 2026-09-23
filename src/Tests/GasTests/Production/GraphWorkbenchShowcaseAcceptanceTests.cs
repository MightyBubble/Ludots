using System.Text.Json;
using GraphAiShowcaseCommon;
using GraphWorkbenchShowcaseMod;
using GraphWorkbenchShowcaseMod.DataPlane;
using GraphWorkbenchShowcaseMod.Domain;
using Ludots.Core.Engine;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[Category("acceptance")]
public sealed class GraphWorkbenchShowcaseAcceptanceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public void SeedDocument_CoversSharedGraphDomainsAndImplementationNavigation()
    {
        var dataPlane = new GraphWorkbenchDataPlane();
        GraphWorkbenchSnapshot snapshot = CreateSnapshot(dataPlane);

        Assert.That(snapshot.Document.Graphs.Select(static graph => graph.Id), Does.Contain("level_blueprint_opening"));
        Assert.That(snapshot.Document.Graphs.Select(static graph => graph.Id), Does.Contain("rts_stance_fsm"));
        Assert.That(snapshot.Document.Graphs.Select(static graph => graph.Id), Does.Contain("complex_bt_selector"));
        Assert.That(snapshot.Document.Graphs.Select(static graph => graph.Id), Does.Contain("stress_field_fsm"));
        Assert.That(snapshot.Document.Graphs.Select(static graph => graph.Domain), Does.Contain("Level Blueprint"));
        Assert.That(snapshot.Document.Graphs.Select(static graph => graph.Domain), Does.Contain("Skill GAS"));
        Assert.That(snapshot.Document.Graphs.Select(static graph => graph.Domain), Does.Contain("Graph VM Ops"));
        Assert.That(snapshot.Document.StateMachines, Has.Count.EqualTo(1));
        Assert.That(snapshot.Document.BehaviorTrees, Has.Count.EqualTo(1));
        Assert.That(snapshot.Compile.Success, Is.True);

        var graphIds = snapshot.Document.Graphs.Select(static graph => graph.Id).ToHashSet(StringComparer.Ordinal);
        GraphWorkbenchGraphDocument level = snapshot.Document.Graphs.Single(static graph => graph.Id == "level_blueprint_opening");
        Assert.That(level.Nodes.Select(static node => node.ImplementationGraphId), Is.All.Not.Empty);
        foreach (GraphWorkbenchNodeDocument node in level.Nodes)
        {
            Assert.That(graphIds, Does.Contain(node.ImplementationGraphId));
            AssertAtomicOpGraph(snapshot.Document, node.ImplementationGraphId);
        }

        Assert.That(
            snapshot.Document.StateMachines.SelectMany(static fsm => fsm.Nodes).Any(node => graphIds.Contains(node.ImplementationGraphId)),
            Is.True,
            "FSM nodes must navigate to implementation Graph documents.");
        Assert.That(
            snapshot.Document.BehaviorTrees.SelectMany(static tree => tree.Nodes).Any(node => graphIds.Contains(node.ImplementationGraphId)),
            Is.True,
            "BT nodes must navigate to implementation Graph documents.");
        AssertAtomicOpGraph(snapshot.Document, "rts_stance_fsm");
        AssertAtomicOpGraph(snapshot.Document, "complex_bt_selector");
        AssertAtomicOpGraph(snapshot.Document, "gas.fireball_cost_impl");
        AssertVisibleInputEdges(snapshot.Document);
        AssertGraphOutputs(snapshot.Document, "level.impl.door_trigger", "level.door.out.next_state");
        AssertGraphOutputs(snapshot.Document, "gas.fireball_cost_impl", "gas.out.can_cast");
        AssertGraphOutputs(snapshot.Document, "rts_stance_fsm", "rts.out.state");
        AssertGraphOutputs(snapshot.Document, "complex_bt_selector", "bt.out.task");
    }

    [Test]
    public void RuntimeScopedDocument_StanceFsmContainsOnlyFsmAndItsAtomicGraph()
    {
        using var engine = new GameEngine();
        engine.GlobalContext["GraphAiShowcase.StanceFsm.Runtime"] = new FakeGraphAiRuntime(CreateStanceSnapshot());
        var dataPlane = new GraphWorkbenchDataPlane(engine);

        GraphWorkbenchSnapshot snapshot = CreateSnapshot(dataPlane);

        Assert.That(snapshot.Runtime.Source, Is.EqualTo("live-3d:graph_stance_fsm"));
        AssertOnlyGraphs(snapshot.Document, "rts_stance_fsm");
        Assert.That(snapshot.Document.StateMachines.Select(static item => item.Id), Is.EquivalentTo(new[] { "rts.stance" }));
        Assert.That(snapshot.Document.BehaviorTrees, Is.Empty);
        Assert.That(snapshot.Document.ActiveGraphId, Is.EqualTo("rts_stance_fsm"));
        Assert.That(snapshot.Document.ActiveStateMachineId, Is.EqualTo("rts.stance"));
        Assert.That(snapshot.Document.ActiveBehaviorTreeId, Is.Empty);
        AssertAtomicOpGraph(snapshot.Document, "rts_stance_fsm");
        AssertNoCrossCaseGraphs(snapshot.Document, "level_blueprint_opening", "complex_bt_selector", "gas.fireball_cost_impl");
    }

    [Test]
    public void RuntimeScopedDocument_ComplexBtContainsOnlyBtAndItsAtomicGraph()
    {
        using var engine = new GameEngine();
        engine.GlobalContext["GraphAiShowcase.ComplexBt.Runtime"] = new FakeGraphAiRuntime(CreateComplexBtSnapshot());
        var dataPlane = new GraphWorkbenchDataPlane(engine);

        GraphWorkbenchSnapshot snapshot = CreateSnapshot(dataPlane);

        Assert.That(snapshot.Runtime.Source, Is.EqualTo("live-3d:graph_complex_bt"));
        AssertOnlyGraphs(snapshot.Document, "complex_bt_selector");
        Assert.That(snapshot.Document.StateMachines, Is.Empty);
        Assert.That(snapshot.Document.BehaviorTrees.Select(static item => item.Id), Is.EquivalentTo(new[] { "unit.assault_bt" }));
        Assert.That(snapshot.Document.ActiveGraphId, Is.EqualTo("complex_bt_selector"));
        Assert.That(snapshot.Document.ActiveStateMachineId, Is.Empty);
        Assert.That(snapshot.Document.ActiveBehaviorTreeId, Is.EqualTo("unit.assault_bt"));
        AssertAtomicOpGraph(snapshot.Document, "complex_bt_selector");
        AssertNoCrossCaseGraphs(snapshot.Document, "level_blueprint_opening", "rts_stance_fsm", "gas.fireball_cost_impl");
    }

    [Test]
    public void RuntimeScopedDocument_LevelBlueprintContainsOnlyTriggerFlowAndItsAtomicGraphs()
    {
        using var engine = new GameEngine();
        engine.GlobalContext["GraphAiShowcase.LevelBlueprint.Runtime"] = new FakeGraphAiRuntime(CreateLevelBlueprintSnapshot());
        var dataPlane = new GraphWorkbenchDataPlane(engine);

        GraphWorkbenchSnapshot snapshot = CreateSnapshot(dataPlane);

        Assert.That(snapshot.Runtime.Source, Is.EqualTo("live-3d:graph_level_blueprint"));
        AssertOnlyGraphs(
            snapshot.Document,
            "level_blueprint_opening",
            "level.impl.door_trigger",
            "level.impl.patrol_trigger",
            "level.impl.beacon_trigger",
            "level.impl.exit_trigger");
        Assert.That(snapshot.Document.StateMachines, Is.Empty);
        Assert.That(snapshot.Document.BehaviorTrees, Is.Empty);
        Assert.That(snapshot.Document.ActiveGraphId, Is.EqualTo("level_blueprint_opening"));
        Assert.That(snapshot.Document.ActiveStateMachineId, Is.Empty);
        Assert.That(snapshot.Document.ActiveBehaviorTreeId, Is.Empty);

        GraphWorkbenchGraphDocument level = snapshot.Document.Graphs.Single(static graph => graph.Id == "level_blueprint_opening");
        Assert.That(level.Nodes.Select(static node => node.Kind), Is.All.EqualTo("Trigger"));
        foreach (GraphWorkbenchNodeDocument node in level.Nodes)
        {
            AssertAtomicOpGraph(snapshot.Document, node.ImplementationGraphId);
        }

        AssertNoCrossCaseGraphs(snapshot.Document, "rts_stance_fsm", "complex_bt_selector", "gas.fireball_cost_impl");
    }

    [Test]
    public void RuntimeScopedDocument_StressFieldContainsOnlyBenchmarkGraphs()
    {
        using var engine = new GameEngine();
        engine.GlobalContext["GraphAiShowcase.StressField.Runtime"] = new FakeGraphAiRuntime(CreateStressSnapshot());
        var dataPlane = new GraphWorkbenchDataPlane(engine);

        GraphWorkbenchSnapshot snapshot = CreateSnapshot(dataPlane);

        Assert.That(snapshot.Runtime.Source, Is.EqualTo("live-3d:graph_stress_field"));
        AssertOnlyGraphs(snapshot.Document, "stress_field_fsm", "stress_field_bt");
        Assert.That(snapshot.Document.StateMachines, Is.Empty);
        Assert.That(snapshot.Document.BehaviorTrees, Is.Empty);
        Assert.That(snapshot.Document.ActiveGraphId, Is.EqualTo("stress_field_bt"));
        Assert.That(snapshot.Document.ActiveStateMachineId, Is.Empty);
        Assert.That(snapshot.Document.ActiveBehaviorTreeId, Is.Empty);
        Assert.That(snapshot.Runtime.CurrentStateMachineId, Is.Empty);
        Assert.That(snapshot.Runtime.CurrentBehaviorTreeId, Is.Empty);
        AssertNoCrossCaseGraphs(snapshot.Document, "level_blueprint_opening", "rts_stance_fsm", "complex_bt_selector", "gas.fireball_cost_impl");
    }

    [Test]
    public void RuntimeScopedDocument_SwitchesFromLibraryWhenRuntimeBecomesActive()
    {
        using var engine = new GameEngine();
        var dataPlane = new GraphWorkbenchDataPlane(engine);
        GraphWorkbenchSnapshot library = CreateSnapshot(dataPlane);
        Assert.That(library.Document.Graphs.Select(static graph => graph.Id), Does.Contain("complex_bt_selector"));

        engine.GlobalContext["GraphAiShowcase.StanceFsm.Runtime"] = new FakeGraphAiRuntime(CreateStanceSnapshot());
        GraphWorkbenchSnapshot scoped = CreateSnapshot(dataPlane);

        AssertOnlyGraphs(scoped.Document, "rts_stance_fsm");
        Assert.That(scoped.Document.StateMachines.Select(static item => item.Id), Is.EquivalentTo(new[] { "rts.stance" }));
        Assert.That(scoped.Document.BehaviorTrees, Is.Empty);
    }

    [Test]
    public void WorkbenchGraphConfigMapping_PreservesPortsParametersAuxOutputsAndGraphOutputs()
    {
        var graph = new GraphWorkbenchGraphDocument
        {
            Id = "test.graph.mapping",
            Title = "Mapping",
            Domain = "Skill GAS",
            EntryNodeId = "caster",
            Nodes =
            [
                new GraphWorkbenchNodeDocument
                {
                    Id = "caster",
                    Label = "Caster",
                    Kind = "OpCode",
                    Op = "LoadCaster"
                },
                new GraphWorkbenchNodeDocument
                {
                    Id = "radius_query",
                    Label = "Radius",
                    Kind = "OpCode",
                    Op = "QueryRadius",
                    RadiusCm = 450f,
                    QueryCapacityPolicy = "AllowTruncated",
                    DroppedOutput = "radius_query.dropped"
                },
                new GraphWorkbenchNodeDocument
                {
                    Id = "index",
                    Label = "Index",
                    Kind = "OpCode",
                    Op = "ConstInt",
                    IntValue = 0
                },
                new GraphWorkbenchNodeDocument
                {
                    Id = "first_target",
                    Label = "First target",
                    Kind = "OpCode",
                    Op = "TargetListGet",
                    Inputs = ["index"],
                    ValidOutput = "first_target.valid"
                }
            ],
            Edges =
            [
                ExecEdge("e1", "caster", "radius_query"),
                ExecEdge("e2", "radius_query", "index"),
                ExecEdge("e3", "index", "first_target"),
                InputEdge("e4", "index", "first_target", 0)
            ],
            Outputs =
            [
                new GraphWorkbenchGraphOutputDocument
                {
                    Id = "out.dropped",
                    Destination = "Summary",
                    Type = "Int",
                    Source = "radius_query.dropped",
                    Key = "dropped",
                    Title = "Dropped",
                    Summary = "Dropped query rows"
                },
                new GraphWorkbenchGraphOutputDocument
                {
                    Id = "out.valid",
                    Destination = "Summary",
                    Type = "Bool",
                    Source = "first_target.valid",
                    Key = "valid",
                    Title = "Valid",
                    Summary = "Whether target exists"
                }
            ]
        };

        GraphConfig config = GraphWorkbenchDocumentCompiler.ToGraphConfig(graph);
        GraphNodeConfig query = config.Nodes.Single(static node => node.Id == "radius_query");
        GraphNodeConfig target = config.Nodes.Single(static node => node.Id == "first_target");

        Assert.That(query.RadiusCm, Is.EqualTo(450f));
        Assert.That(query.QueryCapacityPolicy, Is.EqualTo("AllowTruncated"));
        Assert.That(query.DroppedOutput, Is.EqualTo("radius_query.dropped"));
        Assert.That(target.Inputs, Is.EqualTo(new[] { "index" }));
        Assert.That(target.ValidOutput, Is.EqualTo("first_target.valid"));
        Assert.That(config.Outputs.Select(static output => output.Source), Does.Contain("radius_query.dropped"));
        Assert.That(config.Outputs.Select(static output => output.Source), Does.Contain("first_target.valid"));

        var (_, outputSchema, diagnostics) = GraphCompiler.CompileWithOutputs(config);
        Assert.That(diagnostics.Where(static item => item.Severity == GraphDiagnosticSeverity.Error), Is.Empty);
        Assert.That(outputSchema.Bindings.Select(static output => output.Id), Does.Contain("out.dropped"));
        Assert.That(outputSchema.Bindings.Select(static output => output.Id), Does.Contain("out.valid"));
    }

    [Test]
    public void CompileFailure_RejectsHiddenGraphInputsWithoutVisiblePortEdges()
    {
        var dataPlane = new GraphWorkbenchDataPlane();
        GraphWorkbenchSnapshot before = CreateSnapshot(dataPlane);
        GraphWorkbenchDocument broken = Clone(before.Document);
        broken.Revision++;
        GraphWorkbenchGraphDocument graph = broken.Graphs.Single(static item => item.Id == "rts_stance_fsm");
        graph.Edges.RemoveAll(static edge =>
            edge.Target == "rts.is_low_health" &&
            edge.TargetPort == "in:0");

        WebUiCommandResult result = dataPlane.ApplyCommand(new WebUiCommandRequest(
            GraphWorkbenchShowcaseIds.CompileDocumentCommand,
            1,
            Array.Empty<WebUiEntityRef>(),
            JsonSerializer.SerializeToElement(new { document = broken }, JsonOptions)));

        GraphWorkbenchSnapshot after = CreateSnapshot(dataPlane);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo("compile_failed"));
        Assert.That(after.Runtime.AppliedRevision, Is.EqualTo(before.Runtime.AppliedRevision));
        Assert.That(after.Compile.Diagnostics.Any(static item => item.Code == "GW0316"), Is.True);
    }

    [Test]
    public void CompileFailure_DoesNotApplyDraftToRunningRevision()
    {
        var dataPlane = new GraphWorkbenchDataPlane();
        GraphWorkbenchSnapshot before = CreateSnapshot(dataPlane);
        GraphWorkbenchDocument broken = Clone(before.Document);
        broken.Revision++;
        broken.Graphs[0].EntryNodeId = "missing.entry";

        WebUiCommandResult result = dataPlane.ApplyCommand(new WebUiCommandRequest(
            GraphWorkbenchShowcaseIds.CompileDocumentCommand,
            1,
            Array.Empty<WebUiEntityRef>(),
            JsonSerializer.SerializeToElement(new { document = broken }, JsonOptions)));

        GraphWorkbenchSnapshot after = CreateSnapshot(dataPlane);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo("compile_failed"));
        Assert.That(after.Compile.Success, Is.False);
        Assert.That(after.Runtime.AppliedRevision, Is.EqualTo(before.Runtime.AppliedRevision));
        Assert.That(after.Compile.Diagnostics.Any(static item => item.Code is "GW0101" or "GASG0005"), Is.True);
    }

    [Test]
    public void RuntimeDebug_ComesFromLive3dShowcaseAndTracksSelectedEntityNodes()
    {
        using var engine = new GameEngine();
        engine.GlobalContext["GraphAiShowcase.StanceFsm.Runtime"] = new FakeGraphAiRuntime(CreateStanceSnapshot());
        var dataPlane = new GraphWorkbenchDataPlane(engine);

        WebUiCommandResult selectResult = dataPlane.ApplyCommand(new WebUiCommandRequest(
            GraphWorkbenchShowcaseIds.SelectEntityCommand,
            1,
            Array.Empty<WebUiEntityRef>(),
            JsonSerializer.SerializeToElement(new { entityId = "graph-stance-damaged-raider" }, JsonOptions)));

        Assert.That(selectResult.Success, Is.True);
        dataPlane.AdvanceRuntime(1f / 30f);
        dataPlane.AdvanceRuntime(1f / 30f);
        dataPlane.AdvanceRuntime(1f / 30f);

        GraphWorkbenchSnapshot snapshot = CreateSnapshot(dataPlane);

        Assert.That(snapshot.Runtime.Source, Is.EqualTo("live-3d:graph_stance_fsm"));
        Assert.That(snapshot.Runtime.SelectedEntityId, Is.EqualTo("graph-stance-damaged-raider"));
        Assert.That(snapshot.Runtime.CurrentStateMachineId, Is.EqualTo("rts.stance"));
        Assert.That(snapshot.Runtime.CurrentStateNodeId, Is.EqualTo("stance.return"));
        Assert.That(snapshot.Runtime.CurrentGraphId, Is.EqualTo("rts_stance_fsm"));
        Assert.That(snapshot.Runtime.CurrentGraphNodeId, Is.EqualTo("rts.write.return_state"));
        Assert.That(snapshot.Runtime.Entities.Select(static entity => entity.Id), Does.Contain("graph-stance-damaged-raider"));
    }

    [Test]
    public void RuntimeDebug_StressFieldExposesBenchmarkAggregatesWithoutFakeEntities()
    {
        using var engine = new GameEngine();
        engine.GlobalContext["GraphAiShowcase.StressField.Runtime"] = new FakeGraphAiRuntime(CreateStressSnapshot());
        var dataPlane = new GraphWorkbenchDataPlane(engine);

        GraphWorkbenchSnapshot snapshot = CreateSnapshot(dataPlane);

        Assert.That(snapshot.Runtime.Source, Is.EqualTo("live-3d:graph_stress_field"));
        Assert.That(snapshot.Runtime.Entities, Is.Empty);
        Assert.That(snapshot.Runtime.CurrentGraphId, Is.EqualTo("stress_field_bt"));
        Assert.That(snapshot.Runtime.CurrentGraphNodeId, Is.EqualTo("stress.bt.task"));
        Assert.That(snapshot.Runtime.Aggregates.Single(static row => row.Domain == "ECS entities").Count, Is.EqualTo(50_000));
        Assert.That(snapshot.Runtime.Aggregates.Single(static row => row.Domain == "visible dots").Count, Is.EqualTo(50_000));
    }

    [Test]
    public void LauncherRegistry_ExposeGraphWorkbenchAsToolingNotAi()
    {
        string repoRoot = FindRepoRoot();
        string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
        string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));
        string registry = File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json"));
        string installerSource = Path.Combine(repoRoot, "mods", "showcases", "graph_workbench", "GraphWorkbenchShowcaseMod", "DataPlane", "GraphWorkbenchDataPlaneInstaller.cs");
        string webAppSource = Path.Combine(repoRoot, "mods", "showcases", "graph_workbench", "GraphWorkbenchShowcaseMod", "WebApp", "src", "main.jsx");
        string docs = Path.Combine(repoRoot, "gitbook", "architecture", "graph-workbench-showcase.md");

        Assert.That(launcherConfig, Does.Contain("graph_workbench_showcase"));
        Assert.That(launcherPresets, Does.Contain("graph_workbench_cef_raylib"));
        Assert.That(launcherPresets, Does.Contain("graph_level_blueprint_workbench_cef_raylib"));
        Assert.That(launcherPresets, Does.Contain("graph_stance_fsm_workbench_cef_raylib"));
        Assert.That(launcherPresets, Does.Contain("graph_complex_bt_workbench_cef_raylib"));
        Assert.That(launcherPresets, Does.Contain("graph_stress_field_workbench_cef_raylib"));
        Assert.That(launcherPresets, Does.Contain("%TEMP%/Ludots/CefGraphWorkbench/stance-fsm"));
        Assert.That(launcherPresets, Does.Contain("%TEMP%/Ludots/CefGraphWorkbench/complex-bt"));
        Assert.That(launcherPresets, Does.Contain("%TEMP%/Ludots/CefGraphWorkbench/stress-field"));
        Assert.That(registry, Does.Contain("\"id\": \"graph_workbench\""));
        Assert.That(registry, Does.Contain("\"category\": \"tooling\""));
        Assert.That(registry, Does.Contain("\"gas\""));
        Assert.That(File.Exists(installerSource), Is.True);
        Assert.That(
            File.ReadAllText(installerSource),
            Does.Contain("BrowserSurfaceCompositeOrder.AfterSkiaOverlay"),
            "Graph Workbench is an opaque docked tool surface; native Skia HUD must not draw over it.");
        Assert.That(
            File.ReadAllText(installerSource),
            Does.Contain("BrowserSurfaceAlphaMode.PromoteNonTransparentToOpaque"),
            "Graph Workbench must not let native world HUD bleed through semi-transparent dock pixels.");
        Assert.That(File.Exists(webAppSource), Is.True);
        string webApp = File.ReadAllText(webAppSource);
        Assert.That(webApp, Does.Contain("sourceHandle"));
        Assert.That(webApp, Does.Contain("targetHandle"));
        Assert.That(webApp, Does.Contain("exec:next"));
        Assert.That(webApp, Does.Contain("out:value"));
        Assert.That(webApp, Does.Contain("in:${index}"));
        Assert.That(webApp, Does.Contain("GraphOutput"));
        Assert.That(webApp, Does.Contain("structuralFlow"));
        Assert.That(webApp, Does.Contain("runtimeActivity"));
        Assert.That(webApp, Does.Contain("preserveCurrentNodePositions"));
        Assert.That(webApp, Does.Contain("shouldAdoptDocumentSnapshot"));
        Assert.That(webApp, Does.Contain("getAvailableModes"));
        Assert.That(webApp, Does.Contain("documentShapeSignature"));
        Assert.That(webApp, Does.Contain("openImplementationGraph"));
        Assert.That(webApp, Does.Contain("onOpenImplementationGraph"));
        Assert.That(webApp, Does.Contain("data-implementation-graph-id"));
        Assert.That(webApp, Does.Contain("resolveRuntimeStateMachineId"));
        Assert.That(webApp, Does.Contain("resolveRuntimeBehaviorTreeId"));
        Assert.That(webApp, Does.Contain("zoomOnDoubleClick={false}"));
        Assert.That(webApp, Does.Contain("nodeClickDistance={6}"));
        Assert.That(webApp, Does.Contain("nodeDragThreshold={4}"));
        Assert.That(webApp, Does.Contain("connectOnClick={false}"));
        Assert.That(webApp, Does.Contain("onNodeDragStart"));
        Assert.That(
            webApp,
            Does.Not.Contain("event.stopPropagation();\r\n        data.onOpenImplementationGraph"),
            "Graph Workbench node double-click navigation must be owned by ReactFlow, not swallowed inside the custom node.");
        Assert.That(
            webApp,
            Does.Not.Contain("Object.entries(MODE_LABELS).map"),
            "Graph Workbench must not show FSM or BT tabs when the active showcase document does not contain them.");
        Assert.That(
            webApp,
            Does.Not.Contain("setNodes(flow.nodes);"),
            "Graph Workbench must not overwrite ReactFlow node positions on every runtime DataPlane snapshot.");
        Assert.That(File.Exists(docs), Is.True);
        Assert.That(webApp, Does.Not.Contain("catch(() => {})"));
    }

    private static GraphWorkbenchSnapshot CreateSnapshot(GraphWorkbenchDataPlane dataPlane)
    {
        var context = new WebUiTopicContext("test-session", GraphWorkbenchShowcaseIds.WebUiTopic, 1, default);
        Assert.That(dataPlane.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
        GraphWorkbenchSnapshot? snapshot = JsonSerializer.Deserialize<GraphWorkbenchSnapshot>(packet.Payload.Span, JsonOptions);
        return snapshot ?? throw new InvalidOperationException("Graph Workbench snapshot was empty.");
    }

    private static GraphWorkbenchDocument Clone(GraphWorkbenchDocument document)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        return JsonSerializer.Deserialize<GraphWorkbenchDocument>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Could not clone Graph Workbench document.");
    }

    private static void AssertOnlyGraphs(GraphWorkbenchDocument document, params string[] expectedGraphIds)
    {
        Assert.That(document.Graphs.Select(static graph => graph.Id), Is.EquivalentTo(expectedGraphIds));
    }

    private static void AssertNoCrossCaseGraphs(GraphWorkbenchDocument document, params string[] forbiddenGraphIds)
    {
        string[] graphIds = document.Graphs.Select(static graph => graph.Id).ToArray();
        foreach (string forbiddenGraphId in forbiddenGraphIds)
        {
            Assert.That(graphIds, Does.Not.Contain(forbiddenGraphId));
        }
    }

    private static void AssertAtomicOpGraph(GraphWorkbenchDocument document, string graphId)
    {
        GraphWorkbenchGraphDocument graph = document.Graphs.Single(item => item.Id == graphId);
        Assert.That(graph.Nodes, Is.Not.Empty);
        Assert.That(graph.Nodes.Select(static node => node.Kind), Is.All.EqualTo("OpCode"));
        Assert.That(graph.Nodes.Select(static node => node.Op), Is.All.Not.Empty);
        Assert.That(
            graph.Nodes.Select(static node => node.Op),
            Has.Some.Matches<string>(op => op is "ConstInt" or "AddInt" or "CompareLtInt" or "CompareEqInt" or "JumpIfFalse" or "Jump"),
            $"Implementation graph '{graphId}' must contain Graph VM op code nodes.");
    }

    private static void AssertVisibleInputEdges(GraphWorkbenchDocument document)
    {
        foreach (GraphWorkbenchGraphDocument graph in document.Graphs)
        {
            var nodesById = graph.Nodes.ToDictionary(static node => node.Id, StringComparer.Ordinal);
            foreach (GraphWorkbenchNodeDocument node in graph.Nodes)
            {
                for (int i = 0; i < node.Inputs.Count; i++)
                {
                    string expectedSource = node.Inputs[i];
                    GraphWorkbenchEdgeDocument edge = graph.Edges.SingleOrDefault(candidate =>
                        candidate.Target == node.Id &&
                        candidate.TargetPort == $"in:{i}")
                        ?? throw new AssertionException($"Graph '{graph.Id}' node '{node.Id}' input[{i}] has no visible port edge.");
                    Assert.That(ResolveEdgeSourceValue(edge, nodesById), Is.EqualTo(expectedSource));
                    Assert.That(edge.SourcePort, Does.StartWith("out:"));
                    Assert.That(edge.Role, Is.EqualTo("input"));
                }
            }
        }
    }

    private static void AssertGraphOutputs(GraphWorkbenchDocument document, string graphId, string expectedOutputId)
    {
        GraphWorkbenchGraphDocument graph = document.Graphs.Single(item => item.Id == graphId);
        Assert.That(graph.Outputs.Select(static output => output.Id), Does.Contain(expectedOutputId));
        Assert.That(graph.Outputs.Select(static output => output.Source), Is.All.Not.Empty);
    }

    private static string ResolveEdgeSourceValue(
        GraphWorkbenchEdgeDocument edge,
        Dictionary<string, GraphWorkbenchNodeDocument> nodesById)
    {
        GraphWorkbenchNodeDocument source = nodesById[edge.Source];
        return edge.SourcePort switch
        {
            "" or "out:value" => source.Id,
            "out:valid" => source.ValidOutput,
            "out:dropped" => source.DroppedOutput,
            _ => throw new AssertionException($"Unsupported source port '{edge.SourcePort}'.")
        };
    }

    private static GraphWorkbenchEdgeDocument ExecEdge(string id, string source, string target) =>
        new()
        {
            Id = id,
            Source = source,
            Target = target,
            Label = "next",
            Role = "next",
            SourcePort = "exec:next",
            TargetPort = "exec:in"
        };

    private static GraphWorkbenchEdgeDocument InputEdge(string id, string source, string target, int inputIndex) =>
        new()
        {
            Id = id,
            Source = source,
            Target = target,
            Label = $"in[{inputIndex}]",
            Role = "input",
            SourcePort = "out:value",
            TargetPort = $"in:{inputIndex}"
        };

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "launcher.config.json")) && Directory.Exists(Path.Combine(dir, "mods")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Ludots repository root.");
    }

    private static GraphAiShowcaseSnapshot CreateLevelBlueprintSnapshot()
    {
        return new GraphAiShowcaseSnapshot(
            "graph_level_blueprint",
            "LevelBlueprint",
            "Graph Level Blueprint Capability",
            "level_blueprint_opening",
            19,
            8,
            2,
            "Beacon Trigger",
            2,
            "Light Beacon",
            2,
            "Capability scene only.",
            GraphAiHotPathSnapshot.Empty,
            GraphAiStressFieldSnapshot.Empty,
            Array.Empty<GraphAiActorSnapshot>());
    }

    private static GraphAiShowcaseSnapshot CreateStanceSnapshot()
    {
        return new GraphAiShowcaseSnapshot(
            "graph_stance_fsm",
            "StanceFsm",
            "Graph RTS Stance FSM Capability",
            "rts_stance_fsm",
            17,
            5,
            1,
            "Return Fire",
            3,
            "Recover",
            0,
            "Capability scene only.",
            GraphAiHotPathSnapshot.Empty,
            GraphAiStressFieldSnapshot.Empty,
            [
                new GraphAiActorSnapshot(
                    "Forward Sentry",
                    "graph-stance-forward-sentry",
                    3,
                    "Attack Anything",
                    2,
                    "Engage Threat",
                    "attack red threat",
                    0,
                    0,
                    "No Task",
                    0,
                    90,
                    240,
                    1200,
                    600),
                new GraphAiActorSnapshot(
                    "Damaged Raider",
                    "graph-stance-damaged-raider",
                    1,
                    "Return Fire",
                    3,
                    "Recover",
                    "retreat to green cover",
                    0,
                    0,
                    "No Task",
                    0,
                    24,
                    220,
                    900,
                    420)
            ]);
    }

    private static GraphAiShowcaseSnapshot CreateComplexBtSnapshot()
    {
        return new GraphAiShowcaseSnapshot(
            "graph_complex_bt",
            "ComplexBt",
            "Graph Complex BT Capability",
            "complex_bt_selector",
            31,
            9,
            3,
            "Attack Anything",
            2,
            "Suppress",
            0,
            "Capability scene only.",
            GraphAiHotPathSnapshot.Empty,
            GraphAiStressFieldSnapshot.Empty,
            [
                new GraphAiActorSnapshot(
                    "Assault Leader",
                    "graph-bt-assault-leader",
                    3,
                    "Attack Anything",
                    2,
                    "Engage Threat",
                    "suppress red threat",
                    2,
                    2,
                    "Suppress Target",
                    4,
                    76,
                    180,
                    1100,
                    540)
            ]);
    }

    private static GraphAiShowcaseSnapshot CreateStressSnapshot()
    {
        return new GraphAiShowcaseSnapshot(
            "graph_stress_field",
            "StressField",
            "Graph AI 50k Benchmark Field",
            "stress_field_fsm",
            55,
            12,
            3,
            "Attack Anything",
            2,
            "Suppress",
            1234,
            "Benchmark scene.",
            GraphAiHotPathSnapshot.Empty,
            new GraphAiStressFieldSnapshot(
                50_000,
                50_000,
                131_072,
                0,
                50_000,
                50_000,
                600_000,
                600_000,
                1400,
                0,
                0,
                1234,
                12_500,
                12_500,
                12_500,
                12_500,
                15,
                31,
                123456),
            Array.Empty<GraphAiActorSnapshot>());
    }

    private sealed class FakeGraphAiRuntime
    {
        public FakeGraphAiRuntime(GraphAiShowcaseSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public bool IsActive => true;
        public GraphAiShowcaseSnapshot Snapshot { get; }
    }
}
