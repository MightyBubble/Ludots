using System;
using global::LiteNetLib;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

public sealed class LiteNetLibClientDatagramPort :
    IClientDatagramPort,
    IClientConnectionEventPort,
    IDisposable
{
    private readonly EventBasedNetListener _listener;
    private readonly NetManager _manager;
    private readonly FixedDatagramQueue _inbound;
    private readonly FixedClientConnectionEventQueue _connectionEvents;
    private NetPeer? _serverPeer;
    private bool _disposed;

    public LiteNetLibClientDatagramPort(
        string host,
        int port,
        string connectionKey,
        int datagramCapacity,
        int connectionEventCapacity,
        int maxPayloadBytes,
        int channelCount)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
        if ((uint)port > ushort.MaxValue || port == 0) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(connectionKey)) throw new ArgumentException("Connection key is required.", nameof(connectionKey));
        if ((uint)(channelCount - 1) >= 64u) throw new ArgumentOutOfRangeException(nameof(channelCount));

        _inbound = new FixedDatagramQueue(datagramCapacity, maxPayloadBytes);
        _connectionEvents = new FixedClientConnectionEventQueue(connectionEventCapacity);
        _listener = new EventBasedNetListener();
        _manager = new NetManager(_listener)
        {
            AutoRecycle = true,
            MaxConnectAttempts = 10,
            DisconnectTimeout = 5000,
            ChannelsCount = (byte)channelCount,
        };

        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;

        if (!_manager.Start())
        {
            throw new InvalidOperationException("LiteNetLib client failed to start its UDP endpoint.");
        }

        _serverPeer = _manager.Connect(host, port, connectionKey)
            ?? throw new InvalidOperationException($"LiteNetLib client failed to start connection to {host}:{port}.");
    }

    public void Pump()
    {
        ThrowIfDisposed();
        _manager.PollEvents();
    }

    public bool TryReceiveConnectionEvent(out ClientConnectionEvent connectionEvent)
    {
        ThrowIfDisposed();
        return _connectionEvents.TryDequeue(out connectionEvent);
    }

    public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ChannelId channelId)
    {
        ThrowIfDisposed();
        if (!_inbound.TryDequeue(buffer, out bytesReceived, out _, out byte channel))
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
        if (_serverPeer == null || _serverPeer.ConnectionState != ConnectionState.Connected)
        {
            return _serverPeer == null || _serverPeer.ConnectionState == ConnectionState.Disconnected
                ? DatagramSendStatus.Closed
                : DatagramSendStatus.NotReady;
        }

        _serverPeer.Send(payload, channelId.Value, DeliveryMethod.ReliableOrdered);
        return DatagramSendStatus.Sent;
    }

    private void OnPeerConnected(NetPeer peer)
    {
        _serverPeer = peer;
        var connectionEvent = new ClientConnectionEvent(TransportConnectionEventKind.Connected);
        _connectionEvents.Enqueue(in connectionEvent);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        _serverPeer = null;
        var connectionEvent = new ClientConnectionEvent(
            TransportConnectionEventKind.Disconnected,
            MapDisconnectReason(info.Reason));
        _connectionEvents.Enqueue(in connectionEvent);
    }

    private void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        if (deliveryMethod != DeliveryMethod.ReliableOrdered)
        {
            throw new InvalidOperationException($"Unexpected delivery method {deliveryMethod}; reliable ordered is required.");
        }

        _inbound.Enqueue(connectionValue: 0, channelNumber, reader.GetRemainingBytesSpan());
    }

    private static TransportDisconnectReason MapDisconnectReason(DisconnectReason reason) => reason switch
    {
        DisconnectReason.Timeout => TransportDisconnectReason.Timeout,
        DisconnectReason.RemoteConnectionClose => TransportDisconnectReason.RemoteClosed,
        DisconnectReason.ConnectionRejected => TransportDisconnectReason.Rejected,
        DisconnectReason.DisconnectPeerCalled => TransportDisconnectReason.LocalClosed,
        _ => TransportDisconnectReason.TransportError,
    };

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LiteNetLibClientDatagramPort));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.Stop();
    }
}
