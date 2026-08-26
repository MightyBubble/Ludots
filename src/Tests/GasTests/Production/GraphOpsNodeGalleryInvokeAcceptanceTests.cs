using System;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// TriggerGraph-only #1116/#1115 per-op galleries: StoreArg* staging reaches the
/// InvokeGraph callee through the named argument key (int return, float echoed into a
/// map variable, entity physically moved by the callee), the InvokeGraph entry-label
/// call selects the authored entry, and DispatchMapEvent fires a schema-checked
/// MapHeartbeat that a map-scoped listener receives with the staged payload.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryInvokeAcceptanceTests
{
    [Test]
    public void InvokeGraph_ExplicitEntryLabel_CalleeReturnsBoostConstant()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("InvokeGraph");
        runtime.EnsureWorld();

        Assert.That(runtime.Vignette.GraphKind, Is.EqualTo("TriggerGraph"));
        runtime.Tick(0.35f);

        var driver = (InvokeGraphNodeDriver)runtime.Driver;
        Assert.That(driver.LastResult.ReturnInt, Is.EqualTo(9),
            "the callee's 'boost' entry must be selected by the authored entry label");
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(1));
        Assert.That(runtime.Metrics.Detail, Does.Contain("boost"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("9"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void StoreArgInt_StagedValueRoundTripsThroughCallee()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("StoreArgInt");
        runtime.EnsureWorld();

        runtime.Tick(0.35f);

        var driver = (InvokeGraphNodeDriver)runtime.Driver;
        Assert.That(driver.LastResult.ReturnInt, Is.EqualTo(6),
            "the callee must read the staged GraphOps.Stage argument and return it");
        Assert.That(runtime.Metrics.Detail, Does.Contain("6"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void StoreArgFloat_CalleeEchoesIntoMapVariable()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("StoreArgFloat");
        runtime.EnsureWorld();

        runtime.Tick(0.35f);

        Assert.That(runtime.Metrics.Detail, Does.Contain("6.5"),
            "the callee must echo the staged float into gallery.echo");
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void StoreArgEntity_CalleeMovesTheStagedEntity()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("StoreArgEntity");
        runtime.EnsureWorld();

        runtime.Tick(0.35f);

        Assert.That(runtime.Metrics.Detail, Does.Contain("300"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("-200"),
            "the callee's SetWorldPosition must move the staged entity to the authored spot");
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void DispatchMapEvent_SchemaHeartbeat_ReachesMapListenerWithPayload()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("DispatchMapEvent");
        runtime.EnsureWorld();

        runtime.Tick(0.35f);

        Assert.That(runtime.Metrics.Detail, Does.Contain("1"),
            "the map-scoped heartbeat probe must fire exactly once");
        Assert.That(runtime.Metrics.Detail, Does.Contain("5"),
            "the staged heartbeatIndex payload must arrive at the listener");
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }
}
