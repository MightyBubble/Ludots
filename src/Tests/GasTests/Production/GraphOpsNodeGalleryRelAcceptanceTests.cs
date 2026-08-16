using System.Text.RegularExpressions;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Presentation.DebugDraw;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryRelAcceptanceTests
{
    private static readonly string[] RelFamilyOps =
    [
        "RelationshipRemoveLink",
        "RelationshipGetMetric",
        "RelationshipSetFlag",
        "RelationshipQueryOutgoing",
        "RelationshipQueryIncoming",
        "RelationshipQueryMutual",
        "RelationshipQueryBetweenPair",
        "RelationshipFilterMetricRange",
        "RelationshipFilterFlag",
        "RelationshipSortByMetric",
        "RelationshipAggSumMetric",
        "RelationshipAggMaxMetric",
        "RelationshipAggAverageMetric",
        "RelationshipAggMinMetric",
        "RelationshipAggMaxEntityByMetric",
        "RelationshipAggMinEntityByMetric",
        "RelationshipHasLink"
    ];

    [Test]
    public void QueryOutgoing_FriendCountGreaterThanZero()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipQueryOutgoing");
        Assert.That(int.Parse(runtime.Context.CaptionValues["friendCount"]), Is.GreaterThan(0));
        Assert.That(runtime.Metrics.Detail, Does.Contain("我交的"));
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [Test]
    public void RemoveLink_DecreasesLinks()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipRemoveLink");
        int before = int.Parse(runtime.Context.CaptionValues["linksBefore"]);
        int after = int.Parse(runtime.Context.CaptionValues["linksAfter"]);
        Assert.That(after, Is.LessThan(before));
        Assert.That(runtime.Metrics.Detail, Does.Contain("拆掉"));
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [Test]
    public void RemoveLink_SecondThinkStillBreaksALink()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipRemoveLink");
        runtime.Tick(0.35f);
        int before = int.Parse(runtime.Context.CaptionValues["linksBefore"]);
        int after = int.Parse(runtime.Context.CaptionValues["linksAfter"]);
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(2));
        Assert.That(after, Is.LessThan(before));
        Assert.That(runtime.Metrics.Detail, Does.Contain("拆掉"));
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [Test]
    public void GetMetric_CaptionContainsNumber()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipGetMetric");
        Assert.That(runtime.Metrics.Detail, Does.Contain("好感"));
        Assert.That(Regex.IsMatch(runtime.Metrics.Detail, @"\d+"), Is.True, runtime.Metrics.Detail);
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [Test]
    public void AggMaxEntityByMetric_NamesHighestPerson()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipAggMaxEntityByMetric");
        Assert.That(runtime.Metrics.Detail, Does.Contain("最高的人"));
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [Test]
    public void QueryIncoming_OverlayDrawsGrayFieldThenYellowArrowsPointingAtMe()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipQueryIncoming");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int gray = 0;
        int yellow = 0;
        CountLineColors(debugDraw, DebugDrawColor.Gray, ref gray, DebugDrawColor.Yellow, ref yellow);
        Assert.That(gray, Is.GreaterThan(0), "incoming overlay must keep the whole gray dashed field of chains");
        Assert.That(yellow, Is.GreaterThan(0), "incoming overlay must light the chains whose arrows point at me");
    }

    [Test]
    public void SetFlag_OverlayPlantsFlagBadgeOnlyAfterTheGraphPlantsIt()
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("RelationshipSetFlag");
        runtime.EnsureWorld();
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.EqualTo(0), "flag badge must not draw before the graph really plants Estranged");

        runtime.Tick(0.35f);
        debugDraw.Clear();
        runtime.DrawOverlay(debugDraw);
        int cyan = 0;
        int red = 0;
        CountLineColors(debugDraw, DebugDrawColor.Cyan, ref cyan, DebugDrawColor.Red, ref red);
        Assert.That(cyan, Is.GreaterThan(0), "planted estranged chain must draw as a cyan directed line");
        Assert.That(red, Is.GreaterThanOrEqualTo(3), "flag badge glyph needs its pole plus two cloth edges");
    }

    private static void CountLineColors(
        DebugDrawCommandBuffer debugDraw,
        DebugDrawColor first,
        ref int firstCount,
        DebugDrawColor second,
        ref int secondCount)
    {
        foreach (DebugDrawLine2D line in debugDraw.Lines)
        {
            if (line.Color.Equals(first))
            {
                firstCount++;
            }

            if (line.Color.Equals(second))
            {
                secondCount++;
            }
        }
    }

    [TestCaseSource(nameof(RelFamilyOps))]
    public void RelFamilyOp_RendersPlayerCaption(string op)
    {
        using GraphOpsNodeGalleryRuntime runtime = Play(op);
        AssertBannedEnglish(runtime.Metrics.Detail);
        AssertActorHealthMatchesWorld(runtime);
        int caster = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "caster");
        Assert.That(runtime.Context.ActorHudLit[caster], Is.True, op);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    private static GraphOpsNodeGalleryRuntime Play(string op)
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        return runtime;
    }

    private static void AssertActorHealthMatchesWorld(GraphOpsNodeGalleryRuntime runtime)
    {
        GraphOpsNodeDriverContext ctx = runtime.Context;
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            float world = GraphOpsNodeActorBinding.ReadHealth(ctx.SimWorld, ctx.SimActors[i]);
            Assert.That(
                world,
                Is.EqualTo(ctx.ActorHealth[i]).Within(0.01f),
                $"{runtime.Op}:{ctx.Vignette.Actors[i].Id}");
        }
    }

    private static void AssertBannedEnglish(string detail)
    {
        Assert.That(detail, Does.Not.Contain("tally"));
        Assert.That(detail, Does.Not.Contain("Validation"));
        Assert.That(detail, Does.Not.Contain("FuncLib"));
        Assert.That(detail, Does.Not.Contain("True"));
        Assert.That(detail, Does.Not.Contain("False"));
        Assert.That(detail, Does.Not.Contain("耗时"));
        Assert.That(detail, Does.Not.Contain("GraphNodeOp"));
        Assert.That(detail, Does.Not.Contain("loyalty"));
        Assert.That(detail, Does.Not.Contain("Loyalty"));
        Assert.That(detail, Does.Not.Contain("Trusted"));
        Assert.That(detail, Does.Not.Contain("Estranged"));
        Assert.That(detail, Does.Not.Contain("SocialBond"));
        Assert.That(detail, Does.Not.Contain("Relationship"));
    }
}
