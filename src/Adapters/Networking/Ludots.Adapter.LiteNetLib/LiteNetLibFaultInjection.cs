using System;
using global::LiteNetLib;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Runtime;
using System.Threading;

namespace Ludots.Adapter.LiteNetLib;

public readonly struct LiteNetLibFaultInjectionSettings
{
    private const int MinimumSimulatedRoundTripLatencyMs = 12;
    private const int ReorderHoldPublishIntervals = 3;

    private LiteNetLibFaultInjectionSettings(
        string profileId,
        int seed,
        int roundTripLatencyMs,
        int jitterMs,
        int packetLossPermille,
        int reorderPermille,
        int reorderHoldTimeoutMilliseconds)
    {
        ProfileId = profileId;
        Seed = seed;
        RoundTripLatencyMs = roundTripLatencyMs;
        JitterMs = jitterMs;
        PacketLossPermille = packetLossPermille;
        ReorderPermille = reorderPermille;
        ReorderHoldTimeoutMilliseconds = reorderHoldTimeoutMilliseconds;
    }

    public string ProfileId { get; }
    public int Seed { get; }
    public int RoundTripLatencyMs { get; }
    public int JitterMs { get; }
    public int PacketLossPermille { get; }
    public int PacketLossPercent => PacketLossPermille / 10;
    public int ReorderPermille { get; }
    internal int ReorderHoldTimeoutMilliseconds { get; }

    public static LiteNetLibFaultInjectionSettings Disabled(int seed = 1) =>
        new(NetworkHostBootstrapConfig.NormalFaultProfile, seed, 0, 0, 0, 0, 1);

    public static LiteNetLibFaultInjectionSettings Create(
        NetworkRuntimeConfig config,
        NetworkHostBootstrapConfig host)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(host);
        NetworkFaultProfileConfig profile = host.ResolveFaultProfile(config);
        profile.Validate(host.FaultProfile);
        if (profile.PacketLossPermille % 10 != 0)
        {
            throw new InvalidOperationException(
                $"LiteNetLib fault injection requires packetLossPermille in whole-percent increments; got {profile.PacketLossPermille}.");
        }

        int minimumLatency = Math.Max(0, profile.RoundTripLatencyMs - profile.JitterMs);
        if ((profile.RoundTripLatencyMs != 0 || profile.JitterMs != 0) &&
            minimumLatency < MinimumSimulatedRoundTripLatencyMs)
        {
            throw new InvalidOperationException(
                $"LiteNetLib fault injection cannot represent a minimum round-trip latency of {minimumLatency} ms; " +
                $"disable latency or configure roundTripLatencyMs - jitterMs to at least {MinimumSimulatedRoundTripLatencyMs} ms.");
        }

        int reorderHoldTimeoutMilliseconds = ResolveReorderHoldTimeoutMilliseconds(config, profile);

        return new LiteNetLibFaultInjectionSettings(
            host.FaultProfile,
            host.FaultSeed,
            profile.RoundTripLatencyMs,
            profile.JitterMs,
            profile.PacketLossPermille,
            profile.ReorderPermille,
            reorderHoldTimeoutMilliseconds);
    }

    private static int ResolveReorderHoldTimeoutMilliseconds(
        NetworkRuntimeConfig config,
        NetworkFaultProfileConfig profile)
    {
        if (profile.ReorderPermille == 0)
        {
            return 1;
        }
        if (config.StatePublishRateHz <= 0)
        {
            throw new InvalidOperationException(
                "LiteNetLib state reordering requires a positive statePublishRateHz.");
        }

        long publishIntervalMilliseconds =
            (1000L + config.StatePublishRateHz - 1L) / config.StatePublishRateHz;
        long maximumRoundTripLatencyMilliseconds = checked(
            (long)profile.RoundTripLatencyMs + profile.JitterMs);
        long maximumInboundLatencyMilliseconds =
            (maximumRoundTripLatencyMilliseconds + 1L) / 2L;
        long timeoutMilliseconds = checked(
            (publishIntervalMilliseconds * ReorderHoldPublishIntervals) +
            maximumInboundLatencyMilliseconds);
        if (timeoutMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"LiteNetLib state reorder hold timeout {timeoutMilliseconds} ms exceeds the supported range.");
        }

        return checked((int)timeoutMilliseconds);
    }

    internal NetworkFaultInjectionConfigurationSnapshot CaptureConfiguration() =>
        new(
            LiteNetLibTransportFactory.TransportIdentity,
            ProfileId,
            Seed,
            RoundTripLatencyMs,
            JitterMs,
            PacketLossPermille,
            ReorderPermille);

    internal void Apply(NetManager manager, int inboundPacketCapacity)
    {
        ArgumentNullException.ThrowIfNull(manager);
        manager.ConfigureSimulation(Seed, inboundPacketCapacity);
        manager.SimulateOutboundFaults = false;
        int minimumLatency = Math.Max(0, RoundTripLatencyMs - JitterMs);
        int maximumLatency = checked(RoundTripLatencyMs + JitterMs);
        manager.SimulateLatency = maximumLatency > 0;
        manager.SimulationMinLatency = minimumLatency;
        manager.SimulationMaxLatency = maximumLatency;
        manager.SimulatePacketLoss = PacketLossPercent > 0;
        manager.SimulationPacketLossChance = PacketLossPercent;
    }
}

internal sealed class DeterministicSequencedReorderFilter
{
    private const int DefaultHoldTimeoutMilliseconds = 1000;

