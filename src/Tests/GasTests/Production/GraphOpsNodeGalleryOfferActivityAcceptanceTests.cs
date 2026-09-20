using System;
using System.Linq;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Per-op gallery acceptance for the OfferActivity op: the vignette Script graph
/// runs on the shared headless gallery engine (which owns the real
/// ActivityRuntimeService and the activity-bound graph API), each think wave offers the
/// gallery activity to the placed caster scope host, and the caption reports the roll-call.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryOfferActivityAcceptanceTests
{
    [Test]
    public void OfferActivity_RollCallOffersGalleryActivityEachWave()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("OfferActivity");
        runtime.EnsureWorld();

        Assert.That(runtime.Vignette.GraphKind, Is.EqualTo("Script"));

        GameEngine engine = GraphOpsHeadlessGameEngine.SharedGallery(FindRepoRoot());
        var activities = engine.GetService(CoreServiceKeys.ActivityRuntimeService) as ActivityRuntimeService
            ?? throw new InvalidOperationException("ActivityRuntimeService missing from the shared gallery engine.");
        Func<int> offeredCount = () => activities.CaptureViews()
            .Count(v => v.ActivityId == "gallery.op.offer_activity" && v.State != ActivityInstanceState.Resolved);
        int before = offeredCount();

        runtime.Tick(0.35f);

        Assert.That(offeredCount() - before, Is.EqualTo(1),
            "wave one must open exactly one new gallery activity instance (shared-engine totals are order-dependent, so assert the delta)");
        Assert.That(activities.CaptureViews().Any(v => v.ActivityId == "gallery.op.offer_activity" && v.State == ActivityInstanceState.Active),
            Is.True, "a forced activity must present immediately");
        Assert.That(runtime.Metrics.Detail, Does.Contain("活动已派发"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));

        runtime.Tick(0.35f);

        // Script 画廊第一波跑到 Halt 后程序游标停机，后续波次不得重复供给（ScriptNodeDriver 语义）
        Assert.That(offeredCount() - before, Is.EqualTo(1),
            "the halted Script program must not re-offer on later waves");
        Assert.That(activities.CaptureViews().Any(v => v.ActivityId == "gallery.op.offer_activity" && v.State == ActivityInstanceState.Active),
            Is.True, "the offered activity must remain active across waves");
    }

    private static string FindRepoRoot()
    {
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "showcase.registry.json")))
        {
            dir = System.IO.Path.GetDirectoryName(dir);
        }

        return dir ?? throw new System.IO.DirectoryNotFoundException("Repository root not found.");
    }
}
