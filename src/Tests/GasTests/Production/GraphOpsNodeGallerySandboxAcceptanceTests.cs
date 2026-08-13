using System;
using System.IO;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[Category("ci-gate")]
public sealed class GraphOpsNodeGallerySandboxAcceptanceTests
{
    private static readonly string[] SandboxOps =
    [
        "HasTag",
        "SelectTagInMask",
        "LookupTagDisplayToken",
        "QueryRadius",
        "QuerySortStable",
        "QueryLimit",
        "FanOutApplyEffect",
        "ApplyEffectDynamic",
        "FanOutApplyEffectDynamic",
        "RelationshipEnsureLink",
        "RelationshipSetMetric",
        "RelationshipAddMetric",
        "RelationshipHasFlag"
    ];

    [Test]
    public void HasTag_ScoutCarriesEnemyMark()
    {
        using var runtime = TickOp("HasTag");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("身上有没有敌人标记"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("标记"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("有"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void QueryRadius_LightsInRangeAndLeavesFarUnits()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("QueryRadius");
        runtime.EnsureWorld();
        float[] before = (float[])runtime.Context.ActorHealth.Clone();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("近处"));
        bool inRangeLit = false;
        bool farUnchanged = false;
        var actors = runtime.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            float dist = MathF.Sqrt(actors[i].X * actors[i].X + actors[i].Y * actors[i].Y);
            if (dist <= 8f)
            {
                if (Math.Abs(runtime.Context.ActorHealth[i] - before[i]) > 0.01f)
                {
                    inRangeLit = true;
                }
            }
            else if (Math.Abs(runtime.Context.ActorHealth[i] - before[i]) < 0.01f)
            {
                farUnchanged = true;
            }
        }

        Assert.That(inRangeLit, Is.True, "In-range units must change health after QueryRadius.");
        Assert.That(farUnchanged, Is.True, "Out-of-range units must stay still.");
        Assert.That(runtime.Metrics.Detail, Does.Not.Match("摸到0个近处"));
    }

    [Test]
    public void RelationshipSetMetric_MapsLoyaltyEightyOntoAllyBar()
    {
        using var runtime = TickOp("RelationshipSetMetric");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("写成"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("80"));
        int ally = -1;
        var actors = runtime.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, "target", StringComparison.Ordinal))
            {
                ally = i;
                break;
            }
        }

        Assert.That(ally, Is.GreaterThanOrEqualTo(0));
        Assert.That(runtime.Context.ActorHealth[ally], Is.EqualTo(80f).Within(0.01f));
    }

    [Test]
    public void SandboxVignettes_TickWithChineseCaptions()
    {
        foreach (string op in SandboxOps)
        {
            using var runtime = TickOp(op);
            AssertBannedPlayerCopy(runtime.Metrics.Detail);
            Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(1), op);
            Assert.That(runtime.Metrics.Detail, Is.Not.Empty, op);
            foreach (string phrase in runtime.Vignette.AssertDetailContains)
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), $"{op} caption missing '{phrase}'");
            }
        }
    }

    [Test]
    public void SandboxCatalog_LivesInGalleryAssetsNotEngineGraphs()
    {
        string assets = GraphOpsNodeGalleryRuntime.ResolveAssetsRoot();
        Assert.That(File.Exists(Path.Combine(assets, "GAS", "sandbox", "catalog.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(assets, "GAS", "graphs.json")), Is.False);
        string repoRoot = FindRepoRoot();
        string engineGraphs = Path.Combine(repoRoot, "assets", "Configs", "GAS", "graphs.json");
        if (File.Exists(engineGraphs))
        {
            string text = File.ReadAllText(engineGraphs);
            Assert.That(text, Does.Not.Contain("showcase.graph_op.HasTag"));
            Assert.That(text, Does.Not.Contain("showcase.graph_op.QueryRadius"));
        }
    }

    private static GraphOpsNodeGalleryRuntime TickOp(string op)
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        return runtime;
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
        Assert.That(detail, Does.Not.Contain("opcode"));
        Assert.That(detail, Does.Not.Contain("Opcode"));
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
