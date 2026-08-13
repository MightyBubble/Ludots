using System;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGallerySpatialAcceptanceTests
{
    [Test]
    public void QueryCone_LightsPeopleInsideTheFan()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryCone");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("扇形里有谁"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("扇"));
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.GreaterThan(0));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void TargetListGet_NamesTheFirstPerson()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("TargetListGet");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("点名单上的第一个"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("第一个"));
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
        Assert.That(driver.FocusIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(driver.FocusIndex, Is.LessThan(driver.UnitCount));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void QueryFilterNotEntity_LeavesSelfOffTheList()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterNotEntity");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("圈人时排除自己"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("排除自己"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("名单里没有自己"));
        Assert.That(driver.CasterInList, Is.False);
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void QueryHexNeighbors_LightsTheSixAdjacentPeople_NotTheOuterOne()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryHexNeighbors");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("贴着的六格邻居"));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(6));
        Assert.That(driver.LastTargetCount, Is.EqualTo(6));
        Assert.That(runtime.Metrics.Detail, Does.Contain("6"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("没亮"));
        AssertWorldHealth(runtime, "caster", 100f);
        AssertWorldHealth(runtime, "east", 100f);
        AssertWorldHealth(runtime, "northeast", 100f);
        AssertWorldHealth(runtime, "northwest", 100f);
        AssertWorldHealth(runtime, "west", 100f);
        AssertWorldHealth(runtime, "southwest", 100f);
        AssertWorldHealth(runtime, "southeast", 100f);
        AssertWorldHealth(runtime, "outer", 0f);
        AssertActorHealthMatchesWorld(runtime);
    }

    [Test]
    public void QueryHexRange_LightsPeopleInsideTwoHexes_NotTheOuterOne()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryHexRange");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(5));
        Assert.That(runtime.Metrics.Detail, Does.Contain("没亮"));
        AssertWorldHealth(runtime, "caster", 100f);
        AssertWorldHealth(runtime, "ring1a", 100f);
        AssertWorldHealth(runtime, "ring1b", 100f);
        AssertWorldHealth(runtime, "ring2a", 100f);
        AssertWorldHealth(runtime, "ring2b", 100f);
        AssertWorldHealth(runtime, "ring2c", 100f);
        AssertWorldHealth(runtime, "outer", 0f);
        AssertActorHealthMatchesWorld(runtime);
    }

    [Test]
    public void QueryHexRing_LightsOnlyTheRing_NotInsideOrOutside()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryHexRing");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(3));
        Assert.That(runtime.Metrics.Detail, Does.Contain("里外"));
        AssertWorldHealth(runtime, "caster", 100f);
        AssertWorldHealth(runtime, "ringEast", 100f);
        AssertWorldHealth(runtime, "ringSouth", 100f);
        AssertWorldHealth(runtime, "ringWest", 100f);
        AssertWorldHealth(runtime, "innerEast", 0f);
        AssertWorldHealth(runtime, "innerSouth", 0f);
        AssertWorldHealth(runtime, "outer", 0f);
        AssertActorHealthMatchesWorld(runtime);
    }

    [Test]
    public void SpatialGalleries_PlayerCopyStaysChinese_AndTargetListIsNonEmpty()
    {
        string[] ops =
        [
            "QueryCone",
            "QueryRectangle",
            "QueryLine",
            "QueryFilterNotEntity",
            "QueryFilterLayer",
            "QueryFilterRelationship",
            "AggCount",
            "AggMinByDistance",
            "TargetListGet",
            "QueryHexRange",
            "QueryHexRing",
            "QueryHexNeighbors"
        ];

        foreach (string op in ops)
        {
            using var runtime = new GraphOpsNodeGalleryRuntime();
            runtime.BindOp(op);
            runtime.EnsureWorld();
            runtime.Tick(0.35f);
            SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

            AssertBannedPlayerCopy(runtime.Metrics.Detail, op);
            Assert.That(runtime.Metrics.Detail, Does.Not.Contain(op), op);
            Assert.That(driver.LastTargetCount, Is.GreaterThan(0), op);
            AssertActorHealthMatchesWorld(runtime);
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

    private static void AssertWorldHealth(GraphOpsNodeGalleryRuntime runtime, string actorId, float expected)
    {
        int index = GraphOpsNodeActorBinding.IndexOfId(runtime.Vignette, actorId);
        float world = GraphOpsNodeActorBinding.ReadHealth(
            runtime.Context.SimWorld,
            runtime.Context.SimActors[index]);
        Assert.That(runtime.Context.ActorHealth[index], Is.EqualTo(expected).Within(0.01f), actorId);
        Assert.That(world, Is.EqualTo(expected).Within(0.01f), actorId);
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
                ctx.Vignette.Actors[i].Id);
        }
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
        Assert.That(detail, Does.Not.Contain("QueryCone"), op);
        Assert.That(detail, Does.Not.Contain("TargetListGet"), op);
        Assert.That(detail, Does.Not.Contain("opcode"), op);
    }
}
