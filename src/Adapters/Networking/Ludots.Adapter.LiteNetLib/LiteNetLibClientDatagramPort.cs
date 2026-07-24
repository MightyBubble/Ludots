using System;
using global::LiteNetLib;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

public sealed class LiteNetLibClientDatagramPort :
    IClientDatagramPort,
    IClientConnectionEventPort,
    IClientConnectionControlPort,
    IDisposable
{
    private readonly EventBasedNetListener _listener;
    private readonly NetManager _manager;
    private readonly FixedDatagramQueue _inbound;
    private readonly FixedClientConnectionEventQueue _connectionEvents;
    private readonly LiteNetLibChannelDeliveryContract _delivery;
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
        int maxConnectAttempts,
        int disconnectTimeoutMilliseconds,
        int channelCount,
        ChannelId controlChannel,
        ChannelId commandChannel,
        ChannelId stateChannel,
        ChannelId inputChannel)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
        if ((uint)port > ushort.MaxValue || port == 0) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(connectionKey)) throw new ArgumentException("Connection key is required.", nameof(connectionKey));
        if (maxConnectAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxConnectAttempts));
        if (disconnectTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(disconnectTimeoutMilliseconds));
        if ((uint)(channelCount - 1) >= 64u) throw new ArgumentOutOfRangeException(nameof(channelCount));

        _inbound = new FixedDatagramQueue(datagramCapacity, maxPayloadBytes);
        _connectionEvents = new FixedClientConnectionEventQueue(connectionEventCapacity);
        _host = host;
        _port = port;
        _connectionKey = connectionKey;
        _delivery = new LiteNetLibChannelDeliveryContract(
            channelCount,
            controlChannel,
            commandChannel,
            stateChannel,
            inputChannel);
        _listener = new EventBasedNetListener();
        _manager = new NetManager(_listener)
        {
            AutoRecycle = true,
            MaxConnectAttempts = maxConnectAttempts,
            DisconnectTimeout = disconnectTimeoutMilliseconds,
            ChannelsCount = (byte)channelCount,
        };

        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;

        if (!_manager.Start())
        {
            throw new InvalidOperationException("LiteNetLib client failed to start its UDP endpoint.");
        }
    }

    /// <summary>
    /// Local UDP port bound by this LiteNetLib client endpoint after construction.
    /// Distinct instances must bind distinct ports; used as load-host uniqueness evidence.
    /// </summary>
    public int BoundPort => _manager.LocalPort;

    public ClientConnectionControlState State
    {
        get
        {
            ThrowIfDisposed();
            if (_serverPeer == null)
            {
                return ClientConnectionControlState.Disconnected;
            }

            return _serverPeer.ConnectionState switch
            {
                ConnectionState.Disconnected => ClientConnectionControlState.Disconnected,
                ConnectionState.Outgoing => ClientConnectionControlState.Connecting,
                ConnectionState.Connected => ClientConnectionControlState.Connected,
                ConnectionState.ShutdownRequested => ClientConnectionControlState.Connected,
                _ => throw new InvalidOperationException(
                    $"Unsupported LiteNetLib connection state {_serverPeer.ConnectionState}."),
            };
        }
    }

    public bool TryConnect()
    {
        ThrowIfDisposed();
        if (State != ClientConnectionControlState.Disconnected)
        {
            return false;
        }

        _serverPeer = _manager.Connect(_host, _port, _connectionKey)
            ?? throw new InvalidOperationException(
                $"LiteNetLib client failed to start connection to {_host}:{_port}.");
        return true;
    }

    public void Disconnect()
    {
        ThrowIfDisposed();
        if (_serverPeer == null || _serverPeer.ConnectionState == ConnectionState.Disconnected)
        {
            throw new InvalidOperationException("LiteNetLib client has no active connection to disconnect.");
        }

        _serverPeer.Disconnect();
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

        _serverPeer.Send(payload, channelId.Value, _delivery.GetExpected(channelId.Value));
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
        _delivery.ValidateReceived(channelNumber, deliveryMethod);
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
