using Arch.Core;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryBlackboardAcceptanceTests
{
    [Test]
    public void LoadConfigFloat_SettlesConfigPowerOnTarget()
    {
        using var runtime = TickOp("LoadConfigFloat");
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Title, Is.EqualTo("翻开技能册照威力办事"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("册上威力"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("40"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("100"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("60"));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(60f).Within(0.01f));
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void ReadBlackboardFloat_SettlesBoardValueOnTarget()
    {
        using var runtime = TickOp("ReadBlackboardFloat");
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Title, Is.EqualTo("照记事板上的威力出拳"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("板上威力"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("35"));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(65f).Within(0.01f));

        var debugDraw = new Ludots.Core.Presentation.DebugDraw.DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0), "read board must draw the memo board frame");
    }

    [Test]
    public void WriteThenReadFloat_VisibleOnHealthAndCaption()
    {
        using var write = TickOp("WriteBlackboardFloat");
        AssertBannedPlayerCopy(write);
        Assert.That(write.Title, Is.EqualTo("把这一拳的威力记上板"));
        Assert.That(write.Metrics.Detail, Does.Contain("记下"));
        Assert.That(write.Metrics.Detail, Does.Contain("35"));
        Assert.That(write.Context.ActorHealth[1], Is.EqualTo(write.Vignette.Actors[1].Health).Within(0.01f));

        var debugDraw = new Ludots.Core.Presentation.DebugDraw.DebugDrawCommandBuffer();
        write.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0), "write board must draw the dashed hand-to-slot line");

        write.Tick(0.35f);
        debugDraw.Clear();
        write.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Boxes.Count, Is.GreaterThan(0), "write board must blink the power slot highlight on even waves");

        using var read = TickOp("ReadBlackboardFloat");
        AssertBannedPlayerCopy(read);
        Assert.That(read.Metrics.Detail, Does.Contain("板上威力"));
        Assert.That(read.Metrics.Detail, Does.Contain("35"));
        Assert.That(read.Context.ActorHealth[1], Is.EqualTo(65f).Within(0.01f));
    }

    [Test]
    public void LoadConfigEffectId_SettlesTicketDamageEachWave_NoConstFloatStandIn()
    {
        using var runtime = TickOp("LoadConfigEffectId");
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Title, Is.EqualTo("册上贴哪张效果票，就照票开打"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("打击票"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("100"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("82"));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(82f).Within(0.01f));
        AssertProgramWithoutConstFloat(runtime);

        runtime.Tick(0.35f);
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(2));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(64f).Within(0.01f), "each wave must settle exactly the ticket's 18");
        Assert.That(runtime.Metrics.Detail, Does.Contain("82"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("64"));

        var debugDraw = new Ludots.Core.Presentation.DebugDraw.DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0), "config book must draw the ticket row");
    }

    [Test]
    public void LoadConfigInt_TierCaptionAndShieldOverlay()
    {
        using var runtime = TickOp("LoadConfigInt");
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Title, Is.EqualTo("翻开技能册认品阶"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("品阶"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("2"));

        var debugDraw = new Ludots.Core.Presentation.DebugDraw.DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0), "config book must draw the tier scale");
    }

    [Test]
    public void BeginLifecycleTransaction_MaterializesBodyAndOpensLedger()
    {
        using var runtime = TickOp("BeginLifecycleTransaction");
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Title, Is.EqualTo("先开生命台账再动土"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("台账"));
        Assert.That(runtime.Driver, Is.TypeOf<AttrNodeDriver>());
        Assert.That(runtime.Context.LastMaterializedTarget, Is.Not.EqualTo(Entity.Null));
        Assert.That(runtime.Context.SimWorld.IsAlive(runtime.Context.LastMaterializedTarget), Is.True, "new body must survive the committed transaction");
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void InvokeBuiltin_BindsBody_ClearsRack_NoMarkStandIn()
    {
        using var runtime = TickOp("InvokeBuiltin");
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Title, Is.EqualTo("账本里的步骤逐条办"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("新身体"));
        Assert.That(runtime.Driver, Is.TypeOf<AttrNodeDriver>());
        Assert.That(GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "mark"), Is.EqualTo(-1), "mark stand-in actor must be gone");
        Entity body = runtime.Context.LastMaterializedTarget;
        Assert.That(body, Is.Not.EqualTo(Entity.Null));
        Assert.That(runtime.Context.SimWorld.IsAlive(body), Is.True, "new body must survive ClearActiveEffects");
        if (runtime.Context.SimWorld.Has<ActiveEffectContainer>(body))
        {
            Assert.That(runtime.Context.SimWorld.Get<ActiveEffectContainer>(body).Count, Is.EqualTo(0), "effect rack must be swept empty");
        }
    }

    [TestCase("ReadBlackboardInt", "层数")]
    [TestCase("ReadBlackboardEntity", "点名格")]
    [TestCase("WriteBlackboardInt", "层数格")]
    [TestCase("WriteBlackboardEntity", "点名格")]
    [TestCase("LoadConfigInt", "品阶")]
    [TestCase("LoadContextSource", "出手人")]
    [TestCase("LoadContextTargetContext", "额外那格")]
    public void BlackboardOp_CaptionContainsAssertPhrase(string op, string phrase)
    {
        using var runtime = TickOp(op);
        AssertBannedPlayerCopy(runtime);
        Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), op);
        foreach (string expected in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(expected));
        }
    }

    private static void AssertProgramWithoutConstFloat(GraphOpsNodeGalleryRuntime runtime)
    {
        foreach (GraphInstruction instruction in runtime.Context.Compiled.Program)
        {
            Assert.That(
                instruction.Op,
                Is.Not.EqualTo((ushort)GraphNodeOp.ConstFloat),
                "LoadConfigEffectId graph must not carry the ConstFloat stand-in");
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
