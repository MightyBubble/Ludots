namespace Ludots.Client.WebGpu.Runtime;

internal sealed class FrameTimingWindow
{
    private readonly double _windowSeconds;
    private double _elapsedSeconds;
    private double _worstFrameSeconds;
    private long _allocatedBytes;
    private int _frameCount;

    public FrameTimingWindow(double windowSeconds)
    {
        if (!double.IsFinite(windowSeconds) || windowSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        }

        _windowSeconds = windowSeconds;
    }

    public bool TryObserve(double frameSeconds, long allocatedBytes, out FrameTimingSnapshot snapshot)
    {
        if (!double.IsFinite(frameSeconds) || frameSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        }
        if (allocatedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(allocatedBytes));
        }

        _elapsedSeconds += frameSeconds;
        _worstFrameSeconds = Math.Max(_worstFrameSeconds, frameSeconds);
        _allocatedBytes += allocatedBytes;
        _frameCount++;
        if (_elapsedSeconds < _windowSeconds)
        {
            snapshot = default;
            return false;
        }

        snapshot = new FrameTimingSnapshot(
            _frameCount / _elapsedSeconds,
            (_elapsedSeconds / _frameCount) * 1000.0,
            _worstFrameSeconds * 1000.0,
            _allocatedBytes / _elapsedSeconds);
        _elapsedSeconds = 0.0;
        _worstFrameSeconds = 0.0;
        _allocatedBytes = 0;
        _frameCount = 0;
        return true;
    }
}

internal readonly record struct FrameTimingSnapshot(
    double FramesPerSecond,
    double AverageFrameMilliseconds,
    double WorstFrameMilliseconds,
    double AllocatedBytesPerSecond);
