using System;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

/// <summary>
/// Adapter-side fault-injecting decorator over a real client datagram port.
/// Applies the same profile to outbound and inbound datagrams.
/// </summary>
public sealed class DeterministicFaultInjectingClientDatagramPort :
    ILiteNetLibClientTransport,
    IDisposable
{
    private readonly LiteNetLibClientDatagramPort _inner;
    private readonly DeterministicTransportFaultInjector _injector;
    private readonly NetworkAdapterMetricsSampler? _metrics;
    private readonly byte[] _drainBuffer;
    private bool _connected;
    private bool _disposed;

    public DeterministicFaultInjectingClientDatagramPort(
        LiteNetLibClientDatagramPort inner,
        DeterministicTransportFaultInjector injector,
        int maxPayloadBytes,
        NetworkAdapterMetricsSampler? metrics = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        if (maxPayloadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

        _metrics = metrics;
        _drainBuffer = new byte[maxPayloadBytes];
    }

    public DeterministicTransportFaultInjector Injector => _injector;

    public ClientConnectionControlState State
    {
        get
        {
            ThrowIfDisposed();
            return _inner.State;
        }
    }

    public bool TryConnect()
    {
        ThrowIfDisposed();
        return _inner.TryConnect();
    }

    public void Disconnect()
    {
        ThrowIfDisposed();
        _inner.Disconnect();
    }

    public void Pump()
    {
        ThrowIfDisposed();
        _injector.AdvanceFromWallClock();
        _injector.ReleaseDueOutbound(SendReleased);
        _inner.Pump();
        DrainUnderlyingInbound();
        _injector.ReleaseDueInboundToReady();
    }

    public bool TryReceiveConnectionEvent(out ClientConnectionEvent connectionEvent)
    {
        ThrowIfDisposed();
        if (!_inner.TryReceiveConnectionEvent(out connectionEvent))
        {
            return false;
        }

        _connected = connectionEvent.Kind == TransportConnectionEventKind.Connected;
        return true;
    }

    public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ChannelId channelId)
    {
        ThrowIfDisposed();
        if (!_injector.TryReceiveReadyInbound(buffer, out bytesReceived, out _, out byte channel))
        {
            channelId = default;
            return false;
        }

        channelId = new ChannelId(channel);
        return true;
    }

    public DatagramSendStatus TrySend(ChannelId channelId, ReadOnlySpan<byte> payload)
    {
        ThrowIfDisposed();
        if (!_connected && _inner.State != ClientConnectionControlState.Connected)
        {
            return _inner.State == ClientConnectionControlState.Connecting
                ? DatagramSendStatus.NotReady
                : DatagramSendStatus.Closed;
        }

        _ = _injector.TryScheduleOutbound(connectionValue: 0, channelId.Value, payload);
        _injector.ReleaseDueOutbound(SendReleased);
        return DatagramSendStatus.Sent;
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
        _ = connectionValue;
        DatagramSendStatus status = _inner.TrySend(new ChannelId(channel), payload);
        if (status == DatagramSendStatus.Sent)
        {
            _metrics?.RecordOutboundBytes(clientIndex: 0, payload.Length);
        }
    }

    private void DrainUnderlyingInbound()
    {
        while (_inner.TryReceive(_drainBuffer, out int bytes, out ChannelId channelId))
        {
            _ = _injector.TryScheduleInbound(connectionValue: 0, channelId.Value, _drainBuffer.AsSpan(0, bytes));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DeterministicFaultInjectingClientDatagramPort));
    }
}
