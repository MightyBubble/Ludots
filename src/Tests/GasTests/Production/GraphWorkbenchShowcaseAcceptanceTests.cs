using System.Text.Json;
using Arch.Core;
using GraphWorkbenchShowcaseMod;
using GraphWorkbenchShowcaseMod.DataPlane;
using GraphWorkbenchShowcaseMod.Domain;
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

        Assert.That(snapshot.Document.Graphs.Select(static graph => graph.Domain), Does.Contain("关卡蓝图"));
        Assert.That(snapshot.Document.Graphs.Select(static graph => graph.Domain), Does.Contain("技能 GAS"));
        Assert.That(snapshot.Document.StateMachines, Has.Count.EqualTo(1));
        Assert.That(snapshot.Document.BehaviorTrees, Has.Count.EqualTo(1));
        Assert.That(snapshot.Compile.Success, Is.True);

        var graphIds = snapshot.Document.Graphs.Select(static graph => graph.Id).ToHashSet(StringComparer.Ordinal);
        Assert.That(
            snapshot.Document.StateMachines.SelectMany(static fsm => fsm.Nodes).Any(node => graphIds.Contains(node.ImplementationGraphId)),
            Is.True,
            "FSM nodes must navigate to implementation Graph documents.");
        Assert.That(
            snapshot.Document.BehaviorTrees.SelectMany(static tree => tree.Nodes).Any(node => graphIds.Contains(node.ImplementationGraphId)),
            Is.True,
            "BT nodes must navigate to implementation Graph documents.");
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
    public void RuntimeDebug_ComesFromEcsRuntimeAndTracksSelectedEntityNodes()
    {
        using World world = World.Create();
        var dataPlane = new GraphWorkbenchDataPlane(world);

        WebUiCommandResult selectResult = dataPlane.ApplyCommand(new WebUiCommandRequest(
            GraphWorkbenchShowcaseIds.SelectEntityCommand,
            1,
            Array.Empty<WebUiEntityRef>(),
            JsonSerializer.SerializeToElement(new { entityId = "entity.rifle-squad" }, JsonOptions)));

        Assert.That(selectResult.Success, Is.True);
        dataPlane.AdvanceRuntime(1f / 30f);
        dataPlane.AdvanceRuntime(1f / 30f);
        dataPlane.AdvanceRuntime(1f / 30f);

        GraphWorkbenchSnapshot snapshot = CreateSnapshot(dataPlane);

        Assert.That(snapshot.Runtime.Source, Is.EqualTo("ecs-runtime"));
        Assert.That(snapshot.Runtime.SelectedEntityId, Is.EqualTo("entity.rifle-squad"));
        Assert.That(snapshot.Runtime.CurrentStateMachineId, Is.EqualTo("rts.stance"));
        Assert.That(snapshot.Runtime.CurrentBehaviorTreeId, Is.EqualTo("unit.assault_bt"));
        Assert.That(snapshot.Runtime.CurrentStateNodeId, Is.Not.Empty);
        Assert.That(snapshot.Runtime.CurrentBehaviorNodeId, Is.Not.Empty);
        Assert.That(snapshot.Runtime.CurrentGraphNodeId, Is.Not.Empty);
    }

    [Test]
    public void LauncherRegistry_ExposeGraphWorkbenchAsToolingNotAi()
    {
        string repoRoot = FindRepoRoot();
        string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
        string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));
        string registry = File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json"));
        string webAppSource = Path.Combine(repoRoot, "mods", "showcases", "graph_workbench", "GraphWorkbenchShowcaseMod", "WebApp", "src", "main.jsx");
        string docs = Path.Combine(repoRoot, "gitbook", "architecture", "graph-workbench-showcase.md");

        Assert.That(launcherConfig, Does.Contain("graph_workbench_showcase"));
        Assert.That(launcherPresets, Does.Contain("graph_workbench_cef_raylib"));
        Assert.That(registry, Does.Contain("\"id\": \"graph_workbench\""));
        Assert.That(registry, Does.Contain("\"category\": \"tooling\""));
        Assert.That(registry, Does.Contain("\"gas\""));
        Assert.That(File.Exists(webAppSource), Is.True);
        Assert.That(File.Exists(docs), Is.True);
        Assert.That(File.ReadAllText(webAppSource), Does.Not.Contain("catch(() => {})"));
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
}
