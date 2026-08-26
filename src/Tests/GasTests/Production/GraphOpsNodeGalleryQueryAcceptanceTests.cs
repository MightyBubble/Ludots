using System;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using NUnit.Framework;

using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryQueryAcceptanceTests
{
    private static readonly string[] QueryOps =
    [
        "QueryAllMapEntities",
        "QueryFromCollection",
        "QueryFilterTeam",
        "QueryFilterTemplate",
        "QueryFilterAttributeRange",
        "QueryFilterTagAny",
        "QueryFilterTagNone",
        "QuerySortByAttribute",
        "AggSumAttribute",
        "AggAverageAttribute",
        "AggMaxAttribute",
        "AggMinAttribute",
        "AggMaxEntityByAttribute",
        "AggMinEntityByAttribute"
    ];

    [Test]
    public void QueryCollectActiveEffects_ListsSeededBuffs()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryCollectActiveEffects");
        QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("效果"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(3));
    }

    [TestCase("QueryCollectEffectTemplates", true, "说明书")]
    [TestCase("QueryCollectAbilitySlots", true, "技能格")]
    [TestCase("QueryCollectInventoryItems", false, "背包")]
    [TestCase("QueryCollectItemDefinitions", true, "物品")]
    [TestCase("QueryCollectPresentTags", true, "印记")]
    [TestCase("QueryCollectActiveTasks", false, "差事")]
    [TestCase("QueryCollectProgressionNodes", true, "进度")]
    [TestCase("QueryCollectAbilityHolders", false, "会这招")]
    public void TypedCollectors_ExecuteWithNonEmptyBags(string op, bool intIdBag, string expectedCopy)
    {
        using GraphOpsNodeGalleryRuntime runtime = Play(op);
        QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain(expectedCopy));
        Assert.That(
            intIdBag ? driver.LastIntIdCount : driver.LastTargetCount,
            Is.GreaterThan(0));
    }

    [Test]
    public void QueryAllMapEntities_CountMatchesSeededMapEntities()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryAllMapEntities");
        QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("把场上的人全点名"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("场上"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(runtime.Vignette.Actors.Length));
        Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.Vignette.Actors.Length.ToString()));
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
    }

    [Test]
    public void QueryFromCollection_LightsTheRoster()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFromCollection");
        QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("点名线"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(6));
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
    }

    [Test]
    public void QueryFilterTeam_KeepsHostileCamp()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterTeam");
        QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("红方"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(10));
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
    }

    [Test]
    public void AggMaxEntityByAttribute_NamesTheThickest()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("AggMaxEntityByAttribute");
        QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("点名最能扛的"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("点名徽"));
        Assert.That(driver.StrongestIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(driver.StrongestIndex, Is.LessThan(driver.UnitCount));
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
    }

    [Test]
    public void SortByAttribute_OverlayCrownsTopThreeWithRankPipsAndCommanderArrow()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QuerySortByAttribute");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(runtime.Title, Is.EqualTo("按血量从厚到薄排队"));
        Assert.That(debugDraw.Boxes.Count, Is.GreaterThanOrEqualTo(6), "top three ranks wear 3+2+1 pips over their heads");
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0), "commander seat must aim a bright arrow at the champion's head");
    }

    [Test]
    public void QueryFilterTemplate_FullAndResultWavesAlternateCirclesAndGhosts()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterTemplate");

        var resultWave = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(resultWave);
        Assert.That(resultWave.Circles.Count, Is.GreaterThanOrEqualTo(4), "two scouts keep circles plus the seat's double rings");
        Assert.That(resultWave.Lines.Count, Is.GreaterThanOrEqualTo(240), "ten soldiers ghost as 8-arc afterimages");

        runtime.Context.Wave = 2;
        var fullWave = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(fullWave);
        Assert.That(fullWave.Circles.Count, Is.GreaterThanOrEqualTo(14), "the full wave circles all twelve candidates plus the seat's double rings");
    }

    [Test]
    public void QueryFilterTagAny_CrownsNineEnemyBadges()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterTagAny");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Boxes.Count, Is.EqualTo(9), "nine red enemy diamonds ride the tagged heads");
        Assert.That(runtime.Metrics.Detail, Does.Contain("红徽"));
    }

    [Test]
    public void QueryFilterTagNone_BadgesTheDeadUnitAndGhostsItInTheResultWave()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterTagNone");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Boxes.Count, Is.EqualTo(1), "only the Dead-tagged scout wears the death badge");
        Assert.That(debugDraw.Lines.Count, Is.GreaterThanOrEqualTo(24), "the badge wearer ghosts to an afterimage");
        Assert.That(runtime.Metrics.Detail, Does.Contain("阵亡徽"));
    }

    [Test]
    public void AggMinAttribute_ResultWaveExitsCirclesAndKeepsOnlyThePanel()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("AggMinAttribute");
        var resultWave = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(resultWave);
        Assert.That(resultWave.Circles.Count, Is.EqualTo(2), "only the seat's double rings remain; no candidate is named");
        Assert.That(resultWave.Lines.Count, Is.GreaterThan(0), "the single-cell panel shows the min digit");

        runtime.Context.Wave = 2;
        var fullWave = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(fullWave);
        Assert.That(fullWave.Circles.Count, Is.GreaterThanOrEqualTo(14), "the full wave circles all candidates");
        Assert.That(runtime.Metrics.Detail, Does.Contain("最低生命"));
    }

    [Test]
    public void AggMinEntityByAttribute_NamesTheWeakestWithBadgeLineAndHpFloat()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("AggMinEntityByAttribute");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Boxes.Count, Is.GreaterThanOrEqualTo(1), "the red diamond name badge marks the weakest");
        Assert.That(debugDraw.Circles.Count, Is.EqualTo(3), "the named unit keeps one circle plus the seat's double rings");
        Assert.That(debugDraw.Lines.Count, Is.GreaterThanOrEqualTo(265), "eleven ghosts plus the name line and the hp float");
        Assert.That(runtime.Metrics.Detail, Does.Contain("点名线"));
    }

    [Test]
    public void QueryFromCollection_ResultWavePullsRosterLinesToSixMembers()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFromCollection");
        var debugDraw = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Boxes.Count, Is.GreaterThanOrEqualTo(6), "the roster board lights six cells");
        Assert.That(debugDraw.Circles.Count, Is.GreaterThanOrEqualTo(8), "six roster members keep circles plus the seat's double rings");
        Assert.That(debugDraw.Lines.Count, Is.GreaterThanOrEqualTo(150), "six ghosts plus six name lines plus the board frame");
        Assert.That(runtime.Metrics.Detail, Does.Contain("点名线"));
    }

    [Test]
    public void QueryAllMapEntities_ScanLightsUnitsProgressively()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("QueryAllMapEntities");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        var wave1 = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(wave1);
        runtime.Tick(0.35f);
        runtime.Tick(0.35f);
        var wave3 = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(wave3);
        runtime.Tick(0.35f);
        runtime.Tick(0.35f);
        var wave5 = new DebugDrawCommandBuffer();
        runtime.DrawOverlay(wave5);
        Assert.That(wave3.Circles.Count, Is.GreaterThan(wave1.Circles.Count), "the scan lights more units as it sweeps");
        Assert.That(wave5.Circles.Count, Is.GreaterThan(wave3.Circles.Count), "the scan keeps lighting more units");
        Assert.That(runtime.Metrics.Detail, Does.Contain("场上"));
    }

    [Test]
    public void QueryFamily_CaptionsCarryRealCountsAndAggregates()
    {
        using (GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterTemplate"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("留圈2个"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterTeam"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("红方10个"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterAttributeRange"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("血量不超过40"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("短血条留圈"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("QueryFromCollection"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("名册上6人"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("不在册的6个"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterTagAny"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("红徽的9个"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterTagNone"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("其余11个"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("AggSumAttribute"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("合计800"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("AggAverageAttribute"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("生命合计800"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("平均62"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("AggMinAttribute"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("最低生命0"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("AggMaxAttribute"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("最高生命150"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("AggMinEntityByAttribute"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("血0"));
        }

        using (GraphOpsNodeGalleryRuntime runtime = Play("AggMaxEntityByAttribute"))
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("血150"));
        }
    }

    [Test]
    public void EveryQueryGallery_HasChineseNumbers_AndNonZeroCount()
    {
        foreach (string op in QueryOps)
        {
            using var runtime = new GraphOpsNodeGalleryRuntime();
            runtime.BindOp(op);
            runtime.EnsureWorld();
            runtime.Tick(0.35f);
            QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

            AssertBannedPlayerCopy(runtime.Metrics.Detail, op);
            Assert.That(runtime.Metrics.Detail, Does.Not.Contain(op), op);
            Assert.That(driver.LastTargetCount, Is.GreaterThan(0), op);
            foreach (string phrase in runtime.Vignette.AssertDetailContains)
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), op);
            }
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

    private static void AssertBannedPlayerCopy(string detail, string op = "")
    {
        Assert.That(detail, Does.Not.Contain("tally"), op);
        Assert.That(detail, Does.Not.Contain("Validation"), op);
        Assert.That(detail, Does.Not.Contain("FuncLib"), op);
        Assert.That(detail, Does.Not.Contain("True"), op);
        Assert.That(detail, Does.Not.Contain("False"), op);
        Assert.That(detail, Does.Not.Contain("耗时"), op);
        Assert.That(detail, Does.Not.Contain("GraphNodeOp"), op);
        Assert.That(detail, Does.Not.Contain("QueryAllMapEntities"), op);
        Assert.That(detail, Does.Not.Contain("AggMaxEntityByAttribute"), op);
    }
}
