using System;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryBlackboardAcceptanceTests
{
    [Test]
    public void LoadConfigFloat_NotZero_CaptionContainsConfigPower()
    {
        using var runtime = TickOp("LoadConfigFloat");
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Title, Is.EqualTo("从技能配置读出威力"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("配置威力"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("40"));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(60f).Within(0.01f));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void WriteThenReadFloat_VisibleOnHealthAndCaption()
    {
        using var write = TickOp("WriteBlackboardFloat");
        AssertBannedPlayerCopy(write);
        Assert.That(write.Metrics.Detail, Does.Contain("记下"));
        Assert.That(write.Metrics.Detail, Does.Contain("35"));
        Assert.That(write.Context.ActorHealth[1], Is.EqualTo(BlackboardNodeDriver.SeedPower).Within(0.01f));

        using var read = TickOp("ReadBlackboardFloat");
        AssertBannedPlayerCopy(read);
        Assert.That(read.Metrics.Detail, Does.Contain("威力"));
        Assert.That(read.Metrics.Detail, Does.Contain("35"));
        Assert.That(read.Context.ActorHealth[1], Is.EqualTo(65f).Within(0.01f));
    }

    [Test]
    public void BeginLifecycleTransaction_CaptionContainsTransaction()
    {
        using var runtime = TickOp("BeginLifecycleTransaction");
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Title, Is.EqualTo("开一笔生命周期事务"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("事务"));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void InvokeBuiltin_ClearsMark_CaptionIsPlayerChinese()
    {
        using var runtime = TickOp("InvokeBuiltin");
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Metrics.Detail, Does.Contain("内置"));
        int mark = GraphOpsNodeActorBinding.IndexOfId(runtime.Vignette, "mark");
        Assert.That(runtime.Context.ActorHealth[mark], Is.EqualTo(40f).Within(0.01f));
        Assert.That(runtime.Context.ActorHudLit[mark], Is.False);
    }

    [TestCase("ReadBlackboardInt", "层数")]
    [TestCase("ReadBlackboardEntity", "点名")]
    [TestCase("WriteBlackboardInt", "层数写")]
    [TestCase("WriteBlackboardEntity", "记到板上")]
    [TestCase("LoadConfigInt", "阶位")]
    [TestCase("LoadConfigEffectId", "要放的效果")]
    [TestCase("LoadContextSource", "出手的人")]
    [TestCase("LoadContextTargetContext", "关联")]
    public void BlackboardOp_CaptionContainsAssertPhrase(string op, string phrase)
    {
        using var runtime = TickOp(op);
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        foreach (string expected in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(expected));
        }
    }

    private static GraphOpsNodeGalleryRuntime TickOp(string op)
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        return runtime;
    }

    private static void AssertBannedPlayerCopy(GraphOpsNodeGalleryRuntime runtime)
    {
        string detail = runtime.Metrics.Detail;
        Assert.That(detail, Does.Not.Contain("tally"));
        Assert.That(detail, Does.Not.Contain("Validation"));
        Assert.That(detail, Does.Not.Contain("FuncLib"));
        Assert.That(detail, Does.Not.Contain("True"));
        Assert.That(detail, Does.Not.Contain("False"));
        Assert.That(detail, Does.Not.Contain("耗时"));
        Assert.That(detail, Does.Not.Contain("GraphNodeOp"));
        Assert.That(detail, Does.Not.Contain("ReadBlackboard"));
        Assert.That(detail, Does.Not.Contain("WriteBlackboard"));
        Assert.That(detail, Does.Not.Contain("LoadConfig"));
        Assert.That(detail, Does.Not.Contain("InvokeBuiltin"));
        Assert.That(detail, Does.Not.Contain("ClearActiveEffects"));
        Assert.That(detail, Does.Not.Contain("TransferStableId"));
        Assert.That(runtime.Title, Does.Not.Contain("True"));
        Assert.That(runtime.Title, Does.Not.Contain("False"));
        Assert.That(runtime.Title, Does.Not.Contain("FuncLib"));
    }
}
