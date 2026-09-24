using System;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

/// <summary>
/// Adapter-side fault-injecting decorator over a real server datagram port.
/// Applies the same profile to outbound and inbound datagrams.
/// </summary>
public sealed class DeterministicFaultInjectingServerDatagramPort :
    ILiteNetLibServerTransport,
    IDisposable
{
    private readonly LiteNetLibServerDatagramPort _inner;
    private readonly DeterministicTransportFaultInjector _injector;
    private readonly NetworkAdapterMetricsSampler? _metrics;
    private readonly int[] _connectionValues;
    private readonly byte[] _drainBuffer;
    private bool _disposed;

    public DeterministicFaultInjectingServerDatagramPort(
        LiteNetLibServerDatagramPort inner,
        DeterministicTransportFaultInjector injector,
        int connectionCapacity,
        int maxPayloadBytes,
        NetworkAdapterMetricsSampler? metrics = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        if (connectionCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(connectionCapacity));
        if (maxPayloadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

        _metrics = metrics;
        _connectionValues = new int[connectionCapacity];
        _drainBuffer = new byte[maxPayloadBytes];
    }

    public int BoundPort => _inner.BoundPort;

    public DeterministicTransportFaultInjector Injector => _injector;

    public void Pump()
    {
        ThrowIfDisposed();
        _injector.AdvanceFromWallClock();
        _injector.ReleaseDueOutbound(SendReleased);
        _inner.Pump();
        DrainUnderlyingInbound();
        _injector.ReleaseDueInboundToReady();
    }

    public bool TryReceiveConnectionEvent(out ServerConnectionEvent connectionEvent)
    {
        ThrowIfDisposed();
        if (!_inner.TryReceiveConnectionEvent(out connectionEvent))
        {
            return false;
        }

        if (connectionEvent.Kind == TransportConnectionEventKind.Connected)
        {
            AssignConnection(connectionEvent.ConnectionId.Value);
        }
        else
        {
            ReleaseConnection(connectionEvent.ConnectionId.Value);
        }

        return true;
    }

    public bool TryReceive(
        Span<byte> buffer,
        out int bytesReceived,
        out ConnectionId connectionId,
        out ChannelId channelId)
    {
        ThrowIfDisposed();
        if (!_injector.TryReceiveReadyInbound(buffer, out bytesReceived, out int connectionValue, out byte channel))
        {
            connectionId = default;
            channelId = default;
            return false;
        }

        connectionId = new ConnectionId(connectionValue);
        channelId = new ChannelId(channel);
        return true;
    }

    public DatagramSendStatus TrySend(ConnectionId connectionId, ChannelId channelId, ReadOnlySpan<byte> payload)
    {
        ThrowIfDisposed();
        DatagramSendStatus readiness = ProbeConnection(connectionId);
        if (readiness != DatagramSendStatus.Sent)
        {
            return readiness;
        }

        // Loss returns Sent from the caller's perspective (packet left the stack).
        _ = _injector.TryScheduleOutbound(connectionId.Value, channelId.Value, payload);
        // Zero-delay packets must leave on the same call; delayed ones wait for Pump.
        _injector.ReleaseDueOutbound(SendReleased);
        return DatagramSendStatus.Sent;
    }

    public void Disconnect(ConnectionId connectionId)
    {
        ThrowIfDisposed();
        _inner.Disconnect(connectionId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.Dispose();
    }

    private void SendReleased(int connectionValue, byte channel, ReadOnlySpan<byte> payload)
    {
        var connectionId = new ConnectionId(connectionValue);
        DatagramSendStatus status = _inner.TrySend(connectionId, new ChannelId(channel), payload);
        if (status == DatagramSendStatus.Sent)
        {
            int clientIndex = FindClientIndex(connectionValue);
            if (clientIndex >= 0)
            {
                _metrics?.RecordOutboundBytes(clientIndex, payload.Length);
            }
        }
    }

    private void DrainUnderlyingInbound()
    {
        while (_inner.TryReceive(_drainBuffer, out int bytes, out ConnectionId connectionId, out ChannelId channelId))
        {
            _ = _injector.TryScheduleInbound(connectionId.Value, channelId.Value, _drainBuffer.AsSpan(0, bytes));
        }
    }

    private DatagramSendStatus ProbeConnection(ConnectionId connectionId)
    {
        // Zero-length probe is not supported by LiteNetLib send path for status alone;
        // use connection table maintained from events.
        return FindClientIndex(connectionId.Value) >= 0
            ? DatagramSendStatus.Sent
            : DatagramSendStatus.Closed;
    }

    private void AssignConnection(int connectionValue)
    {
        for (int i = 0; i < _connectionValues.Length; i++)
        {
            if (_connectionValues[i] == 0)
            {
                _connectionValues[i] = connectionValue;
                return;
            }
        }

        throw new InvalidOperationException(
            "Fault-injecting server assigned a connection beyond configured capacity.");
    }

    private void ReleaseConnection(int connectionValue)
    {
        for (int i = 0; i < _connectionValues.Length; i++)
        {
            if (_connectionValues[i] == connectionValue)
            {
                _connectionValues[i] = 0;
                return;
            }
        }
    }

    private int FindClientIndex(int connectionValue)
    {
        for (int i = 0; i < _connectionValues.Length; i++)
        {
            if (_connectionValues[i] == connectionValue)
            {
                return i;
            }
        }

        return -1;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DeterministicFaultInjectingServerDatagramPort));
    }
}
