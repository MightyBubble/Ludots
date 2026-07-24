using System;

namespace Ludots.Core.Networking.Transport
{
    /// <summary>
    /// Opaque adapter-assigned connection handle. Value 0 is reserved as unbound.
    /// </summary>
    public readonly struct ConnectionId : IEquatable<ConnectionId>
    {
        public ConnectionId(int value)
        {
            if (value == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Connection id 0 is reserved as unbound.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(ConnectionId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ConnectionId other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(ConnectionId left, ConnectionId right) => left.Equals(right);

        public static bool operator !=(ConnectionId left, ConnectionId right) => !left.Equals(right);
    }

    /// <summary>
    /// Logical datagram channel within a connection (reliability/ordering are adapter concerns).
    /// </summary>
    public readonly struct ChannelId : IEquatable<ChannelId>
    {
        public ChannelId(byte value)
        {
            Value = value;
        }

        public byte Value { get; }

        public bool Equals(ChannelId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ChannelId other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(ChannelId left, ChannelId right) => left.Equals(right);

        public static bool operator !=(ChannelId left, ChannelId right) => !left.Equals(right);
    }

    public enum DatagramSendStatus : byte
    {
        Sent = 0,
        NotReady = 1,
        Closed = 2,
    }

    /// <summary>
    /// Server-side transport-neutral datagram port. Core polls; the adapter owns the real endpoint and buffers.
    /// </summary>
    public interface IServerDatagramPort
    {
        /// <summary>
        /// Copies one pending datagram into <paramref name="buffer"/>. Returns false when the receive queue is empty.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the datagram does not fit in <paramref name="buffer"/>.</exception>
        bool TryReceive(Span<byte> buffer, out int bytesReceived, out ConnectionId connectionId, out ChannelId channelId);

        DatagramSendStatus TrySend(ConnectionId connectionId, ChannelId channelId, ReadOnlySpan<byte> payload);
    }

    /// <summary>
    /// Client-side transport-neutral datagram port. Core polls; the adapter owns the real endpoint and buffers.
    /// </summary>
    public interface IClientDatagramPort
    {
        /// <summary>
        /// Copies one pending datagram into <paramref name="buffer"/>. Returns false when the receive queue is empty.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the datagram does not fit in <paramref name="buffer"/>.</exception>
        bool TryReceive(Span<byte> buffer, out int bytesReceived, out ChannelId channelId);

        DatagramSendStatus TrySend(ChannelId channelId, ReadOnlySpan<byte> payload);
    }
}
