using Arch.Core;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Knowledge;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Per-op gallery acceptance: ActivateContext/DeactivateContext settle real
/// damage through their graph tails while the target carries (or no longer carries) the
/// gallery aim context instance; WriteCollection lands the final set through the
/// event-keyed writer into the EntityCollectionStore; DiscloseCollection then writes
/// LiveVisible knowledge for that same member.
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
    public void DiscloseCollectionVignette_WritesLiveVisibleKnowledge()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("DiscloseCollection");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"DiscloseCollection detail missing phrase: {runtime.Metrics.Detail}");
        }

        Assert.That(runtime.Metrics.Detail, Does.Contain("1"), "the caption quotes the disclosed member count");
        KnowledgeProjectionStore knowledge = runtime.Context.Knowledge
            ?? throw new InvalidOperationException("DiscloseCollection gallery requires KnowledgeProjectionStore.");
        Assert.That(
            knowledge.TryGet(runtime.Context.Caster, runtime.Context.Target, currentTick: 0, out KnowledgeDisclosureRecord record),
            "DiscloseCollection must upsert LiveVisible for the collection member.");
        Assert.That(record.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
        Assert.That(record.Position, Is.EqualTo(KnowledgePositionAccess.Live));
    }
}
