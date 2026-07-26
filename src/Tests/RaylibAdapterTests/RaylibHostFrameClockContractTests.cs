using Ludots.Adapter.Raylib;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibHostFrameClockContractTests
{
    // Feature: automated capture advances simulation by the authored host-frame clock
    // Given a scene targets 60 FPS and a slow render frame reports a large wall-clock delta
    // When deterministic capture timing is enabled
    // Then simulation receives exactly 1/60 second instead of catching up the slow wall-clock frame
    [Test]
    public void Feature_DeterministicCaptureDelta_UsesConfiguredTargetFps()
    {
        float resolved = RaylibHostLoop.ResolveFrameDeltaSeconds(
            measuredDeltaSeconds: 2.5f,
            targetFps: 60,
            deterministicCapture: true);

        Assert.That(resolved, Is.EqualTo(1f / 60f));
        Assert.That(
            RaylibHostLoop.ResolveFrameDeltaSeconds(0.125f, targetFps: 60, deterministicCapture: false),
            Is.EqualTo(0.125f));
        Assert.That(
            () => RaylibHostLoop.ResolveFrameDeltaSeconds(0.125f, targetFps: 0, deterministicCapture: true),
            Throws.InvalidOperationException.With.Message.Contains("positive targetFps"));
    }

    // Feature: screenshot labels match the completed host frame clock
    // Given Raylib published HostFrameIndex before engine.Tick
    // When the host finishes presenting that frame
    // Then a screenshot labeled f0240 is eligible only after HostFrameIndex 240 completed
    [Test]
    public void Feature_ScreenshotGate_MatchesCompletedHostFrameIndex_NotPostIncrement()
    {
        Assert.That(RaylibHostLoop.MatchesCompletedHostFrame(completedHostFrameIndex: 239, gateFrame: 240), Is.False);
        Assert.That(RaylibHostLoop.MatchesCompletedHostFrame(completedHostFrameIndex: 240, gateFrame: 240), Is.True);
        Assert.That(RaylibHostLoop.MatchesCompletedHostFrame(completedHostFrameIndex: 241, gateFrame: 240), Is.True);
        Assert.That(RaylibHostLoop.MatchesCompletedHostFrame(completedHostFrameIndex: 749, gateFrame: 750), Is.False);
        Assert.That(RaylibHostLoop.MatchesCompletedHostFrame(completedHostFrameIndex: 750, gateFrame: 750), Is.True);
    }
}
