using System;
using System.Net;
using System.Net.Sockets;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Fixed-capacity datagram transport. Ownership and disposal are explicit.
/// Hot-path receive/send must not allocate after warmup.
/// </summary>
public interface IPhysics3DNetDatagramTransport : IDisposable
{
    bool IsBound { get; }
    bool IsDisposed { get; }
    int EndpointCapacity { get; }
    int MaxDatagramPayloadBytes { get; }

    void Bind(IPEndPoint localEndPoint);

    /// <summary>
    /// Registers a remote endpoint into a fixed slot. Returns the assigned slot.
    /// </summary>
    int RegisterRemoteEndpoint(IPEndPoint remoteEndPoint);

    void UnregisterEndpoint(int endpointSlot);

    bool TryGetEndpoint(int endpointSlot, out IPEndPoint endpoint);

    /// <summary>
    /// Copies one datagram into <paramref name="destination"/> and resolves the source endpoint slot.
    /// Returns false when the socket has no pending datagram.
    /// </summary>
    bool TryReceive(Span<byte> destination, out int bytesReceived, out int endpointSlot);

    void Send(int endpointSlot, ReadOnlySpan<byte> payload);
}

/// <summary>
/// System.Net.Sockets UDP transport with fixed receive/send buffers and endpoint registry.
/// </summary>
public sealed class Physics3DNetUdpDatagramTransport : IPhysics3DNetDatagramTransport
{
    private readonly int _endpointCapacity;
    private readonly int _maxDatagramPayloadBytes;
    private readonly byte[] _receiveBuffer;
    private readonly byte[] _sendBuffer;
    private readonly SocketAddress[] _remoteAddresses;
    private readonly IPEndPoint[] _remoteEndPoints;
    private readonly bool[] _remoteOccupied;
    private readonly SocketAddress _receiveAddress;

    private Socket? _socket;
    private bool _disposed;

    public Physics3DNetUdpDatagramTransport(Physics3DNetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        _endpointCapacity = config.TransportEndpointCapacity;
        _maxDatagramPayloadBytes = config.MaxDatagramPayloadBytes;
        _receiveBuffer = new byte[config.DatagramReceiveBufferBytes];
        _sendBuffer = new byte[config.DatagramSendBufferBytes];
        _remoteAddresses = new SocketAddress[_endpointCapacity];
        _remoteEndPoints = new IPEndPoint[_endpointCapacity];
        _remoteOccupied = new bool[_endpointCapacity];
        _receiveAddress = new SocketAddress(AddressFamily.InterNetwork, SocketAddress.GetMaximumAddressSize(AddressFamily.InterNetworkV6));
    }

    public bool IsBound => _socket is not null && !_disposed;
    public bool IsDisposed => _disposed;
    public int EndpointCapacity => _endpointCapacity;
    public int MaxDatagramPayloadBytes => _maxDatagramPayloadBytes;
    public IPEndPoint? LocalEndPoint => _socket?.LocalEndPoint as IPEndPoint;

    public void Bind(IPEndPoint localEndPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(localEndPoint);
        if (_socket is not null)
        {
            throw new InvalidOperationException("Transport is already bound. Dispose and create a new instance to rebind.");
        }

        var socket = new Socket(localEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Blocking = false;
            socket.Bind(localEndPoint);
            _socket = socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public int RegisterRemoteEndpoint(IPEndPoint remoteEndPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        for (int i = 0; i < _endpointCapacity; i++)
        {
            if (_remoteOccupied[i]
                && _remoteEndPoints[i].Address.Equals(remoteEndPoint.Address)
                && _remoteEndPoints[i].Port == remoteEndPoint.Port)
            {
                return i;
            }
        }

        for (int i = 0; i < _endpointCapacity; i++)
        {
            if (_remoteOccupied[i])
            {
                continue;
            }

            _remoteEndPoints[i] = new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port);
            _remoteAddresses[i] = remoteEndPoint.Serialize();
            _remoteOccupied[i] = true;
            return i;
        }

        throw new Physics3DNetCapacityExceededException("transport endpoint registry", _endpointCapacity, tick: 0);
    }

    public void UnregisterEndpoint(int endpointSlot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateEndpointSlot(endpointSlot);
        if (!_remoteOccupied[endpointSlot])
        {
            throw new InvalidOperationException($"Endpoint slot {endpointSlot} is not registered.");
        }

        _remoteOccupied[endpointSlot] = false;
        _remoteAddresses[endpointSlot] = null!;
        _remoteEndPoints[endpointSlot] = null!;
    }

    public bool TryGetEndpoint(int endpointSlot, out IPEndPoint endpoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)endpointSlot >= (uint)_endpointCapacity || !_remoteOccupied[endpointSlot])
        {
            endpoint = null!;
            return false;
        }

