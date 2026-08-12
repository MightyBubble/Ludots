using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CapabilityStandardGraphOpsEventMod.Runtime;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[Category("ci-gate")]
public sealed class GraphOpsEventShowcaseAcceptanceTests
{
    [Test]
    public void EventVignette_DispatchControlDomainAndSnap_PlayerReadable()
    {
        using var runtime = new GraphOpsEventRuntime();
        runtime.BindStandaloneFromModAssets();
        runtime.EnsureWorld();

        for (int i = 0; i < 4; i++) runtime.Tick(0.2f);
        runtime.Metrics.MaxThinkMs = 0;
        runtime.Metrics.LastThinkMs = 0;
        for (int i = 0; i < 12; i++) runtime.Tick(0.2f);

        TestContext.WriteLine(
            $"{runtime.Metrics.ShowcaseId}: waves={runtime.Metrics.ThinkWaves} max={runtime.Metrics.MaxThinkMs:F3} detail={runtime.Metrics.Detail}");

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("发事件"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("控制域"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("知识投影"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("扇出派发"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("吸附"));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(25.0));
        });
    }

    [Test]
    public void EventVignette_DispatchGraph_ContainsEventControlOps()
    {
        GraphControlFlowCompileResult compiled = CompileModGraph("Graph.GraphOpsEvent.Dispatch");
        var emitted = compiled.Program.Select(i => (GraphNodeOp)i.Op).ToHashSet();

        GraphNodeOp[] required =
        [
            GraphNodeOp.LoadViewer,
            GraphNodeOp.LoadEventPayloadInt,
            GraphNodeOp.LoadEventPayloadFloat,
            GraphNodeOp.ControlDomainResolve,
            GraphNodeOp.ControlDomainControls,
            GraphNodeOp.KnowledgeHasProjection,
            GraphNodeOp.SendEvent,
            GraphNodeOp.FanOutDispatchEffect,
            GraphNodeOp.FanOutDispatchEffectDynamic
        ];

        foreach (GraphNodeOp op in required)
        {
            Assert.That(emitted, Does.Contain(op), $"Dispatch graph missing {op}");
        }
    }

    [Test]
    public void EventVignette_PlacementGraph_ContainsSnapOps()
    {
        GraphControlFlowCompileResult compiled = CompileModGraph("Graph.GraphOpsEvent.Placement");
        var emitted = compiled.Program.Select(i => (GraphNodeOp)i.Op).ToHashSet();

        GraphNodeOp[] required =
        [
            GraphNodeOp.LoadTargetPosX,
            GraphNodeOp.LoadTargetPosY,
            GraphNodeOp.ClampTargetToRange,
            GraphNodeOp.IsPointInCircle,
            GraphNodeOp.SnapToNearestInCollection,
            GraphNodeOp.SnapToNearestGraphEdge
        ];

        foreach (GraphNodeOp op in required)
        {
            Assert.That(emitted, Does.Contain(op), $"Placement graph missing {op}");
        }
    }

    private static GraphControlFlowCompileResult CompileModGraph(string graphId)
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods/showcases/capability_standard/CapabilityStandardGraphOpsEventMod/assets/GAS/graphs.json");
        Assert.That(File.Exists(path), Is.True, path);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement match = default;
        bool found = false;
        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            if (entry.GetProperty("id").GetString() == graphId)
            {
                match = entry;
                found = true;
                break;
            }
        }

        Assert.That(found, Is.True, $"Missing graph '{graphId}' in Event mod graphs.json");

        JsonObject obj = JsonNode.Parse(match.GetRawText())!.AsObject();
        obj.Remove("id");
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        Assert.That(compiled.Succeeded, Is.True, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        return compiled;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "Repository root not found.");
        return dir!.FullName;
    }
}
