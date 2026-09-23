using Ludots.Client.WebGpu.Runtime;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class FrameTimingWindowTests
{
    [Test]
    public void ReportsActualCadenceAndWorstFrameForCompletedWindow()
    {
        var timing = new FrameTimingWindow(1.0);
        FrameTimingSnapshot snapshot = default;

        Assert.Multiple(() =>
        {
            Assert.That(timing.TryObserve(0.10, 100, out snapshot), Is.False);
            Assert.That(timing.TryObserve(0.15, 200, out snapshot), Is.False);
            Assert.That(timing.TryObserve(0.25, 300, out snapshot), Is.False);
            Assert.That(timing.TryObserve(0.20, 400, out snapshot), Is.False);
            Assert.That(timing.TryObserve(0.30, 500, out snapshot), Is.True);
        });

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.FramesPerSecond, Is.EqualTo(5.0).Within(0.001));
            Assert.That(snapshot.AverageFrameMilliseconds, Is.EqualTo(200.0).Within(0.001));
            Assert.That(snapshot.WorstFrameMilliseconds, Is.EqualTo(300.0).Within(0.001));
            Assert.That(snapshot.AllocatedBytesPerSecond, Is.EqualTo(1500.0).Within(0.001));
        });
    }

    [TestCase(0.0)]
    [TestCase(-0.01)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void RejectsInvalidFrameIntervals(double frameSeconds)
    {
        var timing = new FrameTimingWindow(1.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => timing.TryObserve(frameSeconds, 0, out _));
    }

    [Test]
    public void RejectsNegativeAllocationDelta()
    {
        var timing = new FrameTimingWindow(1.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => timing.TryObserve(0.1, -1, out _));
    }
}
