using System;

namespace Ludots.Adapter.LiteNetLib;

/// <summary>
/// Bounded outbound bandwidth + tick duration metrics. Steady-state Record paths allocate nothing.
/// </summary>
public sealed class NetworkAdapterMetricsSampler
{
    private readonly OutboundClientWindow[] _outbound;
    private readonly long[] _tickSamplesMicroseconds;
    private readonly long[] _tickScratch;
    private readonly long[] _outboundScratch;
    private readonly int _outboundSampleCapacity;
    private int _tickCount;
    private int _tickWrite;
    private long _nowMs;
    private long _lastWallTimestamp;

    public NetworkAdapterMetricsSampler(int clientCapacity, int tickSampleCapacity, int outboundSampleCapacity = 64)
    {
        if (clientCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(clientCapacity));
        if (tickSampleCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(tickSampleCapacity));
        if (outboundSampleCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(outboundSampleCapacity));

        _outbound = new OutboundClientWindow[clientCapacity];
        _outboundSampleCapacity = outboundSampleCapacity;
        for (int i = 0; i < clientCapacity; i++)
        {
            _outbound[i] = new OutboundClientWindow(outboundSampleCapacity);
        }

        _tickSamplesMicroseconds = new long[tickSampleCapacity];
        _tickScratch = new long[tickSampleCapacity];
        _outboundScratch = new long[outboundSampleCapacity];
        _lastWallTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public int ClientCapacity => _outbound.Length;

    public int TickSampleCount => _tickCount;

    public void AdvanceWallClock()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastWallTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _lastWallTimestamp = now;
        int delta = elapsedMs < 1.0 ? 1 : (int)elapsedMs;
        AdvanceMs(delta);
    }

    public void AdvanceMs(int deltaMs)
    {
        if (deltaMs < 0) throw new ArgumentOutOfRangeException(nameof(deltaMs));
        _nowMs = checked(_nowMs + deltaMs);
        for (int i = 0; i < _outbound.Length; i++)
        {
            _outbound[i].FlushWindows(_nowMs, _outboundSampleCapacity);
        }
    }

    public void RecordOutboundBytes(int clientIndex, int byteCount)
    {
        if ((uint)clientIndex >= (uint)_outbound.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(clientIndex));
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        _outbound[clientIndex].AddBytes(byteCount, _nowMs, _outboundSampleCapacity);
    }

    public void RecordTickMicroseconds(ulong microseconds)
    {
        if (microseconds > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(microseconds));
        }

        _tickSamplesMicroseconds[_tickWrite] = (long)microseconds;
        _tickWrite = (_tickWrite + 1) % _tickSamplesMicroseconds.Length;
        if (_tickCount < _tickSamplesMicroseconds.Length)
        {
            _tickCount++;
        }
    }

    public long GetCurrentOutboundBytesPerSecond(int clientIndex)
    {
        if ((uint)clientIndex >= (uint)_outbound.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(clientIndex));
        }

        return _outbound[clientIndex].CurrentBytesPerSecond(_nowMs);
    }

    public void CopyCurrentOutboundBytesPerSecond(Span<long> destination)
    {
        if (destination.Length < _outbound.Length)
        {
            throw new ArgumentException("Destination is smaller than client capacity.", nameof(destination));
        }

        for (int i = 0; i < _outbound.Length; i++)
        {
            destination[i] = _outbound[i].CurrentBytesPerSecond(_nowMs);
        }
    }

    public long GetOutboundBytesPerSecondP95(int clientIndex)
    {
        if ((uint)clientIndex >= (uint)_outbound.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(clientIndex));
        }

        return _outbound[clientIndex].GetPercentile(95, _outboundScratch);
    }

    public long GetTickP95Microseconds() => GetTickPercentile(95);

    public long GetTickP99Microseconds() => GetTickPercentile(99);

    private long GetTickPercentile(int percentile)
    {
        if (_tickCount == 0)
        {
            return 0;
        }

        _tickSamplesMicroseconds.AsSpan(0, _tickCount).CopyTo(_tickScratch.AsSpan(0, _tickCount));
        return SelectPercentile(_tickScratch.AsSpan(0, _tickCount), percentile);
    }

    internal static long SelectPercentile(Span<long> samples, int percentile)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        if ((uint)(percentile - 1) >= 100u)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        samples.Sort();
        int index = (int)((percentile / 100.0) * (samples.Length - 1));
        return samples[index];
    }

    private struct OutboundClientWindow
    {
        private readonly long[] _samples;
        private long _windowStartMs;
        private long _bytesInWindow;
        private int _sampleCount;
        private int _sampleWrite;
        private bool _started;

        public OutboundClientWindow(int sampleCapacity)
        {
            _samples = new long[sampleCapacity];
            _windowStartMs = 0;
            _bytesInWindow = 0;
            _sampleCount = 0;
            _sampleWrite = 0;
            _started = false;
        }

        public void AddBytes(int byteCount, long nowMs, int sampleCapacity)
        {
            EnsureWindow(nowMs, sampleCapacity);
            _bytesInWindow = checked(_bytesInWindow + byteCount);
        }

        public void FlushWindows(long nowMs, int sampleCapacity) => EnsureWindow(nowMs, sampleCapacity);

        public long CurrentBytesPerSecond(long nowMs)
        {
            if (!_started)
            {
                return 0;
            }

            long elapsed = nowMs - _windowStartMs;
            if (elapsed <= 0)
            {
                return _bytesInWindow;
            }

            if (elapsed >= 1000)
            {
                return _bytesInWindow;
            }

            return _bytesInWindow * 1000 / elapsed;
        }

        public long GetPercentile(int percentile, long[] scratch)
        {
            if (_sampleCount == 0)
            {
                return 0;
            }

            _samples.AsSpan(0, _sampleCount).CopyTo(scratch.AsSpan(0, _sampleCount));
            return SelectPercentile(scratch.AsSpan(0, _sampleCount), percentile);
        }

        private void EnsureWindow(long nowMs, int sampleCapacity)
        {
            if (!_started)
            {
                _started = true;
                _windowStartMs = nowMs;
                return;
            }

            while (nowMs - _windowStartMs >= 1000)
            {
                PushSample(_bytesInWindow, sampleCapacity);
                _windowStartMs += 1000;
                _bytesInWindow = 0;
            }
        }

        private void PushSample(long bytesPerSecond, int sampleCapacity)
        {
            _ = sampleCapacity;
            _samples[_sampleWrite] = bytesPerSecond;
            _sampleWrite = (_sampleWrite + 1) % _samples.Length;
            if (_sampleCount < _samples.Length)
            {
                _sampleCount++;
            }
        }
    }
}
