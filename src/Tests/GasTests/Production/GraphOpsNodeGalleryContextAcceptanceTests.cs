using Arch.Core;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Input.Interaction;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// #1398 S2b per-op gallery acceptance: ActivateContext/DeactivateContext settle real
/// damage through their graph tails while the target carries (or no longer carries) the
/// gallery aim context instance; DispatchCollectionEvent lands the final set through the
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
    public void DispatchCollectionEventVignette_CommitsThroughTheEventKeyedWriter()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("DispatchCollectionEvent");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"DispatchCollectionEvent detail missing phrase: {runtime.Metrics.Detail}");
        }

        Assert.That(runtime.Metrics.Detail, Does.Contain("1"), "the caption quotes the committed member count");
    }
}
