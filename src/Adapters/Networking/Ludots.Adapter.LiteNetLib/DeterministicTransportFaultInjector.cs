using System;
using Ludots.Core.Networking.Configuration;

namespace Ludots.Adapter.LiteNetLib;

public delegate void DelayedPacketDelivery(int connectionValue, byte channel, ReadOnlySpan<byte> payload);

/// <summary>
/// Seeded, allocation-free (steady state) latency/jitter/loss/reorder decisions for acceptance.
/// Time advances via <see cref="AdvanceMs"/> (tests) or wall-clock delta in <see cref="AdvanceFromWallClock"/>.
/// </summary>
public sealed class DeterministicTransportFaultInjector
{
    private readonly NetworkFaultProfileConfig _profile;
    private readonly DelayedSlot[] _outbound;
    private readonly DelayedSlot[] _inbound;
    private readonly byte[][] _outboundPayloads;
    private readonly byte[][] _inboundPayloads;
    private readonly FixedDatagramQueue _readyInbound;
    private uint _rngState;
    private long _nowMs;
    private long _lastPumpTimestamp;
    private readonly bool _useWallClock;
    private int _outboundCount;
    private int _inboundCount;

    public DeterministicTransportFaultInjector(
        NetworkFaultProfileConfig profile,
        uint seed,
        int slotCapacity,
        int maxPayloadBytes,
        bool useWallClock = true)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        profile.Validate(nameof(profile));
        if (slotCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(slotCapacity));
        if (maxPayloadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

        _rngState = seed == 0 ? 0xA5A5A5A5u : seed;
        _useWallClock = useWallClock;
        _outbound = new DelayedSlot[slotCapacity];
        _inbound = new DelayedSlot[slotCapacity];
        _outboundPayloads = new byte[slotCapacity][];
        _inboundPayloads = new byte[slotCapacity][];
        for (int i = 0; i < slotCapacity; i++)
        {
            _outboundPayloads[i] = new byte[maxPayloadBytes];
            _inboundPayloads[i] = new byte[maxPayloadBytes];
        }

        _readyInbound = new FixedDatagramQueue(slotCapacity, maxPayloadBytes);
        _lastPumpTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public long NowMs => _nowMs;

    public int PendingOutboundCount => _outboundCount;

    public int PendingInboundCount => _inboundCount;

    public uint RngState => _rngState;

    public void AdvanceMs(int deltaMs)
    {
        if (deltaMs < 0) throw new ArgumentOutOfRangeException(nameof(deltaMs));
        _nowMs = checked(_nowMs + deltaMs);
    }

    public void AdvanceFromWallClock()
    {
        if (!_useWallClock)
        {
            return;
        }

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastPumpTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _lastPumpTimestamp = now;
        int delta = elapsedMs < 1.0 ? 1 : (int)elapsedMs;
        AdvanceMs(delta);
    }

    /// <summary>Deterministic loss decision (permille). Exposed for tests.</summary>
    public bool NextShouldDrop()
    {
        if (_profile.PacketLossPermille <= 0)
        {
            return false;
        }

        return NextPermille() < (uint)_profile.PacketLossPermille;
    }

    /// <summary>One-way delay in ms = RTT/2 ± jitter. Exposed for tests.</summary>
    public int NextOneWayDelayMs()
    {
        int baseDelay = _profile.RoundTripLatencyMs / 2;
        if (_profile.JitterMs <= 0)
        {
            return baseDelay;
        }

        int span = checked((_profile.JitterMs * 2) + 1);
        int offset = (int)(NextUInt32() % (uint)span) - _profile.JitterMs;
        int delay = baseDelay + offset;
        return delay < 0 ? 0 : delay;
    }

    public bool NextShouldReorder()
    {
        if (_profile.ReorderPermille <= 0)
        {
            return false;
        }

        return NextPermille() < (uint)_profile.ReorderPermille;
    }

    /// <summary>Returns false when the packet was dropped by loss injection; true when accepted/scheduled.</summary>
    public bool TryScheduleOutbound(int connectionValue, byte channel, ReadOnlySpan<byte> payload)
    {
        if (NextShouldDrop())
        {
            return false;
        }

        Enqueue(_outbound, _outboundPayloads, ref _outboundCount, connectionValue, channel, payload);
        return true;
    }

    public bool TryScheduleInbound(int connectionValue, byte channel, ReadOnlySpan<byte> payload)
    {
        if (NextShouldDrop())
        {
            return false;
        }

        Enqueue(_inbound, _inboundPayloads, ref _inboundCount, connectionValue, channel, payload);
        return true;
    }

    public void ReleaseDueOutbound(DelayedPacketDelivery send) =>
        ReleaseDue(_outbound, _outboundPayloads, ref _outboundCount, send);

    public void ReleaseDueInboundToReady() =>
        ReleaseDue(
            _inbound,
            _inboundPayloads,
            ref _inboundCount,
            (connectionValue, channel, payload) => _readyInbound.Enqueue(connectionValue, channel, payload));

    public bool TryReceiveReadyInbound(
        Span<byte> buffer,
        out int bytesReceived,
        out int connectionValue,
        out byte channel) =>
        _readyInbound.TryDequeue(buffer, out bytesReceived, out connectionValue, out channel);

    private void Enqueue(
        DelayedSlot[] slots,
        byte[][] payloads,
        ref int count,
        int connectionValue,
        byte channel,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length > payloads[0].Length)
        {
            throw new InvalidOperationException(
                $"Fault injector payload {payload.Length} exceeds configured maximum {payloads[0].Length} bytes.");
        }

        if (count == slots.Length)
        {
            throw new InvalidOperationException(
                $"Fault injector delay capacity {slots.Length} is exhausted.");
        }

        int free = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Occupied)
            {
                free = i;
                break;
            }
        }

        if (free < 0)
        {
            throw new InvalidOperationException(
                $"Fault injector delay capacity {slots.Length} is exhausted.");
        }

        int delayMs = NextOneWayDelayMs();
        long releaseAt = checked(_nowMs + delayMs);
        payload.CopyTo(payloads[free]);
        slots[free] = new DelayedSlot
        {
            Occupied = true,
            ReleaseTickMs = releaseAt,
            ConnectionValue = connectionValue,
            Channel = channel,
            Length = payload.Length,
        };
        count++;

        if (NextShouldReorder())
        {
            TrySwapWithLaterSlot(slots, free, releaseAt);
        }
    }

    private static void TrySwapWithLaterSlot(DelayedSlot[] slots, int free, long releaseAt)
    {
        int later = -1;
        long laterRelease = long.MaxValue;
        for (int i = 0; i < slots.Length; i++)
        {
            if (i == free || !slots[i].Occupied)
            {
                continue;
            }

            if (slots[i].ReleaseTickMs > releaseAt && slots[i].ReleaseTickMs < laterRelease)
            {
                later = i;
                laterRelease = slots[i].ReleaseTickMs;
            }
        }

        if (later < 0)
        {
            return;
        }

        long swap = slots[free].ReleaseTickMs;
        slots[free].ReleaseTickMs = slots[later].ReleaseTickMs;
        slots[later].ReleaseTickMs = swap;
    }

    private void ReleaseDue(
        DelayedSlot[] slots,
        byte[][] payloads,
        ref int count,
        DelayedPacketDelivery deliver)
    {
        while (true)
        {
            int due = -1;
            long dueAt = long.MaxValue;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Occupied || slots[i].ReleaseTickMs > _nowMs)
                {
                    continue;
                }

                if (slots[i].ReleaseTickMs < dueAt)
                {
                    dueAt = slots[i].ReleaseTickMs;
                    due = i;
                }
            }

            if (due < 0)
            {
                return;
            }

            DelayedSlot slot = slots[due];
            deliver(slot.ConnectionValue, slot.Channel, payloads[due].AsSpan(0, slot.Length));
            slots[due] = default;
            count--;
        }
    }

    private uint NextPermille() => NextUInt32() % 1000u;

    private uint NextUInt32()
    {
        uint x = _rngState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _rngState = x;
        return x;
    }

    private struct DelayedSlot
    {
        public bool Occupied;
        public long ReleaseTickMs;
        public int ConnectionValue;
        public byte Channel;
        public int Length;
    }
}
