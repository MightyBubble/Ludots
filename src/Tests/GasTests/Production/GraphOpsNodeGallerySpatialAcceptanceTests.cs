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
    public void QueryCone_LightsEveryoneInsideTheFan_EdgeContrastStaysDark()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryCone");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("朝这个方向的扇形里有谁"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("扇形"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("弧内"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(6));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(6));
        AssertHudLit(runtime, "ally", true);
        AssertHudLit(runtime, "hexN", true);
        AssertHudLit(runtime, "hexNW", true);
        AssertHudLit(runtime, "edgeOut", false);
        AssertHudLit(runtime, "caster", false);
        Assert.That(driver.CasterInList, Is.False);
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
        Assert.That(runtime.Title, Is.EqualTo("按名单取第一个"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("名单第 1 个"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contain("没有人"));
        Assert.That(driver.LastTargetCount, Is.GreaterThan(0));
        Assert.That(driver.FocusIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(driver.FocusIndex, Is.LessThan(driver.UnitCount));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void QueryFilterNotEntity_LeavesSelfOffTheList_TwoBeatsStageSelfFirst()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterNotEntity");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("圈人时把你自己抠出去"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("排除自己"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("名单里没有自己"));
        Assert.That(driver.CasterInList, Is.False);
        Assert.That(driver.LastTargetCount, Is.EqualTo(6));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(6));
        AssertHudLit(runtime, "caster", false);
        AssertHudLit(runtime, "north", true);

        runtime.Tick(0.35f);
        Assert.That(runtime.Context.Wave, Is.EqualTo(2));
        AssertHudLit(runtime, "caster", true);
        AssertHudLit(runtime, "north", true);
        Assert.That(runtime.Metrics.Detail, Does.Contain("名单剩6人"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void QueryFilterRelationship_KeepsHostileMercenary_DropsFriendlyTraitor()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterRelationship");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("只留敌对关系的人"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(4));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(4));
        AssertHudLit(runtime, "north", true);
        AssertHudLit(runtime, "hexN", true);
        AssertHudLit(runtime, "mercenary", true);
        AssertHudLit(runtime, "traitor", false);
        AssertHudLit(runtime, "ally", false);
        AssertHudLit(runtime, "caster", false);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void QueryFilterLayer_KeepsEnemyLayerOnly_MercenaryFlipsOff()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryFilterLayer");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("只留敌方层的人"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(3));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(3));
        AssertHudLit(runtime, "north", true);
        AssertHudLit(runtime, "hexN", true);
        AssertHudLit(runtime, "hexNW", true);
        AssertHudLit(runtime, "mercenary", false);
        AssertHudLit(runtime, "traitor", false);
        AssertHudLit(runtime, "ally", false);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void AggCount_CountsEveryoneInTheFan()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("AggCount");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("扇形里数出几个人"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(6));
        Assert.That(runtime.Metrics.Detail, Does.Contain("扇形内共6人"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void AggMinByDistance_NamesTheNearestPersonInTheFan()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("AggMinByDistance");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("扇形里谁离我最近"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("最近的"));
        Assert.That(runtime.Context.CaptionValues["name"], Is.EqualTo("友军"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("友军"));
        Assert.That(driver.FocusIndex, Is.GreaterThanOrEqualTo(0));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void QueryRectangle_FramesTheGroundAhead_OfTheCaster()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryRectangle");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("身前这块矩形里有谁"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(2));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(2));
        AssertHudLit(runtime, "ally", true);
        AssertHudLit(runtime, "diag", true);
        AssertHudLit(runtime, "north", false);
        AssertHudLit(runtime, "side", false);
        AssertHudLit(runtime, "farDiag", false);
        AssertHudLit(runtime, "caster", false);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void QueryLine_HitsTheTwoOnTheLine_NearMissStaysOut()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryLine");
        SpatialNodeDriver driver = (SpatialNodeDriver)runtime.Driver;

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("这条窄线穿过谁"));
        Assert.That(driver.LastTargetCount, Is.EqualTo(2));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(2));
        AssertHudLit(runtime, "diag", true);
        AssertHudLit(runtime, "farDiag", true);
        AssertHudLit(runtime, "near", false);
        AssertHudLit(runtime, "caster", false);
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
        Assert.That(runtime.Title, Is.EqualTo("贴身六格邻居"));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(6));
        Assert.That(driver.LastTargetCount, Is.EqualTo(6));
        Assert.That(runtime.Metrics.Detail, Does.Contain("6"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("多一格"));
        AssertWorldHealth(runtime, "caster", 100f);
        AssertWorldHealth(runtime, "east", 100f);
        AssertWorldHealth(runtime, "northeast", 100f);
        AssertWorldHealth(runtime, "northwest", 100f);
        AssertWorldHealth(runtime, "west", 100f);
        AssertWorldHealth(runtime, "southwest", 100f);
        AssertWorldHealth(runtime, "southeast", 100f);
        AssertWorldHealth(runtime, "outer", 100f);
        AssertHudLit(runtime, "caster", false);
        AssertHudLit(runtime, "east", true);
        AssertHudLit(runtime, "outer", false);
        AssertHealthDisclosure(runtime, "east", true);
        AssertHealthDisclosure(runtime, "outer", false);
        AssertActorHealthMatchesWorld(runtime);
    }

    [Test]
    public void QueryHexRange_LightsPeopleInsideTwoHexes_NotTheOuterOne()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryHexRange");

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("两格以内的六角范围"));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(5));
        Assert.That(runtime.Metrics.Detail, Does.Contain("第三格"));
        AssertWorldHealth(runtime, "caster", 100f);
        AssertWorldHealth(runtime, "ring1a", 100f);
        AssertWorldHealth(runtime, "ring1b", 100f);
        AssertWorldHealth(runtime, "ring2a", 100f);
        AssertWorldHealth(runtime, "ring2b", 100f);
        AssertWorldHealth(runtime, "ring2c", 100f);
        AssertWorldHealth(runtime, "outer", 100f);
        AssertHudLit(runtime, "ring1a", true);
        AssertHudLit(runtime, "outer", false);
        AssertHealthDisclosure(runtime, "ring1a", true);
        AssertHealthDisclosure(runtime, "outer", false);
        AssertActorHealthMatchesWorld(runtime);
    }

    [Test]
    public void QueryHexRing_LightsOnlyTheRing_NotInsideOrOutside()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("QueryHexRing");

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("只取半径 2 的六角环"));
        Assert.That(int.Parse(runtime.Context.CaptionValues["count"]), Is.EqualTo(3));
        Assert.That(runtime.Metrics.Detail, Does.Contain("环上"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("里圈和环外"));
        AssertWorldHealth(runtime, "caster", 100f);
        AssertWorldHealth(runtime, "ringEast", 100f);
        AssertWorldHealth(runtime, "ringSouth", 100f);
        AssertWorldHealth(runtime, "ringWest", 100f);
        AssertWorldHealth(runtime, "innerEast", 100f);
        AssertWorldHealth(runtime, "innerSouth", 100f);
        AssertWorldHealth(runtime, "outer", 100f);
        AssertHudLit(runtime, "ringEast", true);
        AssertHudLit(runtime, "innerEast", false);
        AssertHudLit(runtime, "outer", false);
        AssertHudLit(runtime, "caster", false);
        AssertHealthDisclosure(runtime, "ringEast", true);
        AssertHealthDisclosure(runtime, "innerEast", false);
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

    private static void AssertHudLit(GraphOpsNodeGalleryRuntime runtime, string actorId, bool expected)
    {
        int index = GraphOpsNodeActorBinding.IndexOfId(runtime.Vignette, actorId);
        Assert.That(runtime.Context.ActorHudLit[index], Is.EqualTo(expected), actorId);
    }

    private static void AssertHealthDisclosure(GraphOpsNodeGalleryRuntime runtime, string actorId, bool expected)
    {
        int index = GraphOpsNodeActorBinding.IndexOfId(runtime.Vignette, actorId);
        Assert.That(
            GraphOpsNodeActorBinding.IsHealthDisclosed(runtime.Context, index),
            Is.EqualTo(expected),
            actorId);
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
