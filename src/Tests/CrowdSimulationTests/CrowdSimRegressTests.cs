using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>回归场景表、终点分区和 p95。场景内容来自 S4 演示 Mod 的表，不在测试里另写一份脚本。</summary>
public sealed class CrowdSimRegressTests
{
    [Test]
    public void NearestRank_UsesCeilingIndex()
    {
        var samples = new double[20];
        for (int i = 0; i < samples.Length; i++) samples[i] = i;
        Assert.That(CrowdSimRegress.NearestRank(samples, 0.95), Is.EqualTo(18));
        Assert.That(CrowdSimRegress.NearestRank(ReadOnlySpan<double>.Empty, 0.95), Is.EqualTo(0));
    }

    [Test]
    public void CollectDifferences_NamesChangedPartitions()
    {
        var left = new CrowdSimStateDigest(1, 1, 2, 3, 4, 5, 6, 7);
        var right = new CrowdSimStateDigest(1, 9, 2, 3, 4, 5, 6, 7);
        var names = new List<string>();
        CrowdSimChecksum.CollectDifferences(left, 1, right, 2, names);
        Assert.That(names, Is.EqualTo(new[] { CrowdSimChecksum.PositionSection, CrowdSimChecksum.HandlesSection }));
    }

    [Test]
    public void Catalog_RejectsUnknownField()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdSimRegressCatalog.Parse(JsonNode.Parse("""{"extra":1}""")!.AsObject()));
        Assert.That(ex!.Message, Does.Contain("extra"));
    }

    [Test]
    public void Catalog_ReadsDeployRebakeAndGaps()
    {
        string root = RepoRoot();
        var catalog = CrowdSimRegressCatalog.Parse(JsonNode.Parse(File.ReadAllText(CatalogPath(root)))!.AsObject());
        Assert.That(catalog.Defaults, Is.EqualTo(new[] { "deploy", "rebake", "areas" }));
        Assert.That(catalog.Full, Is.EqualTo(new[] { "stress" }));
        Assert.That(catalog.Scenarios["areas"].UnavailableReason, Is.Null);
        Assert.That(catalog.Scenarios["areas"].ScriptFromRepo, Is.True);
        Assert.That(catalog.Scenarios["stress"].ScriptFromRepo, Is.True);
        var plan = catalog.CreateFuzzPlan(7, 200);
        Assert.That(plan.Ops.Any(op => op.Id == "kill" && !op.Reserved && op.ProbeHandles), Is.True);
        var tools = JsonNode.Parse(File.ReadAllText(Path.Combine(
            root, "mods/capabilities/navigation/CrowdSimulationUiMod/assets/CrowdSimulationDebug.json")))!
            ["tools"]!.AsObject();
        Assert.That(plan.BoomRadiusCm, Is.EqualTo(tools["boomRadiusCm"]!.GetValue<int>()));
        Assert.That(plan.RingScale.ToDouble(), Is.EqualTo(tools["ringScale"]!.GetValue<double>()).Within(1e-6));
        Assert.That(plan.Impulse.ToDouble(), Is.EqualTo(tools["impulse"]!.GetValue<double>()).Within(1e-6));
        Assert.That(plan.Seed, Is.EqualTo(7));
        Assert.That(plan.OpCount, Is.EqualTo(200));

        var deployDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(
            root, catalog.Scenarios["deploy"].SeedAssets!, catalog.Scenarios["deploy"].ScriptAsset!)))!.AsObject();
        Assert.That(CrowdSimRegressCatalog.TickCount(deployDoc, "frames"), Is.EqualTo(150));
        var rebakeDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(
            root, catalog.Scenarios["rebake"].SeedAssets!, catalog.Scenarios["rebake"].ScriptAsset!)))!.AsObject();
        Assert.That(CrowdSimRegressCatalog.TickCount(rebakeDoc, "ticks"), Is.EqualTo(360));
    }

    [Test]
    public void UnavailableScenario_DoesNotOpenSession()
    {
        var report = CrowdSimRegress.Run(new CrowdSimRegressRequest
        {
            Scenarios = new[]
            {
                new CrowdSimRegressScenario { Id = "areas", UnavailableReason = "没有脚本" },
            },
            Repeat = 1,
            OpenScenario = _ => throw new InvalidOperationException("不应打开会话"),
        });
        Assert.That(report.Passed, Is.False);
        Assert.That(report.Scenarios[0].Unavailable, Is.True);
        Assert.That(report.Scenarios[0].Error, Does.Contain("没有脚本"));
    }

    [Test]
    public void TwoRepeats_MatchEndChecksum()
    {
        var report = CrowdSimRegress.Run(new CrowdSimRegressRequest
        {
            Scenarios = new[] { Empty(2) },
            Repeat = 2,
            OpenScenario = OpenLand,
        });
        Assert.That(report.Passed, Is.True, report.Scenarios[0].Error);
        Assert.That(report.Scenarios[0].Differing, Is.Empty);
        Assert.That(report.Scenarios[0].P95TickMs, Is.GreaterThan(0));
    }

    [Test]
    public void Budget_FailsWhenP95IsAboveIt()
    {
        var report = CrowdSimRegress.Run(new CrowdSimRegressRequest
        {
            Scenarios = new[] { Empty(2) },
            Repeat = 1,
            BudgetMs = -1,
            OpenScenario = OpenLand,
        });
        Assert.That(report.Passed, Is.False);
        Assert.That(report.Scenarios[0].Error, Does.Contain("p95"));
    }

    private static CrowdSimRegressScenario Empty(int ticks) => new()
    {
        Id = "empty",
        Script = new JsonArray(),
        Ticks = ticks,
        Movement = true,
        WithFog = false,
    };

    private static CrowdSimRegressHost OpenLand(CrowdSimRegressScenario _)
    {
        var world = CrowdBeliefTerrainTests.LandWorld.Open(withFog: false);
        return new CrowdSimRegressHost
        {
            Session = world.Session,
            BakeMs = 1,
            Release = world.Dispose,
        };
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(CatalogPath(dir.FullName))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到回归场景表。");
    }

    private static string CatalogPath(string root) => Path.Combine(
        root,
        "mods",
        "showcases",
        "crowd_simulation",
        "CrowdSimulationS4DeployMod",
        "assets",
        "CrowdSimulation",
        "regress",
        "catalog.json");
}
