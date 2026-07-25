using System;
using global::LiteNetLib;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

public sealed class LiteNetLibServerDatagramPort :
    IServerDatagramPort,
    IServerConnectionEventPort,
    IServerConnectionControlPort,
    INetworkFaultInjectionMetricsPort,
    IDisposable
{
    private readonly EventBasedNetListener _listener;
    private readonly NetManager _manager;
    private readonly NetPeer?[] _peers;
    private readonly int[] _connectionValues;
    private readonly FixedDatagramQueue _inbound;
    private readonly DeterministicSequencedReorderFilter _reorderFilter;
    private readonly FixedServerConnectionEventQueue _connectionEvents;
    private readonly string _connectionKey;
    private readonly byte _stateChannelId;
    private readonly NetworkFaultInjectionConfigurationSnapshot _faultInjectionConfiguration;
    private int _nextConnectionValue = 1;
    private bool _disposed;

    public LiteNetLibServerDatagramPort(
        int listenPort,
        string connectionKey,
        int connectionCapacity,
        int datagramCapacity,
        int connectionEventCapacity,
        int maxPayloadBytes,
        int channelCount,
        int stateChannelId,
        in LiteNetLibFaultInjectionSettings faults)
    {
        if ((uint)listenPort > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(listenPort));
        if (string.IsNullOrWhiteSpace(connectionKey)) throw new ArgumentException("Connection key is required.", nameof(connectionKey));
        if (connectionCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(connectionCapacity));
        if ((uint)(channelCount - 1) >= 64u) throw new ArgumentOutOfRangeException(nameof(channelCount));
        if ((uint)stateChannelId >= (uint)channelCount) throw new ArgumentOutOfRangeException(nameof(stateChannelId));

        _connectionKey = connectionKey;
        _stateChannelId = (byte)stateChannelId;
        _peers = new NetPeer[connectionCapacity];
        _connectionValues = new int[connectionCapacity];
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
        faults.Apply(_manager, datagramCapacity);
        _faultInjectionConfiguration = faults.CaptureConfiguration();
        _reorderFilter = new DeterministicSequencedReorderFilter(
            connectionCapacity,
            maxPayloadBytes,
            _stateChannelId,
            faults.ReorderPermille,
            faults.Seed);

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
    public long SimulatedStateReorderCount => _reorderFilter.ReorderedStateDatagramCount;

    public NetworkFaultInjectionObservationSnapshot Capture()
    {
        ThrowIfDisposed();
        return new NetworkFaultInjectionObservationSnapshot(
            NetworkProcessRole.AuthoritativeServer,
            in _faultInjectionConfiguration,
            _manager.SimulatedInboundDelayedPacketCount,
            _manager.SimulatedInboundDroppedPacketCount,
            _reorderFilter.ReorderedStateDatagramCount);
    }

    public void Pump()
    {
        ThrowIfDisposed();
        _reorderFilter.BeginPump();
        _manager.PollEvents();
        _reorderFilter.FlushAged(_inbound);
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
        int peerIndex = FindConnectionSlot(connectionId.Value);
        if (peerIndex < 0)
        {
            return DatagramSendStatus.Closed;
        }

        NetPeer? peer = _peers[peerIndex];
        if (peer == null || peer.ConnectionState != ConnectionState.Connected)
        {
            return DatagramSendStatus.Closed;
        }

        peer.Send(payload, channelId.Value, ResolveDeliveryMethod(channelId.Value));
        return DatagramSendStatus.Sent;
    }

    public void Disconnect(ConnectionId connectionId)
    {
        ThrowIfDisposed();
        int peerIndex = FindConnectionSlot(connectionId.Value);
        if (peerIndex < 0)
        {
            throw new InvalidOperationException(
                $"LiteNetLib cannot disconnect unknown connection {connectionId.Value}.");
        }

        NetPeer? peer = _peers[peerIndex];
        if (peer == null || peer.ConnectionState != ConnectionState.Connected)
        {
            throw new InvalidOperationException(
                $"LiteNetLib connection {connectionId.Value} is not connected.");
        }

        peer.Disconnect();
    }

    private void OnPeerConnected(NetPeer peer)
    {
        int slot = FindFreePeerSlot();
        if (slot < 0)
        {
            peer.Disconnect();
            throw new InvalidOperationException("LiteNetLib connected beyond the configured connection capacity.");
        }

        int connectionValue = AllocateConnectionValue();
        _peers[slot] = peer;
        _connectionValues[slot] = connectionValue;
        var connectionEvent = new ServerConnectionEvent(
            new ConnectionId(connectionValue),
            TransportConnectionEventKind.Connected);
        _connectionEvents.Enqueue(in connectionEvent);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        int slot = FindPeerSlot(peer);
        if (slot < 0)
        {
            throw new InvalidOperationException("LiteNetLib disconnected a peer that is not in the connection table.");
        }

        int connectionValue = _connectionValues[slot];
        _reorderFilter.DiscardConnection(connectionValue);
        _peers[slot] = null;
        _connectionValues[slot] = 0;
        var connectionEvent = new ServerConnectionEvent(
            new ConnectionId(connectionValue),
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
        DeliveryMethod expected = ResolveDeliveryMethod(channelNumber);
        if (deliveryMethod != expected)
        {
            throw new InvalidOperationException(
                $"Unexpected delivery method {deliveryMethod} on channel {channelNumber}; expected {expected}.");
        }

        int slot = FindPeerSlot(peer);
        if (slot < 0)
        {
            throw new InvalidOperationException("LiteNetLib received data from a peer that is not in the connection table.");
        }

        _reorderFilter.Enqueue(
            _connectionValues[slot],
            channelNumber,
            reader.GetRemainingBytesSpan(),
            _inbound);
    }

    private int FindFreePeerSlot()
    {
        for (int i = 0; i < _peers.Length; i++)
        {
            if (_peers[i] == null)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindPeerSlot(NetPeer peer)
    {
        for (int i = 0; i < _peers.Length; i++)
        {
            if (ReferenceEquals(_peers[i], peer))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindConnectionSlot(int connectionValue)
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

    private int AllocateConnectionValue()
    {
        for (int attempt = 0; attempt <= _connectionValues.Length; attempt++)
        {
            int candidate = _nextConnectionValue;
            _nextConnectionValue = candidate == int.MaxValue ? 1 : candidate + 1;
            if (FindConnectionSlot(candidate) < 0)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique transport connection generation.");
    }

    private DeliveryMethod ResolveDeliveryMethod(byte channelNumber)
        => channelNumber == _stateChannelId
            ? DeliveryMethod.Sequenced
            : DeliveryMethod.ReliableOrdered;

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
