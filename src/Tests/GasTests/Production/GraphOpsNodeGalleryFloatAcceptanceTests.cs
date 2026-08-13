using System.Globalization;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
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
    public void RandomFloat01_ResultStaysInUnitIntervalAcrossWaves()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("RandomFloat01");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        float first = ParseCaptionResult(runtime.Metrics.Detail, "这一刀随机抖动是 ", "。");
        Assert.That(first, Is.InRange(0f, 1f));
        Assert.That(runtime.Metrics.Detail, Does.Contain("随机"));
        AssertBannedPlayerCopy(runtime.Metrics.Detail);

        runtime.Tick(0.35f);
        float second = ParseCaptionResult(runtime.Metrics.Detail, "这一刀随机抖动是 ", "。");
        Assert.That(second, Is.InRange(0f, 1f));
        Assert.That(runtime.Metrics.Detail, Does.Contain("随机"));
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
    }

    [Test]
    public void CompareGtFloat_CriticalCheckHolds()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("CompareGtFloat");
        Assert.That(runtime.Metrics.Detail, Does.Contain("暴击"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("成立"));
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
    }

    [Test]
    public void ClampFloat_SubtractsClampedForty()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ClampFloat");
        runtime.EnsureWorld();
        float before = runtime.Context.ActorHealth[1];
        runtime.Tick(0.35f);
        Assert.That(runtime.Metrics.Detail, Does.Contain("钳"));
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
        Assert.That(before - runtime.Context.ActorHealth[1], Is.EqualTo(40f).Within(0.01f));
    }

    private static GraphOpsNodeGalleryRuntime Play(string op)
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        return runtime;
    }

    private static float ParseCaptionResult(string detail, string prefix, string suffix)
    {
        Assert.That(detail, Does.StartWith(prefix));
        Assert.That(detail, Does.EndWith(suffix));
        string token = detail[prefix.Length..^suffix.Length];
        Assert.That(
            float.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out float value),
            Is.True,
            $"Caption result '{token}' is not a float.");
        return value;
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
