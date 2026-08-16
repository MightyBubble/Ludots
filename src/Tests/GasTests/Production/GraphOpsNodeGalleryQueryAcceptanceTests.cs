using System;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using Ludots.Core.Presentation.DebugDraw;
using NUnit.Framework;

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
    public void QueryAllMapEntities_CountMatchesSeededMapEntities()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryAllMapEntities");
        QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("把场上的人都找出来"));
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
        Assert.That(runtime.Metrics.Detail, Does.Contain("花名册"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(6));
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
    }

    [Test]
    public void QueryFilterTeam_KeepsHostileCamp()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterTeam");
        QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("敌对阵营"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(10));
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
    }

    [Test]
    public void AggMaxEntityByAttribute_NamesTheThickest()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("AggMaxEntityByAttribute");
        QueryNodeDriver driver = (QueryNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("谁最能打（血最厚）"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("最厚"));
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
