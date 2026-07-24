using System;
using global::LiteNetLib;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

public sealed class LiteNetLibServerDatagramPort :
    IServerDatagramPort,
    IServerConnectionEventPort,
    IDisposable
{
    private readonly EventBasedNetListener _listener;
    private readonly NetManager _manager;
    private readonly NetPeer?[] _peers;
    private readonly FixedDatagramQueue _inbound;
    private readonly FixedServerConnectionEventQueue _connectionEvents;
    private readonly string _connectionKey;
    private bool _disposed;

    public LiteNetLibServerDatagramPort(
        int listenPort,
        string connectionKey,
        int connectionCapacity,
        int datagramCapacity,
        int connectionEventCapacity,
        int maxPayloadBytes,
        int channelCount)
    {
        if ((uint)listenPort > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(listenPort));
        if (string.IsNullOrWhiteSpace(connectionKey)) throw new ArgumentException("Connection key is required.", nameof(connectionKey));
        if (connectionCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(connectionCapacity));
        if ((uint)(channelCount - 1) >= 64u) throw new ArgumentOutOfRangeException(nameof(channelCount));

        _connectionKey = connectionKey;
        _peers = new NetPeer[connectionCapacity];
        _inbound = new FixedDatagramQueue(datagramCapacity, maxPayloadBytes);
        _connectionEvents = new FixedServerConnectionEventQueue(connectionEventCapacity);
        _listener = new EventBasedNetListener();
        _manager = new NetManager(_listener)
        {
            AutoRecycle = true,
            MaxConnectAttempts = 10,
            DisconnectTimeout = 5000,
            ChannelsCount = (byte)channelCount,
        };

        _listener.ConnectionRequestEvent += request =>
        {
            if (_manager.ConnectedPeersCount >= _peers.Length)
            {
                request.Reject();
                return;
            }

            request.AcceptIfKey(_connectionKey);
        };
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;

        if (!_manager.Start(listenPort))
        {
            throw new InvalidOperationException($"LiteNetLib server failed to bind UDP port {listenPort}.");
        }
    }

    public int BoundPort => _manager.LocalPort;

    public void Pump()
    {
        ThrowIfDisposed();
        _manager.PollEvents();
    }

    public bool TryReceiveConnectionEvent(out ServerConnectionEvent connectionEvent)
    {
        ThrowIfDisposed();
        return _connectionEvents.TryDequeue(out connectionEvent);
    }

    public bool TryReceive(
        Span<byte> buffer,
        out int bytesReceived,
        out ConnectionId connectionId,
        out ChannelId channelId)
    {
        ThrowIfDisposed();
        if (!_inbound.TryDequeue(buffer, out bytesReceived, out int connectionValue, out byte channel))
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
        int peerIndex = connectionId.Value - 1;
        if ((uint)peerIndex >= (uint)_peers.Length)
        {
            return DatagramSendStatus.Closed;
        }

        NetPeer? peer = _peers[peerIndex];
        if (peer == null || peer.ConnectionState != ConnectionState.Connected)
        {
            return DatagramSendStatus.Closed;
        }

        peer.Send(payload, channelId.Value, DeliveryMethod.ReliableOrdered);
        return DatagramSendStatus.Sent;
    }

    private void OnPeerConnected(NetPeer peer)
    {
        if ((uint)peer.Id >= (uint)_peers.Length || _peers[peer.Id] != null)
        {
            peer.Disconnect();
            throw new InvalidOperationException($"LiteNetLib assigned invalid or duplicate peer id {peer.Id}.");
        }

        _peers[peer.Id] = peer;
        var connectionEvent = new ServerConnectionEvent(
            new ConnectionId(peer.Id + 1),
            TransportConnectionEventKind.Connected);
        _connectionEvents.Enqueue(in connectionEvent);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if ((uint)peer.Id < (uint)_peers.Length)
        {
            _peers[peer.Id] = null;
        }

        var connectionEvent = new ServerConnectionEvent(
            new ConnectionId(peer.Id + 1),
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

        _inbound.Enqueue(peer.Id + 1, channelNumber, reader.GetRemainingBytesSpan());
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
        if (_disposed) throw new ObjectDisposedException(nameof(LiteNetLibServerDatagramPort));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.Stop();
    }
}
