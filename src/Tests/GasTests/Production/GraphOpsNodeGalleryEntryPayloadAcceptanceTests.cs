using System;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// TriggerGraph-only LoadEntryPayload* per-op galleries: each vignette compiles as a real
/// TriggerGraph document (entries table + named payload key), captures its entry event's
/// schema-declared payload, and executes the entry body to halt with the captured value
/// surfaced in the player caption.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryEntryPayloadAcceptanceTests
{
    [Test]
    public void LoadEntryPayloadEntity_CaptionNamesCapturedSource()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadEntryPayloadEntity");
        runtime.EnsureWorld();

        Assert.That(runtime.Vignette.GraphKind, Is.EqualTo("TriggerGraph"));
        AssertCompiledEntryEvent(runtime, "EntityDied");

        runtime.Tick(0.35f);

        var driver = (EntryPayloadNodeDriver)runtime.Driver;
        Assert.That(driver.LastResult.EntityValue, Is.EqualTo(runtime.Context.Target),
            "the captured MapTrigger.SourceEntity must be the staged dying entity");
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(1));
        Assert.That(runtime.Metrics.Detail, Does.Contain("木桩"),
            "the caption must name the entity read from the entry payload");
        Assert.That(runtime.Metrics.Detail, Does.Contain("事件载荷"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void LoadEntryPayloadInt_CaptionReportsCapturedCount()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadEntryPayloadInt");
        runtime.EnsureWorld();

        Assert.That(runtime.Vignette.GraphKind, Is.EqualTo("TriggerGraph"));
        AssertCompiledEntryEvent(runtime, "EntityAliveCountChanged");

        runtime.Tick(0.35f);

        var driver = (EntryPayloadNodeDriver)runtime.Driver;
        Assert.That(driver.LastResult.IntValue, Is.EqualTo(3),
            "the captured MapTrigger.Count must read back through the named payload key");
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(1));
        Assert.That(runtime.Metrics.Detail, Does.Contain("3"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("事件载荷"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void LoadEntryPayloadFloat_CaptionReportsCapturedGroundX()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadEntryPayloadFloat");
        runtime.EnsureWorld();

        Assert.That(runtime.Vignette.GraphKind, Is.EqualTo("TriggerGraph"));
        AssertCompiledEntryEvent(runtime, "InputActionFired");

        runtime.Tick(0.35f);

        var driver = (EntryPayloadNodeDriver)runtime.Driver;
        Assert.That(driver.LastResult.FloatValue, Is.EqualTo(360.5f).Within(0.001f),
            "the captured MapTrigger.GroundXCm must keep its fraction through the named read");
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(1));
        Assert.That(runtime.Metrics.Detail, Does.Contain("360.5"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("事件载荷"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    private static void AssertCompiledEntryEvent(GraphOpsNodeGalleryRuntime runtime, string expectedEvent)
    {
        if (!runtime.Context.Compiled.Package.HasValue ||
            runtime.Context.Compiled.Package.Value.TriggerGraphEntries is not { Length: 1 } entries)
        {
            Assert.Fail($"Gallery '{runtime.Op}' must compile to exactly one TriggerGraph entry.");
            return;
        }

        Assert.That(entries[0].EventName, Is.EqualTo(expectedEvent));
    }
}
