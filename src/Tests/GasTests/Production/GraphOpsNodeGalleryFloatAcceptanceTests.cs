using System.Globalization;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryFloatAcceptanceTests
{
    private static readonly string[] FloatFamilyOps =
    [
        "ConstBool",
        "MulFloat",
        "SubFloat",
        "DivFloat",
        "MinFloat",
        "MaxFloat",
        "ClampFloat",
        "AbsFloat",
        "NegFloat",
        "RandomFloat01",
        "CompareGtFloat"
    ];

    [TestCaseSource(nameof(FloatFamilyOps))]
    public void FloatFamilyOp_RendersPlayerCaption(string op)
    {
        using GraphOpsNodeGalleryRuntime runtime = Play(op);
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void RandomFloat01_RollsAndSettlesScaledDamageAcrossWaves()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("RandomFloat01");
        runtime.EnsureWorld();
        int targetIndex = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(targetIndex, Is.GreaterThanOrEqualTo(0));

        runtime.Tick(0.35f);
        float first = ParseRoll(runtime.Metrics.Detail);
        Assert.That(first, Is.InRange(0f, 1f));
        Assert.That(runtime.Metrics.Detail, Does.Contain("掷出"));
        AssertRollDamage(runtime, targetIndex, first);
        AssertBannedPlayerCopy(runtime.Metrics.Detail);

        runtime.Tick(0.35f);
        float second = ParseRoll(runtime.Metrics.Detail);
        Assert.That(second, Is.InRange(0f, 1f));
        Assert.That(runtime.Metrics.Detail, Does.Contain("掷出"));
        AssertRollDamage(runtime, targetIndex, second);
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
    }

    private static void AssertRollDamage(GraphOpsNodeGalleryRuntime runtime, int targetIndex, float captionRoll)
    {
        float health = runtime.Context.ActorHealth[targetIndex];
        float actualRoll = (100f - health) / 30f;
        Assert.That(actualRoll, Is.InRange(0f, 1f));
        Assert.That(captionRoll, Is.EqualTo(actualRoll).Within(0.05f));
    }

    [Test]
    public void LoadPointerScreenX_QuotesPinnedLivePointerAxis()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("LoadPointerScreenX");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("42"),
            "linear gallery pins live pointer at (42,42); caption must quote screen X");
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void LoadPointerScreenY_QuotesPinnedLivePointerAxis()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("LoadPointerScreenY");
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Metrics.Detail, Does.Contain("42"),
            "linear gallery pins live pointer at (42,42); caption must quote screen Y");
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    [Test]
    public void CompareGtFloat_ThinStakeClearedWhileThickStakeWithstands()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("CompareGtFloat");
        runtime.EnsureWorld();
        int thin = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        int thick = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "context");
        Assert.That(thin, Is.GreaterThanOrEqualTo(0));
        Assert.That(thick, Is.GreaterThanOrEqualTo(0));

        runtime.Tick(0.35f);
        Assert.That(runtime.Metrics.Detail, Does.Contain("判定"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("成立"));
        Assert.That(runtime.Context.ActorHealth[thin], Is.EqualTo(0f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[thick], Is.EqualTo(100f).Within(0.01f));
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
    }

    [TestCase("MaxFloat", 28f)]
    [TestCase("MinFloat", 18f)]
    [TestCase("AddFloat", 42f)]
    [TestCase("ClampFloat", 40f)]
    [TestCase("ConstFloat", 42f)]
    [TestCase("NegFloat", 8f)]
    [TestCase("AbsFloat", 8f)]
    [TestCase("SubFloat", 38f)]
    [TestCase("DivFloat", 20f)]
    [TestCase("ConstBool", 8f)]
    public void ArithmeticOp_SettlesRealDamageThroughGraphTail(string op, float expectedDrop)
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        int targetIndex = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "target");
        Assert.That(targetIndex, Is.GreaterThanOrEqualTo(0));
        float before = runtime.Context.ActorHealth[targetIndex];
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(before, Is.EqualTo(100f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[targetIndex], Is.EqualTo(before - expectedDrop).Within(0.01f));

        if (string.Equals(op, "DivFloat", StringComparison.Ordinal))
        {
            int contextIndex = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "context");
            Assert.That(contextIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(runtime.Context.ActorHealth[contextIndex], Is.EqualTo(100f - expectedDrop).Within(0.01f));
        }
    }

    [Test]
    public void MulFloat_SettlesRealWorldDamageThroughGraphTail()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("MulFloat");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);

        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(runtime.Title, Is.EqualTo("伤害拉长一半"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("拉长"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("30"));
        Assert.That(before, Is.EqualTo(100f).Within(0.01f));
        Assert.That(runtime.Context.ActorHealth[1], Is.EqualTo(70f).Within(0.01f));

        var debugDraw = new Ludots.Platform.Abstractions.DebugDrawCommandBuffer();
        runtime.DrawOverlay(debugDraw);
        Assert.That(debugDraw.Lines.Count, Is.GreaterThan(0), "settle bench must draw the stretched-damage track");
    }

    [Test]
    public void ClampFloat_SubtractsClampedForty()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ClampFloat");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);
        Assert.That(runtime.Metrics.Detail, Does.Contain("撞墙"));
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(before - runtime.Context.ActorHealth[1], Is.EqualTo(40f).Within(0.01f));
    }

    private static float ParseRoll(string detail)
    {
        const string prefix = "这一拍掷出 ";
        Assert.That(detail, Does.StartWith(prefix));
        int semicolon = detail.IndexOf('；', StringComparison.Ordinal);
        Assert.That(semicolon, Is.GreaterThan(prefix.Length), "Roll caption must contain the result token before '；'.");
        string token = detail[prefix.Length..semicolon];
        Assert.That(
            float.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out float value),
            Is.True,
            $"Roll token '{token}' is not a float.");
        return value;
    }

    private static GraphOpsNodeGalleryRuntime Play(string op)
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
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
