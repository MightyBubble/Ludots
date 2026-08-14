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
    public void RetiredFamilyAggregates_HaveNoPlayerLaunchBinding()
    {
        string repoRoot = FindRepoRoot();
        using JsonDocument registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json")));
        using JsonDocument launcher = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json")));

        var launcherNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement binding in launcher.RootElement.GetProperty("bindings").EnumerateArray())
        {
            string? name = binding.GetProperty("name").GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                launcherNames.Add(name);
            }
        }

        string[] familyIds =
        {
            "capability_standard_graph_ops_attr",
            "capability_standard_graph_ops_float",
            "capability_standard_graph_ops_script",
            "capability_standard_graph_ops_spatial",
            "capability_standard_graph_ops_query",
            "capability_standard_graph_ops_rel",
            "capability_standard_graph_ops_blackboard",
            "capability_standard_graph_ops_event",
        };

        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement showcase in registry.RootElement.GetProperty("showcases").EnumerateArray())
        {
            string? id = showcase.GetProperty("id").GetString();
            if (!string.IsNullOrWhiteSpace(id))
            {
                byId[id] = showcase;
            }
        }

        foreach (string id in familyIds)
        {
            Assert.That(byId.ContainsKey(id), Is.True, $"Missing retired family showcase '{id}'.");
            JsonElement entry = byId[id];
            Assert.That(entry.GetProperty("status").GetString(), Is.EqualTo("retired"), id);
            Assert.That(entry.GetProperty("binding").ValueKind, Is.EqualTo(JsonValueKind.Null), id);
            Assert.That(entry.GetProperty("preset").ValueKind, Is.EqualTo(JsonValueKind.Null), id);
            Assert.That(launcherNames, Does.Not.Contain(id), $"Retired family '{id}' still has a launcher binding.");
        }
    }

    [Test]
    public void EveryVignette_TicksOnce_WithChineseCaption()
    {
        string repoRoot = FindRepoRoot();
        string assets = Path.Combine(repoRoot, GraphOpsNodeIds.ModAssetsRelative);
        var failures = new List<string>();
        foreach (GraphNodeOp op in Enum.GetValues<GraphNodeOp>())
        {
            if (op == GraphNodeOp.None)
            {
                continue;
            }

            string name = op.ToString();
            try
            {
                using var runtime = new GraphOpsNodeGalleryRuntime();
                runtime.BindOp(name);
                runtime.EnsureWorld();
                int ticks = name is "Yield" or "JumpIfFalse" ? 12 : 1;
                for (int i = 0; i < ticks; i++)
                {
                    runtime.Tick(0.35f);
                }

                if (runtime.Metrics.ThinkWaves < 1)
                {
                    failures.Add($"{name}: think waves stayed 0");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(runtime.Metrics.Detail))
                {
                    failures.Add($"{name}: empty caption");
                }

                if (runtime.Metrics.Detail.Contains('{', StringComparison.Ordinal))
                {
                    failures.Add($"{name}: unsubstituted caption '{runtime.Metrics.Detail}'");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.That(failures, Is.Empty, "Per-op gallery tick failures:\n" + string.Join("\n", failures));
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