        endpoint = _remoteEndPoints[endpointSlot];
        return true;
    }

    public bool TryReceive(Span<byte> destination, out int bytesReceived, out int endpointSlot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Socket socket = RequireBoundSocket();
        if (destination.Length < _maxDatagramPayloadBytes)
        {
            throw new Physics3DNetCapacityExceededException(
                "transport receive destination",
                destination.Length,
                tick: 0);
        }

        bytesReceived = 0;
        endpointSlot = -1;

        int received;
        try
        {
            received = socket.ReceiveFrom(_receiveBuffer.AsSpan(0, _maxDatagramPayloadBytes), SocketFlags.None, _receiveAddress);
        }
        catch (SocketException ex) when (IsWouldBlock(ex))
        {
            return false;
        }

        if (received <= 0)
        {
            return false;
        }

        if (received > destination.Length)
        {
            throw new Physics3DNetCapacityExceededException("transport receive destination", destination.Length, tick: 0);
        }

        endpointSlot = ResolveOrRegisterReceiveEndpoint();
        _receiveBuffer.AsSpan(0, received).CopyTo(destination);
        bytesReceived = received;
        return true;
    }

    public void Send(int endpointSlot, ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Socket socket = RequireBoundSocket();
        ValidateEndpointSlot(endpointSlot);
        if (!_remoteOccupied[endpointSlot])
        {
            throw new InvalidOperationException($"Endpoint slot {endpointSlot} is not registered.");
        }

        if (payload.Length == 0)
        {
            throw new ArgumentException("Payload must not be empty.", nameof(payload));
        }

        if (payload.Length > _maxDatagramPayloadBytes)
        {
            throw new Physics3DNetCapacityExceededException("datagram payload", _maxDatagramPayloadBytes, tick: 0);
        }

        if (payload.Length > _sendBuffer.Length)
        {
            throw new Physics3DNetCapacityExceededException("transport send buffer", _sendBuffer.Length, tick: 0);
        }

        payload.CopyTo(_sendBuffer);
        int sent = socket.SendTo(_sendBuffer.AsSpan(0, payload.Length), SocketFlags.None, _remoteAddresses[endpointSlot]);
        if (sent != payload.Length)
        {
            throw new InvalidOperationException(
                $"UDP SendTo wrote {sent} bytes but payload length was {payload.Length}.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _socket?.Dispose();
        _socket = null;
    }

    private int ResolveOrRegisterReceiveEndpoint()
    {
        for (int i = 0; i < _endpointCapacity; i++)
        {
            if (_remoteOccupied[i] && SocketAddressEquals(_remoteAddresses[i], _receiveAddress))
            {
                return i;
            }
        }

        for (int i = 0; i < _endpointCapacity; i++)
        {
            if (_remoteOccupied[i])
            {
                continue;
            }

            var endPoint = (IPEndPoint)new IPEndPoint(
                _receiveAddress.Family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any,
                0).Create(_receiveAddress);
            _remoteEndPoints[i] = endPoint;
            var stored = new SocketAddress(_receiveAddress.Family, _receiveAddress.Size);
            _receiveAddress.Buffer.Slice(0, _receiveAddress.Size).CopyTo(stored.Buffer);
            _remoteAddresses[i] = stored;
            _remoteOccupied[i] = true;
            return i;
        }

        throw new Physics3DNetCapacityExceededException("transport endpoint registry", _endpointCapacity, tick: 0);
    }

    private Socket RequireBoundSocket()
    {
        if (_socket is null)
        {
            throw new InvalidOperationException("Transport is not bound.");
        }

        return _socket;
    }

    private void ValidateEndpointSlot(int endpointSlot)
    {
        if ((uint)endpointSlot >= (uint)_endpointCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(endpointSlot), endpointSlot, "Endpoint slot out of range.");
        }
    }

    private static bool IsWouldBlock(SocketException ex) =>
        ex.SocketErrorCode is SocketError.WouldBlock
            or SocketError.TimedOut
            or SocketError.Interrupted;

    private static bool SocketAddressEquals(SocketAddress left, SocketAddress right)
    {
        if (left.Size != right.Size)
        {
            return false;
        }

        ReadOnlySpan<byte> leftSpan = left.Buffer.Span.Slice(0, left.Size);
        ReadOnlySpan<byte> rightSpan = right.Buffer.Span.Slice(0, right.Size);
        return leftSpan.SequenceEqual(rightSpan);
    }
}
