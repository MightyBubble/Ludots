using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
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
        Assert.That(runtime.Title, Is.EqualTo("直接扣血"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("扣"));
        Assert.That(runtime.Context.ActorHealth[1], Is.LessThan(before));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(before - 25f).Within(0.01f));
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
        Assert.That(runtime.Title, Is.EqualTo("给自己回一口"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("回"));
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
        Assert.That(runtime.Metrics.Detail, Does.Contain("回"));
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
        Assert.That(runtime.Metrics.Detail, Does.Contain("扣"));
    }

    [Test]
    public void LoadAttribute_CaptionContainsNumber()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadAttribute");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("还有"));
        Assert.That(runtime.Metrics.Detail, Does.Match(@"还有 \d+"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("80"));
    }

    [Test]
    public void ApplyEffectTemplate_EnqueuesMark()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ApplyEffectTemplate");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("挂上"));
        Assert.That(runtime.Driver, Is.TypeOf<AttrNodeDriver>());
        Assert.That(((AttrNodeDriver)runtime.Driver).PendingEffectRequests, Is.GreaterThan(0));
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
