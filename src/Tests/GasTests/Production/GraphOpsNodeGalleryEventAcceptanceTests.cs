using Arch.Core;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;
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
        Assert.That(runtime.Title, Is.EqualTo("贴到花名册里最近的人"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("吸到"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("近员"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }

        var ctx = runtime.Context;
        var eventDriver = (EventNodeDriver)runtime.Driver;
        int nearest = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(eventDriver.LastFeaturedResult.EntityValue, Is.EqualTo(ctx.SimActors[nearest]),
            "the snap must land on the real nearest roster member, not the far one.");
        WorldCmInt2 nearestPos = ctx.SimWorld.Get<WorldPositionCm>(ctx.SimActors[nearest]).ToWorldCmInt2();
        Assert.That(
            Math.Abs(ctx.TargetPosCm.X - nearestPos.X) + Math.Abs(ctx.TargetPosCm.Y - nearestPos.Y),
            Is.LessThan(5),
            "snapped landing point must sit within 5cm of the nearest member.");
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
        Assert.That(runtime.Title, Is.EqualTo("打出去，对方听得见"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("铃亮"));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(82f).Within(0.01f));

        var ctx = runtime.Context;
        var eventDriver = (EventNodeDriver)runtime.Driver;
        Assert.That(eventDriver.LastBusEventCount, Is.EqualTo(1), "exactly one bus event per beat.");
        Assert.That(eventDriver.LastBusEvent.Target, Is.EqualTo(ctx.Target));
        Assert.That(eventDriver.LastBusEvent.Magnitude, Is.EqualTo(18f).Within(0.01f));
        Assert.That(HasLiveMark(ctx, ctx.Target), Is.True,
            "the listener graph must settle a real Effect.GraphOps.Mark on the stake.");
    }

    [Test]
    public void KnowledgeHasProjection_ShowsVisible()
    {
        using var runtime = BindAndTick("KnowledgeHasProjection");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("观众名下有记录才看得见"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("看得见"));

        var ctx = runtime.Context;
        int stranger = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "caster");
        Assert.That(strayDisclosure(ctx, ctx.Viewer, ctx.SimActors[stranger]), Is.False,
            "no disclosure is written for the stranger; the second pass must read 看不见.");
        Assert.That(ctx.ActorHudLit[stranger], Is.False, "the stranger's health bar must stay hidden.");
    }

    private static bool strayDisclosure(GraphOpsNodeDriverContext ctx, Entity viewer, Entity stranger)
    {
        return ctx.Knowledge!.TryGet(viewer, stranger, currentTick: 0, out _);
    }

    [Test]
    public void LoadViewer_ReadsTheAudience()
    {
        using var runtime = BindAndTick("LoadViewer");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("取出镜头背后的人"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("观众"));
        var eventDriver = (EventNodeDriver)runtime.Driver;
        Assert.That(eventDriver.LastFeaturedResult.EntityValue, Is.EqualTo(runtime.Context.Viewer));
    }

    [Test]
    public void SnapToNearestGraphEdge_SnapsOntoTheRoad()
    {
        using var runtime = BindAndTick("SnapToNearestGraphEdge");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("离路太远就拽回路边"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("路边"));
    }

    [Test]
    public void LoadEventPayloadFloat_ReadsTheRealBusEvent()
    {
        using var runtime = BindAndTick("LoadEventPayloadFloat");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("小数"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("2.5"));
        var eventDriver = (EventNodeDriver)runtime.Driver;
        Assert.That(eventDriver.LastBusEventCount, Is.EqualTo(1), "the producer must put exactly one event on the bus.");
        Assert.That(eventDriver.LastBusEvent.Magnitude, Is.EqualTo(2.5f).Within(0.001f));
        Assert.That(eventDriver.LastFeaturedResult.FloatValue, Is.EqualTo(eventDriver.LastBusEvent.Magnitude).Within(0.001f),
            "featured result must equal the value read back from the bus.");
    }

    [Test]
    public void LoadEventPayloadInt_ReadsTheRealRegistryNumber()
    {
        using var runtime = BindAndTick("LoadEventPayloadInt");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("编号"));
        int tagId = TagRegistry.GetId("Event.DamageDealt");
        Assert.That(tagId, Is.GreaterThan(0));
        Assert.That(runtime.Metrics.Detail, Does.Contain(tagId.ToString()));
        var eventDriver = (EventNodeDriver)runtime.Driver;
        Assert.That(eventDriver.LastBusEventCount, Is.EqualTo(1), "the shared producer must put exactly one event on the bus.");
        Assert.That(eventDriver.LastBusEvent.TagId, Is.EqualTo(tagId));
        Assert.That(eventDriver.LastFeaturedResult.IntValue, Is.EqualTo(eventDriver.LastBusEvent.TagId),
            "featured result must equal the registry number read back from the bus.");
    }

    [TestCase("LoadTargetPosX", "东西", 360)]
    [TestCase("LoadTargetPosY", "南北", 200)]
    public void LoadTargetPos_ReadsRulerReading(string op, string axis, int reading)
    {
        using var runtime = BindAndTick(op);
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain(axis));
        Assert.That(runtime.Metrics.Detail, Does.Contain(reading.ToString()));
    }

    [Test]
    public void FanOutDispatchEffect_StrikesTheWholeCircleTo82()
    {
        using var runtime = BindAndTick("FanOutDispatchEffect");
        var ctx = runtime.Context;
        Assert.That(ctx.EffectRequests!.Count, Is.EqualTo(0), "settlement must consume the effect request queue");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("派给"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("3"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("18"));
        for (int i = 0; i < runtime.Vignette.Actors.Length; i++)
        {
            bool caster = string.Equals(runtime.Vignette.Actors[i].Role, "caster", StringComparison.Ordinal);
            float expected = caster ? 100f : 82f;
            Assert.That(ctx.ActorHealth[i], Is.EqualTo(expected).Within(0.01f), runtime.Vignette.Actors[i].Id);
        }
    }

    [Test]
    public void FanOutDispatchEffectDynamic_HangsBellsWithoutDamage()
    {
        using var runtime = BindAndTick("FanOutDispatchEffectDynamic");
        var ctx = runtime.Context;
        Assert.That(ctx.EffectRequests!.Count, Is.EqualTo(0), "settlement must consume the effect request queue");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("挂上铃"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("3"));
        for (int i = 0; i < runtime.Vignette.Actors.Length; i++)
        {
            Assert.That(ctx.ActorHealth[i], Is.EqualTo(100f).Within(0.01f),
                $"{runtime.Vignette.Actors[i].Id} must not lose health to the Mark card.");
            if (string.Equals(runtime.Vignette.Actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.That(HasLiveMark(ctx, ctx.SimActors[i]), Is.True,
                $"{runtime.Vignette.Actors[i].Id} must really carry a settled Effect.GraphOps.Mark.");
        }
    }

    [Test]
    public void ControlDomainResolve_WalksTheOwnsChainToTheCaptain()
    {
        using var runtime = BindAndTick("ControlDomainResolve");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("说了算"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("队长"));
        var ctx = runtime.Context;
        Assert.That(ctx.Ownership!.TryResolveRootOwner(ctx.Target, out Entity root), Is.True);
        Assert.That(root, Is.EqualTo(ctx.Caster), "the vignette Owns link must reach the captain without being flattened.");
    }

    [Test]
    public void ControlDomainControls_AnswersBothDirections()
    {
        using var runtime = BindAndTick("ControlDomainControls");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("管得着"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("管不动"));
    }

    [Test]
    public void IsPointInCircle_VerdictsInsideAndOutside()
    {
        using var runtime = BindAndTick("IsPointInCircle");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("圈里圈外，当场见分晓"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("在圈里"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("在圈外"));
    }

    private static bool HasLiveMark(GraphOpsNodeDriverContext ctx, Entity target)
    {
        int markId = EffectTemplateIdRegistry.GetId("Effect.GraphOps.Mark");
        if (markId <= 0 || target == Entity.Null || !ctx.SimWorld.Has<ActiveEffectContainer>(target))
        {
            return false;
        }

        ActiveEffectContainer container = ctx.SimWorld.Get<ActiveEffectContainer>(target);
        for (int i = 0; i < container.Count; i++)
        {
            Entity effect = container.GetEntity(i);
            if (ctx.SimWorld.IsAlive(effect) &&
                ctx.SimWorld.Has<GameplayEffect>(effect) &&
                !ctx.SimWorld.Get<GameplayEffect>(effect).CancelRequested &&
                ctx.SimWorld.Has<EffectTemplateRef>(effect) &&
                ctx.SimWorld.Get<EffectTemplateRef>(effect).TemplateId == markId)
            {
                return true;
            }
        }

        return false;
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
