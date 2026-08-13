using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryAcceptanceTests
{
    [Test]
    public void ConstFloat_SetsTargetHealthToAuthoredConstant()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ConstFloat");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("写死的一刀"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("写死"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("42"));
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(1));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(42f).Within(0.01f));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void AddFloat_SubtractsSumFromTargetHealth()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("AddFloat");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("两段伤害叠在一起"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("加上"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("一共"));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(before - 42f).Within(0.01f));
    }

    [Test]
    public void ExistingVignettes_CompileWithFeaturedOp()
    {
        string assets = GraphOpsNodeGalleryRuntime.ResolveAssetsRoot();
        string vignetteDir = Path.Combine(assets, "Vignettes");
        string[] files = Directory.GetFiles(vignetteDir, "*.json");
        Assert.That(files, Is.Not.Empty, "Node gallery has no vignettes.");

        foreach (string file in files)
        {
            string op = Path.GetFileNameWithoutExtension(file);
            GraphOpsNodeVignette vignette = GraphOpsNodeVignetteLoader.Load(assets, op);
            var compiled = GraphOpsNodeGraphCompiler.Compile(assets, vignette);
            Assert.That(compiled.Succeeded, Is.True, op);
            Assert.That(
                compiled.Program.Any(i => i.Op == (ushort)Enum.Parse<GraphNodeOp>(op)),
                Is.True,
                $"{op} compiled program must emit the featured opcode.");
        }
    }

    [Test]
    public void EveryExecutableOp_HasVignetteGraphAndUniqueShowcaseId()
    {
        string repoRoot = FindRepoRoot();
        string assets = Path.Combine(repoRoot, GraphOpsNodeIds.ModAssetsRelative);
        string coveragePath = Path.Combine(repoRoot, "assets/Configs/GAS/graph_node_op_coverage.registry.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(coveragePath));
        var coverageIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonElement entry in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            coverageIds[entry.GetProperty("op").GetString()!] = entry.GetProperty("showcaseId").GetString()!;
        }

        var missing = new List<string>();
        foreach (GraphNodeOp op in Enum.GetValues<GraphNodeOp>())
        {
            if (op == GraphNodeOp.None)
            {
                continue;
            }

            string name = op.ToString();
            string expectedId = GraphOpsNodeIds.ShowcaseId(name);
            if (!coverageIds.TryGetValue(name, out string? actualId) ||
                !string.Equals(actualId, expectedId, StringComparison.Ordinal))
            {
                missing.Add($"{name}: coverage showcaseId must be {expectedId}");
            }

            string vignette = Path.Combine(assets, "Vignettes", name + ".json");
            string graph = Path.Combine(assets, "GAS", "graphs", name + ".json");
            if (!File.Exists(vignette))
            {
                missing.Add($"{name}: missing Vignettes/{name}.json");
            }

            if (!File.Exists(graph))
            {
                missing.Add($"{name}: missing GAS/graphs/{name}.json");
            }
        }

        Assert.That(missing, Is.Empty, "Per-op galleries incomplete:\n" + string.Join("\n", missing));
    }

    [Test]
    public void GeneratedMaps_SpawnEveryVignetteActor()
    {
        string assets = GraphOpsNodeGalleryRuntime.ResolveAssetsRoot();
        string vignetteDir = Path.Combine(assets, "Vignettes");
        string mapsDir = Path.Combine(assets, "Maps");
        foreach (string file in Directory.GetFiles(vignetteDir, "*.json"))
        {
            string op = Path.GetFileNameWithoutExtension(file);
            GraphOpsNodeVignette vignette = GraphOpsNodeVignetteLoader.Load(assets, op);
            string mapPath = Path.Combine(mapsDir, GraphOpsNodeIds.MapId(op) + ".json");
            Assert.That(File.Exists(mapPath), Is.True, op);
            using JsonDocument map = JsonDocument.Parse(File.ReadAllText(mapPath));
            JsonElement entities = map.RootElement.GetProperty("Entities");
            Assert.That(entities.GetArrayLength(), Is.EqualTo(vignette.Actors.Length), op);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entity in entities.EnumerateArray())
            {
                ids.Add(entity.GetProperty("InstanceId").GetString()!);
            }

            foreach (GraphOpsNodeActor actor in vignette.Actors)
            {
                Assert.That(ids, Does.Contain(actor.Id), $"{op} map missing {actor.Id}");
            }
        }
    }

    private static void AssertBannedPlayerCopy(string detail)
    {
        Assert.That(detail, Does.Not.Contain("tally"));
        Assert.That(detail, Does.Not.Contain("Validation"));
        Assert.That(detail, Does.Not.Contain("FuncLib"));
        Assert.That(detail, Does.Not.Contain("True"));
        Assert.That(detail, Does.Not.Contain("False"));
        Assert.That(detail, Does.Not.Contain("耗时"));
        Assert.That(detail, Does.Not.Contain("GraphNodeOp"));
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
