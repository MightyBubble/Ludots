using Arch.Core;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Input.Interaction;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Per-op gallery acceptance: ActivateContext/DeactivateContext settle real
/// damage through their graph tails while the target carries (or no longer carries) the
/// gallery aim context instance; WriteCollection lands the final set through the
/// event-keyed writer into the EntityCollectionStore.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryContextAcceptanceTests
{
    [Test]
    public void ActivateContextVignette_MountsTheGalleryAimInstance()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ActivateContext");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"ActivateContext detail missing phrase: {runtime.Metrics.Detail}");
        }

        Assert.That(runtime.Context.ActorHealth[1], Is.LessThan(before), "the graph tail settles real damage");
        Entity target = runtime.Context.Target;
        Assert.That(
            runtime.Context.SimWorld.TryGet<InteractionContextInstances>(target, out InteractionContextInstances instances) &&
            instances.Count == 1,
            "the featured op mounts the gallery aim context instance on the target");
    }

    [Test]
    public void DeactivateContextVignette_RoundsTripTheInstanceSet()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("DeactivateContext");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"DeactivateContext detail missing phrase: {runtime.Metrics.Detail}");
        }

        Assert.That(runtime.Context.ActorHealth[1], Is.LessThan(before), "the graph tail settles real damage");
        Entity target = runtime.Context.Target;
        bool carries = runtime.Context.SimWorld.TryGet<InteractionContextInstances>(target, out InteractionContextInstances instances);
        Assert.That(carries && instances.Count == 0, "activate-then-deactivate leaves an empty instance set");
    }

    [Test]
    public void WriteCollectionVignette_CommitsThroughTheStoreWrite()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("WriteCollection");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"WriteCollection detail missing phrase: {runtime.Metrics.Detail}");
        }

        Assert.That(runtime.Metrics.Detail, Does.Contain("1"), "the caption quotes the committed member count");
    }

    [Test]
    public void IntToFloatVignette_ConvertsIntegerScaleAndSettlesDamage()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("IntToFloat");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), runtime.Metrics.Detail);
        }

        Assert.That(runtime.Context.ActorHealth[1], Is.LessThan(before), "the converted float settles damage");
    }

    [Test]
    public void FloatToIntVignette_RoundsHalfAwayFromZeroAndSettlesDamage()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("FloatToInt");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);

        Assert.That(runtime.Metrics.Detail, Does.Contain("-9"), "the caption quotes the rounded integer");

        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(before - 9f), "the rounded integer settles as 9 damage");
    }

    [Test]
    public void SqrtFloatVignette_ComputesTheRootAndSettlesDamage()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SqrtFloat");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), runtime.Metrics.Detail);
        }

        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(before - 9f), "the root settles as 9 damage");
    }

    [Test]
    public void LoadEntityPosXVignette_QuotesTheTargetCentimeterX()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadEntityPosX");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        Assert.That(runtime.Metrics.Detail, Does.Contain("400"), "the caption quotes the target X in centimeters");
    }

    [Test]
    public void LoadEntityPosYVignette_QuotesTheTargetCentimeterY()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadEntityPosY");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        Assert.That(runtime.Metrics.Detail, Does.Contain("300"), "the caption quotes the target Y in centimeters");
    }

    [Test]
    public void SubmitAssignedOrderVignette_EnqueuesAMoveOrderIntoThePipeline()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SubmitAssignedOrder");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        Assert.That(runtime.Metrics.Detail, Does.Contain("650"), "the caption quotes the submitted X");
        Assert.That(runtime.Metrics.Detail, Does.Contain("520"), "the caption quotes the submitted Y");
    }

    [Test]
    public void CompleteActiveOrderVignette_SettlesTheActiveOrderAndFreesTheBuffer()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("CompleteActiveOrder");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), runtime.Metrics.Detail);
        }

        Assert.That(runtime.Context.SimWorld.Get<Ludots.Core.Gameplay.GAS.Components.OrderBuffer>(runtime.Context.Caster).ActiveIndex,
            Is.EqualTo(-1), "the active order slot is released after completion");
    }

    [Test]
    public void QueryFilterControllableVignette_KeepsOnlyTheViewerControllableUnits()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("QueryFilterControllable");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), runtime.Metrics.Detail);
        }

        Assert.That(runtime.Context.HitTargetCount, Is.GreaterThan(0), "at least one controllable unit stays circled");
    }
}
