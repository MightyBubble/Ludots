using System;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Aimsource pure-helper per-op galleries: each vignette compiles as a real Query
/// document, runs against the production aimsource kernel over the gallery binding,
/// and surfaces the computed ground point / picked entity / region count / direction
/// angle in the player caption.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryAimSourceAcceptanceTests
{
    [Test]
    public void ScreenPointToGround_QuotesResolvedGroundPoint()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("ScreenPointToGround");

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("把光标钉到地上"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("落点钉在东 4 米、北 1.5 米"),
            "the caption must quote the ground point the kernel chain resolved");
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void ScreenPointToEntity_NamesThePickedCandidate()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("ScreenPointToEntity");

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("点名线落在敌军1身上"),
            "the pointer lands on the westernmost unit; the caption names the picked candidate");
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void ScreenRegionToEntities_CountsTheRectHits()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("ScreenRegionToEntities");

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("圈住2人"),
            "the western rect covers the two westernmost units only");
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void PointToDirection_QuotesTheAimAngle()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("PointToDirection");

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("炮口指向 46 度"),
            "caster (0,-2.2) → point (3.3,1.2) aims at ~45.9 degrees");
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void StickToDirection_QuotesTheStickAngle()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("StickToDirection");

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("摇杆掰向 45 度"),
            "stick (0.7,0.7) aims at 45 degrees");
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
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(1));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
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
    }
}
