using System.Numerics;
using Ludots.Adapter.Raylib;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibWindowRepaintGuardTests
{
    [Test]
    public void ObserveWindowRect_FirstObservation_DoesNotInvalidateDesktop()
    {
        var invalidator = new RecordingInvalidator();
        var guard = new RaylibWindowRepaintGuard(invalidator);

        bool changed = guard.ObserveWindowRect(IntPtr.Zero, new Vector2(100f, 200f), 640, 480);

        Assert.That(changed, Is.False);
        Assert.That(invalidator.InvalidatedRegions, Is.Empty);
    }

    [Test]
    public void ObserveWindowRect_SameRect_DoesNotInvalidateDesktop()
    {
        var invalidator = new RecordingInvalidator();
        var guard = new RaylibWindowRepaintGuard(invalidator);

        guard.ObserveWindowRect(IntPtr.Zero, new Vector2(100f, 200f), 640, 480);
        bool changed = guard.ObserveWindowRect(IntPtr.Zero, new Vector2(100f, 200f), 640, 480);

        Assert.That(changed, Is.False);
        Assert.That(invalidator.InvalidatedRegions, Is.Empty);
    }

    [Test]
    public void ObserveWindowRect_MovedRect_InvalidatesInflatedUnionOfOldAndNewRects()
    {
        var invalidator = new RecordingInvalidator();
        var guard = new RaylibWindowRepaintGuard(invalidator);

        guard.ObserveWindowRect(IntPtr.Zero, new Vector2(100f, 200f), 640, 480);
        bool changed = guard.ObserveWindowRect(IntPtr.Zero, new Vector2(160f, 240f), 640, 480);

        Assert.That(changed, Is.True);
        Assert.That(invalidator.InvalidatedRegions, Has.Count.EqualTo(1));
        Assert.That(
            invalidator.InvalidatedRegions[0],
            Is.EqualTo(new RaylibWindowRepaintGuard.DesktopRect(68, 168, 832, 752)));
    }

    [Test]
    public void ObserveWindowRect_WhenNativeRectIsAvailable_UsesNativeDesktopRect()
    {
        var invalidator = new RecordingInvalidator();
        var handle = new IntPtr(123);
        invalidator.WindowRects[handle] = new RaylibWindowRepaintGuard.DesktopRect(90, 180, 760, 700);
        var guard = new RaylibWindowRepaintGuard(invalidator);

        guard.ObserveWindowRect(handle, new Vector2(100f, 200f), 640, 480);
        invalidator.WindowRects[handle] = new RaylibWindowRepaintGuard.DesktopRect(140, 210, 810, 730);
        bool changed = guard.ObserveWindowRect(handle, new Vector2(100f, 200f), 640, 480);

        Assert.That(changed, Is.True);
        Assert.That(invalidator.InvalidatedRegions, Has.Count.EqualTo(1));
        Assert.That(
            invalidator.InvalidatedRegions[0],
            Is.EqualTo(new RaylibWindowRepaintGuard.DesktopRect(58, 148, 842, 762)));
    }

    [Test]
    public void AfterPresent_FlushesForTwoFramesAfterMove()
    {
        var invalidator = new RecordingInvalidator();
        var guard = new RaylibWindowRepaintGuard(invalidator);

        guard.ObserveWindowRect(IntPtr.Zero, new Vector2(100f, 200f), 640, 480);
        guard.ObserveWindowRect(IntPtr.Zero, new Vector2(160f, 240f), 640, 480);

        guard.AfterPresent();
        guard.AfterPresent();
        guard.AfterPresent();

        Assert.That(invalidator.FlushCount, Is.EqualTo(2));
    }

    private sealed class RecordingInvalidator : RaylibWindowRepaintGuard.IDesktopRegionInvalidator
    {
        public List<RaylibWindowRepaintGuard.DesktopRect> InvalidatedRegions { get; } = new();

        public Dictionary<IntPtr, RaylibWindowRepaintGuard.DesktopRect> WindowRects { get; } = new();

        public int FlushCount { get; private set; }

        public bool TryGetWindowRect(IntPtr nativeWindowHandle, out RaylibWindowRepaintGuard.DesktopRect rect)
        {
            return WindowRects.TryGetValue(nativeWindowHandle, out rect);
        }

        public void Invalidate(in RaylibWindowRepaintGuard.DesktopRect rect)
        {
            InvalidatedRegions.Add(rect);
        }

        public void Flush()
        {
            FlushCount++;
        }
    }
}
