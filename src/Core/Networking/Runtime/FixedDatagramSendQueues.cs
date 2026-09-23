using System;
using Ludots.Core.Networking.Transport;

namespace Ludots.Core.Networking.Runtime
{
    internal sealed class FixedServerDatagramSendQueue
    {
        private readonly int _maxPayloadBytes;
        private readonly int[] _connections;
        private readonly byte[] _channels;
        private readonly int[] _lengths;
        private readonly byte[] _payloads;
        private int _head;
        private int _count;
        private int _reservedCount;

        public FixedServerDatagramSendQueue(int capacity, int maxPayloadBytes)
        {
            _maxPayloadBytes = maxPayloadBytes;
            _connections = new int[capacity];
            _channels = new byte[capacity];
            _lengths = new int[capacity];
            _payloads = new byte[checked(capacity * maxPayloadBytes)];
        }

        public int Count => _count;
        public int ReservedCount => _reservedCount;
        public int Capacity => _connections.Length;
        public int AvailableCapacity => Capacity - _count - _reservedCount;

        public bool TryEnqueue(ConnectionId connection, ChannelId channel, ReadOnlySpan<byte> payload)
        {
            if (_reservedCount != 0 || _count == Capacity || payload.Length > _maxPayloadBytes)
            {
                return false;
            }

            int slot = (_head + _count) % Capacity;
            WriteSlot(slot, connection, channel, payload);
            _count++;
            return true;
        }

        public bool TryExpandReserved(int additionalDatagrams)
        {
            if (additionalDatagrams <= 0 || additionalDatagrams > AvailableCapacity)
            {
                return false;
            }

            _reservedCount += additionalDatagrams;
            return true;
        }

        public bool TryGetReservedWriteSlot(
            int reservedIndex,
            out Span<byte> payloadDestination)
        {
            if ((uint)reservedIndex >= (uint)_reservedCount)
            {
                payloadDestination = default;
                return false;
            }

            int slot = (_head + _count + reservedIndex) % Capacity;
            payloadDestination = _payloads.AsSpan(slot * _maxPayloadBytes, _maxPayloadBytes);
            return true;
        }

        public bool TryCommitReservedWrite(
            int reservedIndex,
            ConnectionId connection,
            ChannelId channel,
            int payloadLength)
        {
            if ((uint)reservedIndex >= (uint)_reservedCount ||
                payloadLength < 0 ||
                payloadLength > _maxPayloadBytes)
            {
                return false;
            }

            int slot = (_head + _count + reservedIndex) % Capacity;
            _connections[slot] = connection.Value;
            _channels[slot] = channel.Value;
            _lengths[slot] = payloadLength;
            return true;
        }

        public bool TryWriteReserved(
            int reservedIndex,
            ConnectionId connection,
            ChannelId channel,
            ReadOnlySpan<byte> payload)
        {
            if (!TryGetReservedWriteSlot(reservedIndex, out Span<byte> destination) ||
                payload.Length > destination.Length)
            {
                return false;
            }

            payload.CopyTo(destination);
            return TryCommitReservedWrite(reservedIndex, connection, channel, payload.Length);
        }

        public void PublishReserved()
        {
            if (_reservedCount == 0)
            {
                throw new InvalidOperationException("The server send queue has no reserved batch to publish.");
            }

            _count += _reservedCount;
            _reservedCount = 0;
        }

        public void CancelReserved()
        {
            if (_reservedCount == 0)
            {
                return;
            }

            for (int index = 0; index < _reservedCount; index++)
            {
                int slot = (_head + _count + index) % Capacity;
                _lengths[slot] = 0;
                _connections[slot] = 0;
                _channels[slot] = 0;
            }

            _reservedCount = 0;
        }

        public bool TryPeek(out ConnectionId connection, out ChannelId channel, out ReadOnlySpan<byte> payload)
        {
            if (_count == 0)
            {
                connection = default;
                channel = default;
                payload = default;
                return false;
            }

            connection = new ConnectionId(_connections[_head]);
            channel = new ChannelId(_channels[_head]);
            payload = _payloads.AsSpan(_head * _maxPayloadBytes, _lengths[_head]);
            return true;
        }

        public void RemoveHead()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("The server send queue is empty.");
            }

            _lengths[_head] = 0;
            _head = (_head + 1) % Capacity;
            _count--;
        }

        private void WriteSlot(
            int slot,
            ConnectionId connection,
            ChannelId channel,
            ReadOnlySpan<byte> payload)
        {
            _connections[slot] = connection.Value;
            _channels[slot] = channel.Value;
            _lengths[slot] = payload.Length;
            payload.CopyTo(_payloads.AsSpan(slot * _maxPayloadBytes, payload.Length));
        }
    }

    internal sealed class FixedClientDatagramSendQueue
    {
        private readonly int _maxPayloadBytes;
        private readonly byte[] _channels;
        private readonly int[] _lengths;
        private readonly byte[] _payloads;
        private int _head;
        private int _count;

        public FixedClientDatagramSendQueue(int capacity, int maxPayloadBytes)
        {
            _maxPayloadBytes = maxPayloadBytes;
            _channels = new byte[capacity];
            _lengths = new int[capacity];
            _payloads = new byte[checked(capacity * maxPayloadBytes)];
        }

        public int Count => _count;
        public int Capacity => _channels.Length;

        public bool TryEnqueue(ChannelId channel, ReadOnlySpan<byte> payload)
        {
            if (_count == Capacity || payload.Length > _maxPayloadBytes)
            {
                return false;
            }

            int slot = (_head + _count) % Capacity;
            _channels[slot] = channel.Value;
            _lengths[slot] = payload.Length;
            payload.CopyTo(_payloads.AsSpan(slot * _maxPayloadBytes, payload.Length));
            _count++;
            return true;
        }

        public bool TryPeek(out ChannelId channel, out ReadOnlySpan<byte> payload)
        {
            if (_count == 0)
            {
                channel = default;
                payload = default;
                return false;
            }

            channel = new ChannelId(_channels[_head]);
            payload = _payloads.AsSpan(_head * _maxPayloadBytes, _lengths[_head]);
            return true;
        }

        public void RemoveHead()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("The client send queue is empty.");
            }

            _lengths[_head] = 0;
            _head = (_head + 1) % Capacity;
            _count--;
        }
    }
}
