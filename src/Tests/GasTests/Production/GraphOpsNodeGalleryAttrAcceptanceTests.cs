using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryAttrAcceptanceTests
{
    [Test]
    public void ModifyAttributeAdd_DropsTargetHealth()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ModifyAttributeAdd");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("直接在血条上做加法"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("加算"));
        Assert.That(runtime.Context.ActorHealth[1], Is.LessThan(before));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(before - 25f).Within(0.01f));
    }

    [Test]
    public void ModifyAttributeSet_TriggerGraphWritesTargetHealth()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ModifyAttributeSet");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("TriggerGraph 写入落地"));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(42f).Within(0.01f));
    }

    [Test]
    public void WriteSelfAttribute_RaisesCasterHealth()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("WriteSelfAttribute");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[0];
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("把血直接写成 90"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("写成"));
        Assert.That(before, Is.EqualTo(60f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[0], Is.GreaterThan(before));
        Assert.That(runtime.Context.ActorHealth[0], Is.EqualTo(90f).Within(0.01f));
    }

    [Test]
    public void WriteSelfAttribute_SecondThinkStillHealsFromOpening()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("WriteSelfAttribute");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        runtime.Tick(0.35f);

        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(2));
        Assert.That(runtime.Context.ActorHealth[0], Is.EqualTo(90f).Within(0.01f));
        Assert.That(runtime.Metrics.Detail, Does.Contain("写成"));
    }

    [Test]
    public void ModifyAttributeAdd_SecondThinkStillDropsFromOpening()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ModifyAttributeAdd");
        runtime.EnsureWorld();
        float opening = runtime.Vignette.Actors[1].Health;
        runtime.Tick(0.35f);
        runtime.Tick(0.35f);

        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(2));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(opening - 25f).Within(0.01f));
        Assert.That(runtime.Metrics.Detail, Does.Contain("加算"));
    }

    [Test]
    public void LoadAttribute_CaptionContainsNumber()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadAttribute");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("读出"));
        Assert.That(runtime.Metrics.Detail, Does.Match(@"读出木桩当前生命 \d+"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("80"));
    }

    [Test]
    public void ApplyEffectTemplate_SettlesMarkOntoTarget()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ApplyEffectTemplate");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("给木桩挂上看得见的状态"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("挂上"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("血量"));
        Assert.That(runtime.Driver, Is.TypeOf<AttrNodeDriver>());
        Assert.That(((AttrNodeDriver)runtime.Driver).PendingEffectRequests, Is.EqualTo(0));

        int markTemplateId = EffectTemplateIdRegistry.GetId(AttrNodeDriver.MarkEffectId);
        var world = runtime.Context.SimWorld;
        var target = runtime.Context.Target;
        Assert.That(world.Has<ActiveEffectContainer>(target), Is.True);
        var container = world.Get<ActiveEffectContainer>(target);
        int liveMarks = 0;
        for (int i = 0; i < container.Count; i++)
        {
            var effect = container.GetEntity(i);
            if (world.IsAlive(effect) &&
                world.Has<GameplayEffect>(effect) &&
                !world.Get<GameplayEffect>(effect).CancelRequested &&
                world.Has<EffectTemplateRef>(effect) &&
                world.Get<EffectTemplateRef>(effect).TemplateId == markTemplateId)
            {
                liveMarks++;
            }
        }

        Assert.That(liveMarks, Is.GreaterThan(0), "Settlement must attach a live mark effect to the target.");
    }

    [Test]
    public void RemoveEffectTemplate_CaptionUnloadsMark()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("RemoveEffectTemplate");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("卸"));
    }

    [Test]
    public void CompareEqInt_StackedSettlesStrikeOnTarget()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("CompareEqInt");
        runtime.EnsureWorld();
        int targetIndex = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(targetIndex, Is.GreaterThanOrEqualTo(0));
        float before = runtime.Context.ActorHealth[targetIndex];
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("层数叠满就引爆"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("叠满"));
        Assert.That(before, Is.EqualTo(100f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[targetIndex], Is.EqualTo(82f).Within(0.01f));
    }

    [Test]
    public void CompareEqEntity_NotSelfSettlesStrikeOnTarget()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("CompareEqEntity");
        runtime.EnsureWorld();
        int targetIndex = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(targetIndex, Is.GreaterThanOrEqualTo(0));
        float before = runtime.Context.ActorHealth[targetIndex];
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("先对脸：打的是不是自己"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("不是"));
        Assert.That(before, Is.EqualTo(100f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[targetIndex], Is.EqualTo(82f).Within(0.01f));
    }

    [Test]
    public void SelectEntity_PicksStakeAndSettlesStrike()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SelectEntity");
        runtime.EnsureWorld();
        int targetIndex = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(targetIndex, Is.GreaterThanOrEqualTo(0));
        float before = runtime.Context.ActorHealth[targetIndex];
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("岔路口选人打"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("挑中"));
        Assert.That(before, Is.EqualTo(100f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[targetIndex], Is.EqualTo(82f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[0], Is.EqualTo(100f).Within(0.01f), "Caster must stay untouched.");
    }

    [Test]
    public void CompareLtInt_BelowLineSettlesFullStrike()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("CompareLtInt");
        runtime.EnsureWorld();
        int targetIndex = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(targetIndex, Is.GreaterThanOrEqualTo(0));
        float before = runtime.Context.ActorHealth[targetIndex];
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("血量过线没：过线轻击，没过线全力"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("全力"));
        Assert.That(before, Is.EqualTo(50f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[targetIndex], Is.EqualTo(32f).Within(0.01f));
    }

    [Test]
    public void LoadSelfAttribute_ReadsCasterPresetHealth()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadSelfAttribute");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("看自己还剩多少血"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("还剩"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("62"));
        Assert.That(runtime.Context.ActorHealth[0], Is.EqualTo(62f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(100f).Within(0.01f));
    }

    [Test]
    public void LoadEffectTiming_ReadsSeededRemainingTicks()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadEffectTiming");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("看效果还剩多久"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("还剩"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("55"));
    }

    [Test]
    public void LoadEffectStack_ReadsSeededStackCount()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadEffectStack");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("看效果叠了几层"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("叠了"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("3"));
    }

    [Test]
    public void AttrFamilyOps_RenderPlayerCaptions()
    {
        string[] ops =
        [
            "ConstInt",
            "LoadCaster",
            "LoadExplicitTarget",
            "LoadAttribute",
            "AddInt",
            "CompareLtInt",
            "CompareEqInt",
            "CompareEqEntity",
            "SelectEntity",
            "ApplyEffectTemplate",
            "RemoveEffectTemplate",
            "ModifyAttributeAdd",
            "ModifyAttributeSet",
            "LoadContextTarget",
            "LoadSelfAttribute",
            "WriteSelfAttribute"
        ];

        foreach (string op in ops)
        {
            using var runtime = new GraphOpsNodeGalleryRuntime();
            runtime.BindOp(op);
            runtime.EnsureWorld();
            runtime.Tick(0.35f);
            AssertBannedPlayerCopy(runtime.Metrics.Detail);
            foreach (string phrase in runtime.Vignette.AssertDetailContains)
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), op);
            }
        }
    }

    [Test]
    public void EnsureWorld_HeadlessWithoutStage_DoesNotThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ModifyAttributeAdd");
        Assert.DoesNotThrow(() => runtime.EnsureWorld());
        Assert.That(runtime.Context.Stage, Is.Null);
        Assert.That(runtime.Context.SimActors, Is.Not.Empty);
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
        Assert.That(detail, Does.Not.Match(@"\bConstInt\b"));
        Assert.That(detail, Does.Not.Contain("opcode"));
    }
}