    private readonly byte[][] _payloads;
    private readonly int[] _lengths;
    private readonly int[] _connections;
    private readonly long[] _heldAtMilliseconds;
    private readonly bool[] _active;
    private readonly byte _stateChannel;
    private readonly int _reorderPermille;
    private readonly int _holdTimeoutMilliseconds;
    private uint _randomState;
    private long _pumpMilliseconds;
    private bool _pumpStarted;

    public DeterministicSequencedReorderFilter(
        int connectionCapacity,
        int maxPayloadBytes,
        byte stateChannel,
        int reorderPermille,
        int seed)
        : this(
            connectionCapacity,
            maxPayloadBytes,
            stateChannel,
            reorderPermille,
            seed,
            DefaultHoldTimeoutMilliseconds)
    {
    }

    public DeterministicSequencedReorderFilter(
        int connectionCapacity,
        int maxPayloadBytes,
        byte stateChannel,
        int reorderPermille,
        int seed,
        int holdTimeoutMilliseconds)
    {
        if (connectionCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(connectionCapacity));
        if (maxPayloadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        if ((uint)reorderPermille > 1000u) throw new ArgumentOutOfRangeException(nameof(reorderPermille));
        if (seed <= 0) throw new ArgumentOutOfRangeException(nameof(seed));
        if (holdTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(holdTimeoutMilliseconds));

        _payloads = new byte[connectionCapacity][];
        _lengths = new int[connectionCapacity];
        _connections = new int[connectionCapacity];
        _heldAtMilliseconds = new long[connectionCapacity];
        _active = new bool[connectionCapacity];
        for (int i = 0; i < _payloads.Length; i++)
        {
            _payloads[i] = new byte[maxPayloadBytes];
        }

        _stateChannel = stateChannel;
        _reorderPermille = reorderPermille;
        _holdTimeoutMilliseconds = holdTimeoutMilliseconds;
        _randomState = unchecked((uint)seed);
    }

    private long _reorderedStateDatagramCount;

    public long ReorderedStateDatagramCount => Interlocked.Read(ref _reorderedStateDatagramCount);

    public void BeginPump() => BeginPump(Environment.TickCount64);

    internal void BeginPump(long monotonicMilliseconds)
    {
        if (monotonicMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monotonicMilliseconds));
        }
        if (_pumpStarted && monotonicMilliseconds < _pumpMilliseconds)
        {
            throw new InvalidOperationException("Network reorder clock must be monotonic.");
        }

        _pumpMilliseconds = monotonicMilliseconds;
        _pumpStarted = true;
    }

    public void Enqueue(
        int connection,
        byte channel,
        ReadOnlySpan<byte> payload,
        FixedDatagramQueue destination)
    {
        if (channel != _stateChannel || _reorderPermille == 0)
        {
            destination.Enqueue(connection, channel, payload);
            return;
        }

        int pending = FindConnection(connection);
        if (pending >= 0)
        {
            destination.Enqueue(connection, channel, payload);
            destination.Enqueue(
                _connections[pending],
                _stateChannel,
                _payloads[pending].AsSpan(0, _lengths[pending]));
            Clear(pending);
            Interlocked.Increment(ref _reorderedStateDatagramCount);
            return;
        }

        if (NextPermille() >= _reorderPermille)
        {
            destination.Enqueue(connection, channel, payload);
            return;
        }

        int free = FindFree();
        if (free < 0)
        {
            throw new InvalidOperationException(
                $"Sequenced reorder capacity {_active.Length} is exhausted.");
        }
        if (payload.Length > _payloads[free].Length)
        {
            throw new InvalidOperationException(
                $"Sequenced reorder payload {payload.Length} exceeds configured maximum {_payloads[free].Length} bytes.");
        }
        if (!_pumpStarted)
        {
            throw new InvalidOperationException(
                "Network reorder filter must begin a pump before holding state datagrams.");
        }

        payload.CopyTo(_payloads[free]);
        _lengths[free] = payload.Length;
        _connections[free] = connection;
        _heldAtMilliseconds[free] = _pumpMilliseconds;
        _active[free] = true;
    }

    public void FlushAged(FixedDatagramQueue destination) => FlushExpired(destination);

    internal void FlushExpired(FixedDatagramQueue destination)
    {
        if (!_pumpStarted)
        {
            throw new InvalidOperationException(
                "Network reorder filter must begin a pump before expiring state datagrams.");
        }

        for (int i = 0; i < _active.Length; i++)
        {
            if (!_active[i] ||
                _pumpMilliseconds - _heldAtMilliseconds[i] < _holdTimeoutMilliseconds)
            {
                continue;
            }

            destination.Enqueue(
                _connections[i],
                _stateChannel,
                _payloads[i].AsSpan(0, _lengths[i]));
            Clear(i);
        }
    }

    public void DiscardConnection(int connection)
    {
        int index = FindConnection(connection);
        if (index >= 0)
        {
            Clear(index);
        }
    }

    private int NextPermille()
    {
        uint value = _randomState;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        _randomState = value;
        return (int)(value % 1000u);
    }

    private int FindConnection(int connection)
    {
        for (int i = 0; i < _active.Length; i++)
        {
            if (_active[i] && _connections[i] == connection)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindFree()
    {
        for (int i = 0; i < _active.Length; i++)
        {
            if (!_active[i])
            {
                return i;
            }
        }

        return -1;
    }

    private void Clear(int index)
    {
        _active[index] = false;
        _lengths[index] = 0;
        _connections[index] = 0;
        _heldAtMilliseconds[index] = 0;
    }
}
