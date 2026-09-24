using Ludots.Adapter.Raylib;
using NUnit.Framework;

namespace Ludots.Adapter.Raylib.Tests;

public sealed class RaylibFixedFrameDeltaTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void MissingOverrideUsesMeasuredFrameTime(string? raw)
    {
        Assert.That(RaylibHostLoop.ParseFixedFrameDeltaSeconds(raw), Is.Null);
    }

    [Test]
    public void ValidOverrideUsesInvariantSeconds()
    {
        Assert.That(
            RaylibHostLoop.ParseFixedFrameDeltaSeconds("0.016666667"),
            Is.EqualTo(1f / 60f).Within(0.0000001f));
    }

    [TestCase("0")]
    [TestCase("-0.1")]
    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("not-a-number")]
    public void InvalidOverrideFailsExplicitly(string raw)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => RaylibHostLoop.ParseFixedFrameDeltaSeconds(raw))!;

        Assert.That(error.Message, Does.Contain("LUDOTS_RAYLIB_FIXED_FRAME_DELTA_SECONDS"));
        Assert.That(error.Message, Does.Contain(raw));
    }
}
