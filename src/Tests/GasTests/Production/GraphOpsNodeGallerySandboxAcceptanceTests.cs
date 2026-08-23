using System;
using System.IO;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGallerySandboxAcceptanceTests
{
    private static readonly string[] SandboxOps =
    [
        "HasTag",
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
        Assert.That(runtime.Title, Is.EqualTo("查一查身上有没有那枚标记"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("标记"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("「有」"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("「无」"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void QueryRadius_LightsFiveUnitsWithoutCaster()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("QueryRadius");
        runtime.EnsureWorld();
        float[] before = (float[])runtime.Context.ActorHealth.Clone();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("5个兵"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("不含施法者"));
        var actors = runtime.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            Assert.That(runtime.Context.ActorHealth[i], Is.EqualTo(before[i]).Within(0.01f), actors[i].Id);
            float dist = MathF.Sqrt(actors[i].X * actors[i].X + actors[i].Y * actors[i].Y);
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
            {
                Assert.That(runtime.Context.ActorHudLit[i], Is.False, "the caster must not count itself as a hit.");
                continue;
            }

            Assert.That(
                runtime.Context.ActorHudLit[i],
                Is.EqualTo(dist <= 8f),
                $"{actors[i].Id} disclosure must follow the notSelf radius hit list.");
        }

        Assert.That(runtime.Context.HitTargetCount, Is.EqualTo(5));
    }

    [Test]
    public void QueryLimit_KeepsFirstThreeByStableOrder()
    {
        using var runtime = TickOp("QueryLimit");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("前三个"));
        var ctx = runtime.Context;
        Assert.That(ctx.HitTargetCount, Is.EqualTo(3));
        var kept = new HashSet<int>();
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            kept.Add(GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]));
        }

        var actors = runtime.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            Assert.That(ctx.ActorHudLit[i], Is.EqualTo(kept.Contains(i)), actors[i].Id);
        }

        int caster = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "caster");
        Assert.That(kept, Does.Not.Contain(caster), "the limit list must not keep the caster.");
    }

    [Test]
    public void QuerySortStable_OrdersByEntityIdAndRepeatsEveryWave()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("QuerySortStable");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        string firstWave = runtime.Metrics.Detail;
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Is.EqualTo(firstWave), "stable order must repeat identically wave to wave");
        var ctx = runtime.Context;
        Assert.That(ctx.HitTargetCount, Is.EqualTo(5));
        int caster = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "caster");
        Assert.That(ctx.ActorHudLit[caster], Is.False, "the caster stands at the center without a number.");
        var byEntityId = new List<int>();
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (i != caster)
            {
                byEntityId.Add(i);
            }
        }

        byEntityId.Sort((a, b) => ctx.SimActors[a].Id.CompareTo(ctx.SimActors[b].Id));
        var expectedNames = new List<string>();
        for (int i = 0; i < byEntityId.Count; i++)
        {
            int actorIndex = byEntityId[i];
            float dx = runtime.Vignette.Actors[actorIndex].X;
            float dy = runtime.Vignette.Actors[actorIndex].Y;
            if (MathF.Sqrt(dx * dx + dy * dy) > 8f)
            {
                continue;
            }

            expectedNames.Add(runtime.Vignette.Actors[actorIndex].Name);
        }

        Assert.That(expectedNames.Count, Is.EqualTo(ctx.HitTargetCount));
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            Assert.That(
                runtime.Vignette.Actors[actorIndex].Name,
                Is.EqualTo(expectedNames[i]),
                "hit order must follow entity id order");
        }

        Assert.That(
            runtime.Metrics.Detail,
            Does.Contain("点名顺序稳定：" + string.Join("、", expectedNames) + "。"));
    }

    [Test]
    public void RelationshipSetMetric_MapsLoyaltyEightyOntoAllyBar()
    {
        using var runtime = TickOp("RelationshipSetMetric");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("直接写成80"));
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
        Assert.That(runtime.Context.ActorHealth[ally], Is.EqualTo(100f).Within(0.01f));
        Assert.That(runtime.Context.ActorHudLit[ally], Is.True);
        Assert.That(runtime.Metrics.Detail, Does.Contain("80"));
    }

    [Test]
    public void RelationshipEnsureLink_BondsCasterAndAlly()
    {
        using var runtime = TickOp("RelationshipEnsureLink");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("关系链"));
        int ally = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(ally, Is.GreaterThanOrEqualTo(0));
        Assert.That(runtime.Context.ActorHudLit[ally], Is.True, "the ally bar must turn visible once the link snaps on.");
    }

    [Test]
    public void RelationshipAddMetric_GrowsLoyaltyToSeventyOnClipboard()
    {
        using var runtime = TickOp("RelationshipAddMetric");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("停在70"));
        int ally = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(runtime.Context.ActorHealth[ally], Is.EqualTo(100f).Within(0.01f), "loyalty must not touch the ally health bar.");
    }

    [Test]
    public void RelationshipHasFlag_ReadsTrustedOnTheBond()
    {
        using var runtime = TickOp("RelationshipHasFlag");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("信任旗"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("信得过"));
        int ally = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(runtime.Context.ActorHudLit[ally], Is.True);
    }

    [Test]
    public void FanOutApplyEffect_StrikesRealDamageOntoCircleUnits()
    {
        using var runtime = TickOp("FanOutApplyEffect");
        var ctx = runtime.Context;
        Assert.That(ctx.EffectRequests!.Count, Is.EqualTo(0), "settlement must consume the effect request queue");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("5人"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("圈外两人完好"));
        var actors = runtime.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            float dist = MathF.Sqrt(actors[i].X * actors[i].X + actors[i].Y * actors[i].Y);
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            float expected = dist <= 8f ? 100f - 18f : 100f;
            Assert.That(ctx.ActorHealth[i], Is.EqualTo(expected).Within(0.01f), actors[i].Id);
            Assert.That(ctx.ActorHudLit[i], Is.EqualTo(dist <= 8f), actors[i].Id);
        }
    }

    [Test]
    public void FanOutApplyEffectDynamic_StrikesCircleFromDrawnCard()
    {
        using var runtime = TickOp("FanOutApplyEffectDynamic");
        var ctx = runtime.Context;
        Assert.That(ctx.EffectRequests!.Count, Is.EqualTo(0), "settlement must consume the effect request queue");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("翻到的牌是打击"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("5人"));
        var actors = runtime.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            float dist = MathF.Sqrt(actors[i].X * actors[i].X + actors[i].Y * actors[i].Y);
            float expected = dist <= 8f ? 100f - 18f : 100f;
            Assert.That(ctx.ActorHealth[i], Is.EqualTo(expected).Within(0.01f), actors[i].Id);
        }
    }

    [Test]
    public void ApplyEffectDynamic_DropsStakeHealthByDrawnCard()
    {
        using var runtime = TickOp("ApplyEffectDynamic");
        var ctx = runtime.Context;
        Assert.That(ctx.EffectRequests!.Count, Is.EqualTo(0), "settlement must consume the effect request queue");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("这张牌是打击"));
        int stake = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        int caster = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "caster");
        Assert.That(ctx.ActorHealth[stake], Is.EqualTo(82f).Within(0.01f), "the drawn card must settle real Strike damage.");
        Assert.That(ctx.ActorHealth[caster], Is.EqualTo(100f).Within(0.01f));
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
        string engineGraphs = Path.Combine(repoRoot, "assets", "GAS", "graphs.json");
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
