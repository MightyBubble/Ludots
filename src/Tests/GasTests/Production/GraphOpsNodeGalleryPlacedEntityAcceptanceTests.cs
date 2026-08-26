using System;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// #1108 TriggerGraph-only LoadPlacedEntity per-op gallery: the roll-call entry resolves
/// the real placed instance from the mounting map's catalog (hit), mirrors its health
/// into a map variable, and — after the driver destroys the placed entity (the
/// KillOneTeamEntity 手法) — reads Entity.Null through the same graph on the next wave.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryPlacedEntityAcceptanceTests
{
    [Test]
    public void LoadPlacedEntity_RollCallHitsLiveInstanceThenNullAfterKill()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadPlacedEntity");
        runtime.EnsureWorld();

        Assert.That(runtime.Vignette.GraphKind, Is.EqualTo("TriggerGraph"));

        runtime.Tick(0.35f);

        var driver = (PlacedEntityNodeDriver)runtime.Driver;
        Assert.That(driver.LastResult.EntityValue, Is.EqualTo(runtime.Context.Target),
            "wave one must resolve the placed boss_camp instance registered at map load");
        Assert.That(runtime.Metrics.Detail, Does.Contain("在岗应答"),
            "the caption must report the hit state");
        Assert.That(runtime.Metrics.Detail, Does.Contain("100"),
            "the map variable must mirror the live placed entity's health (100)");
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));

        runtime.Tick(0.35f);

        Assert.That(driver.LastResult.EntityValue, Is.EqualTo(Arch.Core.Entity.Null),
            "after the placed entity is destroyed the same graph must read Entity.Null (stale index handle + IsAlive insurance)");
        Assert.That(runtime.Metrics.Detail, Does.Contain("位置空缺"),
            "the caption must report the miss state");
        Assert.That(runtime.Metrics.Detail, Does.Contain("0"),
            "the map variable must mirror 0 once the placed read is Null");
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }
}
