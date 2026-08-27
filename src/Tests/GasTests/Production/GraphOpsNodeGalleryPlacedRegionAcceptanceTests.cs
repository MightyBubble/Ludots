using System;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// #1108 TriggerGraph-only LoadPlacedRegion / LoadPlacedAnchor per-op galleries.
/// Region: direct ExecuteSlice reports authored yard=1 and intentional ghost=0.
/// Anchor: reuses the placed-entity roll-call driver against InstanceId containing "anchor".
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryPlacedRegionAcceptanceTests
{
    [Test]
    public void LoadPlacedRegion_RollCallReportsYardPresentAndGhostAbsent()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadPlacedRegion");
        runtime.EnsureWorld();

        Assert.That(runtime.Vignette.GraphKind, Is.EqualTo("TriggerGraph"));

        runtime.Tick(0.35f);

        var driver = (PlacedRegionNodeDriver)runtime.Driver;
        Assert.That(driver.LastResult.IntValue, Is.EqualTo(1),
            "featured LoadPlacedRegion(yard) must write presence 1");
        Assert.That(runtime.Metrics.Detail, Does.Contain("营地圈=1"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("鬼区=0"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void LoadPlacedAnchor_RollCallHitsLiveInstanceThenNullAfterKill()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadPlacedAnchor");
        runtime.EnsureWorld();

        Assert.That(runtime.Vignette.GraphKind, Is.EqualTo("TriggerGraph"));

        runtime.Tick(0.35f);

        var driver = (PlacedEntityNodeDriver)runtime.Driver;
        Assert.That(driver.LastResult.EntityValue, Is.EqualTo(runtime.Context.Target),
            "wave one must resolve the placed camp_anchor instance registered at map load");
        Assert.That(runtime.Metrics.Detail, Does.Contain("在岗应答"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("100"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));

        runtime.Tick(0.35f);

        Assert.That(driver.LastResult.EntityValue, Is.EqualTo(Arch.Core.Entity.Null),
            "after the placed anchor is destroyed the same graph must read Entity.Null");
        Assert.That(runtime.Metrics.Detail, Does.Contain("位置空缺"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("0"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }
}
