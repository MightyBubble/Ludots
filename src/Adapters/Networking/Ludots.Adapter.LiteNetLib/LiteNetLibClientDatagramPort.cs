using System;
using global::LiteNetLib;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

public sealed class LiteNetLibClientDatagramPort :
    IClientDatagramPort,
    IClientConnectionEventPort,
    IClientConnectionControlPort,
    INetworkFaultInjectionMetricsPort,
    IDisposable
{
    private readonly EventBasedNetListener _listener;
    private readonly NetManager _manager;
    private readonly FixedDatagramQueue _inbound;
    private readonly DeterministicSequencedReorderFilter _reorderFilter;
    private readonly FixedClientConnectionEventQueue _connectionEvents;
    private readonly byte _stateChannelId;
    private readonly NetworkFaultInjectionConfigurationSnapshot _faultInjectionConfiguration;
    private readonly string _host;
    private readonly int _port;
    private readonly string _connectionKey;
    private NetPeer? _serverPeer;
    private bool _disposed;

    public LiteNetLibClientDatagramPort(
        string host,
        int port,
        string connectionKey,
        int datagramCapacity,
        int connectionEventCapacity,
        int maxPayloadBytes,
        int channelCount,
        int stateChannelId,
        in LiteNetLibFaultInjectionSettings faults)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
        if ((uint)port > ushort.MaxValue || port == 0) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(connectionKey)) throw new ArgumentException("Connection key is required.", nameof(connectionKey));
        if ((uint)(channelCount - 1) >= 64u) throw new ArgumentOutOfRangeException(nameof(channelCount));
        if ((uint)stateChannelId >= (uint)channelCount) throw new ArgumentOutOfRangeException(nameof(stateChannelId));

        _host = host;
        _port = port;
        _connectionKey = connectionKey;
        _stateChannelId = (byte)stateChannelId;
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
        faults.Apply(_manager, datagramCapacity);
        _faultInjectionConfiguration = faults.CaptureConfiguration();
        _reorderFilter = new DeterministicSequencedReorderFilter(
            connectionCapacity: 1,
            maxPayloadBytes: maxPayloadBytes,
            stateChannel: _stateChannelId,
            reorderPermille: faults.ReorderPermille,
            seed: faults.Seed,
            holdTimeoutMilliseconds: faults.ReorderHoldTimeoutMilliseconds);

        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;

        if (!_manager.Start())
        {
            throw new InvalidOperationException("LiteNetLib client failed to start its UDP endpoint.");
        }

    }

    public ClientConnectionControlState State
    {
        get
        {
            ThrowIfDisposed();
            return _serverPeer?.ConnectionState switch
            {
                ConnectionState.Connected => ClientConnectionControlState.Connected,
                ConnectionState.Outgoing => ClientConnectionControlState.Connecting,
                _ => ClientConnectionControlState.Disconnected,
            };
        }
    }

    public long SimulatedStateReorderCount => _reorderFilter.ReorderedStateDatagramCount;

    public NetworkFaultInjectionObservationSnapshot Capture()
    {
        ThrowIfDisposed();
        return new NetworkFaultInjectionObservationSnapshot(
            NetworkProcessRole.ReplicatedClient,
            in _faultInjectionConfiguration,
            _manager.SimulatedInboundDelayedPacketCount,
            _manager.SimulatedInboundDroppedPacketCount,
            _reorderFilter.ReorderedStateDatagramCount);
    }

    public int RoundTripTimeMilliseconds
    {
        get
        {
            ThrowIfDisposed();
            return _serverPeer?.RoundTripTime ?? 0;
        }
    }

    public bool TryConnect()
    {
        ThrowIfDisposed();
        if (State != ClientConnectionControlState.Disconnected)
        {
            return false;
        }

        _serverPeer = _manager.Connect(_host, _port, _connectionKey);
        return _serverPeer != null;
    }

    public void Disconnect()
    {
        ThrowIfDisposed();
        _serverPeer?.Disconnect();
    }

    public void Pump()
    {
        ThrowIfDisposed();
        _reorderFilter.BeginPump(Environment.TickCount64);
        _manager.PollEvents();
        _reorderFilter.FlushExpired(_inbound);
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

        _serverPeer.Send(payload, channelId.Value, ResolveDeliveryMethod(channelId.Value));
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
        _reorderFilter.DiscardConnection(1);
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
        DeliveryMethod expected = ResolveDeliveryMethod(channelNumber);
        if (deliveryMethod != expected)
        {
            throw new InvalidOperationException(
                $"Unexpected delivery method {deliveryMethod} on channel {channelNumber}; expected {expected}.");
        }

        _reorderFilter.Enqueue(
            connection: 1,
            channelNumber,
            reader.GetRemainingBytesSpan(),
            _inbound);
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
        if (_disposed) throw new ObjectDisposedException(nameof(LiteNetLibClientDatagramPort));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.Stop();
    }
}
