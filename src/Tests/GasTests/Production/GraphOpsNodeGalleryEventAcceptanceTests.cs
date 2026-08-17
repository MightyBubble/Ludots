using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryEventAcceptanceTests
{
    private static readonly string[] EventFamilyOpsWithoutDedicatedMethod =
    [
        "FanOutDispatchEffect",
        "FanOutDispatchEffectDynamic",
        "LoadTargetPosX",
        "LoadTargetPosY",
        "IsPointInCircle",
        "LoadEventPayloadInt",
        "LoadEventPayloadFloat",
        "ControlDomainResolve",
        "ControlDomainControls"
    ];

    [TestCaseSource(nameof(EventFamilyOpsWithoutDedicatedMethod))]
    public void EventFamilyOp_RendersPlayerCaption(string op)
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick(op);
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), op);
        }
    }

    [TestCase("FanOutDispatchEffect")]
    [TestCase("FanOutDispatchEffectDynamic")]
    public void FanOutDispatch_SettlesQueuedEffectRequests(string op)
    {
        using var runtime = BindAndTick(op);
        Assert.That(runtime.Context.EffectRequests!.Count, Is.EqualTo(0), op);
    }

    [Test]
    public void SnapToNearestInCollection_SucceedsWithPlayerCaption()
    {
        using var runtime = BindAndTick("SnapToNearestInCollection");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("吸到花名册里最近的人"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("吸到"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void ClampTargetToRange_PullsLandingPointInRange()
    {
        using var runtime = BindAndTick("ClampTargetToRange");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("落点拉回够得着的地方"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("拉回"));
    }

    [Test]
    public void SendEvent_BroadcastsPlayerReadableHit()
    {
        using var runtime = BindAndTick("SendEvent");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("打出一记并广播出去"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("广播"));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(82f).Within(0.01f));
    }

    [Test]
    public void KnowledgeHasProjection_ShowsVisible()
    {
        using var runtime = BindAndTick("KnowledgeHasProjection");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("观众知不知道那个人"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("看得见"));
    }

    [Test]
    public void LoadViewer_ReadsTheAudience()
    {
        using var runtime = BindAndTick("LoadViewer");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("从观众自己看"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("自己这侧"));
    }

    [Test]
    public void SnapToNearestGraphEdge_SnapsOntoTheRoad()
    {
        using var runtime = BindAndTick("SnapToNearestGraphEdge");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("离路太远就拽回路边"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("路边"));
    }

    private static GraphOpsNodeGalleryRuntime BindAndTick(string op)
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(1));
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
