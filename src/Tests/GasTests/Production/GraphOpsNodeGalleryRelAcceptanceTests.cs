using System.Text.RegularExpressions;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

using Ludots.Platform.Abstractions;

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

    [Test]
    public void QueryMutual_OverlayKeepsGrayFieldAndLightsDoubleArrowsOnly()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipQueryMutual");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int gray = CountLines(debugDraw, DebugDrawColor.Gray);
        int yellow = CountLines(debugDraw, DebugDrawColor.Yellow);
        Assert.That(gray, Is.GreaterThan(0), "mutual overlay must keep the whole gray dashed chain field");
        Assert.That(yellow, Is.GreaterThanOrEqualTo(5), "each double-arrow mutual chain draws a body plus four wings");
        Assert.That(runtime.Context.CaptionValues["friendCount"], Is.EqualTo("2"));
    }

    [Test]
    public void FilterFlag_OverlayDrawsTrustFlagsOnGreenChains()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipFilterFlag");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int green = CountLines(debugDraw, DebugDrawColor.Green);
        int yellow = CountLines(debugDraw, DebugDrawColor.Yellow);
        Assert.That(green, Is.GreaterThanOrEqualTo(6), "two trusted chains: body plus wings each");
        Assert.That(yellow, Is.GreaterThanOrEqualTo(3), "flag badge glyph needs its pole plus two cloth edges");
        Assert.That(runtime.Context.CaptionValues["friendCount"], Is.EqualTo("2"));
    }

    [Test]
    public void QueryOutgoing_OverlayKeepsGrayFieldAndLightsOutgoingArrows()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipQueryOutgoing");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int gray = CountLines(debugDraw, DebugDrawColor.Gray);
        int yellow = CountLines(debugDraw, DebugDrawColor.Yellow);
        Assert.That(gray, Is.GreaterThan(0), "outgoing overlay must keep the gray chain field");
        Assert.That(yellow, Is.GreaterThanOrEqualTo(12), "four outgoing chains: body plus wings each");
    }

    [Test]
    public void FilterMetricRange_OverlayDrawsGateAndHeadClipboards()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipFilterMetricRange");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int white = CountLines(debugDraw, DebugDrawColor.White);
        int yellow = CountLines(debugDraw, DebugDrawColor.Yellow);
        Assert.That(white, Is.GreaterThan(0), "gate posts and clipboard frames must be white");
        Assert.That(yellow, Is.GreaterThan(0), "threshold ticks and loyalty numbers must be yellow");
        Assert.That(runtime.Context.CaptionValues["friendCount"], Is.EqualTo("3"));
    }

    [Test]
    public void SortByMetric_OverlayDrawsRankBadgesAboveHeads()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipSortByMetric");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Boxes.Count, Is.GreaterThanOrEqualTo(4), "each friend wears a white rank badge frame");
        Assert.That(runtime.Context.CaptionValues["friend"], Is.EqualTo("好友1"));
        Assert.That(runtime.Context.CaptionValues["loyalty"], Is.EqualTo("85"));
    }

    [Test]
    public void HasLink_OverlayDrawsChainLinkOnlyWhileTheLinkIsLive()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipHasLink");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int green = CountLines(debugDraw, DebugDrawColor.Green);
        Assert.That(green, Is.GreaterThan(0), "live seed link must draw the green chain-link line");
        Assert.That(debugDraw.Circles.Count, Is.GreaterThanOrEqualTo(2), "chain-link glyph is two interlocked rings");
        Assert.That(runtime.Metrics.Detail, Does.Contain("链着"));
    }

    [Test]
    public void GetMetric_OverlayDrawsReadingCardWithTrueLoyalty()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipGetMetric");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(runtime.Context.CaptionValues["loyalty"], Is.EqualTo("85"));
        Assert.That(runtime.Context.CaptionValues["friend"], Is.EqualTo("好友1"));
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0));
    }

    [Test]
    public void RemoveLink_OverlayKeepsGhostsOfSeveredChains()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipRemoveLink");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int gray = CountLines(debugDraw, DebugDrawColor.Gray);
        Assert.That(gray, Is.GreaterThan(0), "severed chain must leave dashed gray ghost segments");
        Assert.That(runtime.Context.CaptionValues["friend"], Is.EqualTo("好友4"));
    }

    [Test]
    public void AggSumMetric_OverlayDrawsBenchWithGraphSum()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipAggSumMetric");
        Assert.That(runtime.Context.CaptionValues["sum"], Is.EqualTo("230"));
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0));
    }

    [Test]
    public void AggAverageMetric_OverlayDrawsBenchWithTruncatedAverage()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipAggAverageMetric");
        Assert.That(runtime.Context.CaptionValues["avg"], Is.EqualTo("57"));
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0));
    }

    [Test]
    public void AggMinMetric_NamesWeakestFriendAndBenchLiftsItsCard()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipAggMinMetric");
        Assert.That(runtime.Context.CaptionValues["min"], Is.EqualTo("35"));
        Assert.That(runtime.Context.CaptionValues["friend"], Is.EqualTo("好友4"));
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0));
    }

    [Test]
    public void AggMaxMetric_NamesStrongestFriendAndBenchLiftsItsCard()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipAggMaxMetric");
        Assert.That(runtime.Context.CaptionValues["max"], Is.EqualTo("85"));
        Assert.That(runtime.Context.CaptionValues["friend"], Is.EqualTo("好友1"));
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0));
    }

    [Test]
    public void AggMinEntityByMetric_OverlayLightsOnlyTheWeakestPerson()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipAggMinEntityByMetric");
        Assert.That(runtime.Context.CaptionValues["friend"], Is.EqualTo("好友4"));
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int yellow = CountLines(debugDraw, DebugDrawColor.Yellow);
        Assert.That(yellow, Is.GreaterThanOrEqualTo(3), "winner chain: body plus wings");
    }

    [Test]
    public void AggMaxEntityByMetric_OverlayLightsOnlyTheStrongestPerson()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipAggMaxEntityByMetric");
        Assert.That(runtime.Context.CaptionValues["friend"], Is.EqualTo("好友1"));
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int yellow = CountLines(debugDraw, DebugDrawColor.Yellow);
        Assert.That(yellow, Is.GreaterThanOrEqualTo(3), "winner chain: body plus wings");
    }

    [Test]
    public void QueryBetweenPair_OverlayDrawsDoubleArrowAndChainLinkWithCountOne()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipQueryBetweenPair");
        Assert.That(runtime.Context.CaptionValues["friendCount"], Is.EqualTo("1"));
        Assert.That(runtime.Context.CaptionValues["friend"], Is.EqualTo("好友1"));
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        int yellow = CountLines(debugDraw, DebugDrawColor.Yellow);
        Assert.That(yellow, Is.GreaterThanOrEqualTo(5), "double-arrow pair line: body plus four wings");
        Assert.That(debugDraw.Circles.Count, Is.GreaterThanOrEqualTo(2), "chain-link ring at the pair midpoint");
    }

    private static int CountLines(DebugDrawCommandBuffer debugDraw, DebugDrawColor color)
    {
        int count = 0;
        foreach (DebugDrawLine2D line in debugDraw.Lines)
        {
            if (line.Color.Equals(color))
            {
                count++;
            }
        }

        return count;
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
