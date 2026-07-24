using System;
using System.Diagnostics;
using global::LiteNetLib;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

public sealed class LiteNetLibServerDatagramPort :
    IServerDatagramPort,
    IServerConnectionEventPort,
    IServerConnectionControlPort,
    IDisposable
{
    private readonly EventBasedNetListener _listener;
    private readonly NetManager _manager;
    private readonly NetPeer?[] _peers;
    private readonly bool[] _pendingDisconnects;
    private readonly long[] _disconnectRequestTimestamps;
    private readonly int[] _pendingReliableDeliveries;
    private readonly FixedDatagramQueue _inbound;
    private readonly FixedServerConnectionEventQueue _connectionEvents;
    private readonly LiteNetLibChannelDeliveryContract _delivery;
    private readonly string _connectionKey;
    private readonly long _reliableDisconnectFlushTimeoutTicks;
    private bool _disposed;

    public LiteNetLibServerDatagramPort(
        int listenPort,
        string connectionKey,
        int connectionCapacity,
        int datagramCapacity,
        int connectionEventCapacity,
        int maxPayloadBytes,
        int maxConnectAttempts,
        int disconnectTimeoutMilliseconds,
        int reliableDisconnectFlushTimeoutMilliseconds,
        int channelCount,
        ChannelId controlChannel,
        ChannelId commandChannel,
        ChannelId stateChannel)
    {
        if ((uint)listenPort > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(listenPort));
        if (string.IsNullOrWhiteSpace(connectionKey)) throw new ArgumentException("Connection key is required.", nameof(connectionKey));
        if (connectionCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(connectionCapacity));
        if (maxConnectAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxConnectAttempts));
        if (disconnectTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(disconnectTimeoutMilliseconds));
        if (reliableDisconnectFlushTimeoutMilliseconds <= 0 ||
            reliableDisconnectFlushTimeoutMilliseconds >= disconnectTimeoutMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(reliableDisconnectFlushTimeoutMilliseconds));
        }
        if ((uint)(channelCount - 1) >= 64u) throw new ArgumentOutOfRangeException(nameof(channelCount));

        _connectionKey = connectionKey;
        _peers = new NetPeer[connectionCapacity];
        _pendingDisconnects = new bool[connectionCapacity];
        _disconnectRequestTimestamps = new long[connectionCapacity];
        _pendingReliableDeliveries = new int[connectionCapacity];
        _reliableDisconnectFlushTimeoutTicks = checked(
            (long)reliableDisconnectFlushTimeoutMilliseconds * Stopwatch.Frequency / 1000L);
        _inbound = new FixedDatagramQueue(datagramCapacity, maxPayloadBytes);
        _connectionEvents = new FixedServerConnectionEventQueue(connectionEventCapacity);
        _delivery = new LiteNetLibChannelDeliveryContract(
            channelCount,
            controlChannel,
            commandChannel,
            stateChannel);
        _listener = new EventBasedNetListener();
        _manager = new NetManager(_listener)
        {
            AutoRecycle = true,
            MaxConnectAttempts = maxConnectAttempts,
            DisconnectTimeout = disconnectTimeoutMilliseconds,
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
        _listener.DeliveryEvent += OnDelivery;

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
        ProcessPendingDisconnects();
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

        DeliveryMethod delivery = _delivery.GetExpected(channelId.Value);
        if (delivery == DeliveryMethod.Sequenced)
        {
            peer.Send(payload, channelId.Value, delivery);
            return DatagramSendStatus.Sent;
        }

        _pendingReliableDeliveries[peerIndex] = checked(_pendingReliableDeliveries[peerIndex] + 1);
        try
        {
            peer.SendWithDeliveryEvent(payload, channelId.Value, delivery, this);
        }
        catch
        {
            _pendingReliableDeliveries[peerIndex]--;
            throw;
        }

        return DatagramSendStatus.Sent;
    }

    public void DisconnectAfterReliableFlush(ConnectionId connectionId)
    {
        ThrowIfDisposed();
        int peerIndex = connectionId.Value - 1;
        if ((uint)peerIndex >= (uint)_peers.Length)
        {
            throw new InvalidOperationException($"Connection {connectionId.Value} is outside the configured peer capacity.");
        }

        if (_pendingDisconnects[peerIndex])
        {
            return;
        }

        NetPeer? peer = _peers[peerIndex];
        if (peer == null || peer.ConnectionState != ConnectionState.Connected)
        {
            throw new InvalidOperationException($"Connection {connectionId.Value} is not connected.");
        }

        _pendingDisconnects[peerIndex] = true;
        _disconnectRequestTimestamps[peerIndex] = Stopwatch.GetTimestamp();
    }

    private void OnPeerConnected(NetPeer peer)
    {
        if ((uint)peer.Id >= (uint)_peers.Length || _peers[peer.Id] != null)
        {
            peer.Disconnect();
            throw new InvalidOperationException($"LiteNetLib assigned invalid or duplicate peer id {peer.Id}.");
        }

        _peers[peer.Id] = peer;
        _pendingReliableDeliveries[peer.Id] = 0;
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
            _pendingDisconnects[peer.Id] = false;
            _disconnectRequestTimestamps[peer.Id] = 0;
            _pendingReliableDeliveries[peer.Id] = 0;
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
        _delivery.ValidateReceived(channelNumber, deliveryMethod);
        _inbound.Enqueue(peer.Id + 1, channelNumber, reader.GetRemainingBytesSpan());
    }

    private void OnDelivery(NetPeer peer, object userData)
    {
        if (!ReferenceEquals(userData, this) ||
            (uint)peer.Id >= (uint)_peers.Length ||
            !ReferenceEquals(_peers[peer.Id], peer) ||
            _pendingReliableDeliveries[peer.Id] <= 0)
        {
            throw new InvalidOperationException(
                $"LiteNetLib reported an unmatched reliable delivery for peer {peer.Id}.");
        }

        _pendingReliableDeliveries[peer.Id]--;
    }

    private void ProcessPendingDisconnects()
    {
        long now = Stopwatch.GetTimestamp();
        for (int i = 0; i < _pendingDisconnects.Length; i++)
        {
            if (!_pendingDisconnects[i])
            {
                continue;
            }

            NetPeer? peer = _peers[i];
            if (peer == null)
            {
                throw new InvalidOperationException($"Disconnecting connection {i + 1} lost its peer before the disconnect event.");
            }

            if (_disconnectRequestTimestamps[i] == 0 || peer.ConnectionState != ConnectionState.Connected)
            {
                continue;
            }

            int pendingReliableDeliveries = _pendingReliableDeliveries[i];
            if (pendingReliableDeliveries != 0)
            {
                if (now - _disconnectRequestTimestamps[i] >= _reliableDisconnectFlushTimeoutTicks)
                {
                    throw new InvalidOperationException(
                        $"Reliable disconnect flush timed out for connection {i + 1}; " +
                        $"{pendingReliableDeliveries} reliable deliveries remain unacknowledged.");
                }

                continue;
            }

            _disconnectRequestTimestamps[i] = 0;
            peer.Disconnect();
        }
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
