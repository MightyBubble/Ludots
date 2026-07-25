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
        private int _preparedCount;
        private bool _preparing;

        public FixedServerDatagramSendQueue(int capacity, int maxPayloadBytes)
        {
            _maxPayloadBytes = maxPayloadBytes;
            _connections = new int[capacity];
            _channels = new byte[capacity];
            _lengths = new int[capacity];
            _payloads = new byte[checked(capacity * maxPayloadBytes)];
        }

        public int Count => _count;
        public int Capacity => _connections.Length;
        public int AvailableCapacity => Capacity - _count - _preparedCount;

        public bool TryEnqueue(ConnectionId connection, ChannelId channel, ReadOnlySpan<byte> payload)
        {
            if (_preparing || _count == Capacity || payload.Length > _maxPayloadBytes)
            {
                return false;
            }

            int slot = (_head + _count) % Capacity;
            _connections[slot] = connection.Value;
            _channels[slot] = channel.Value;
            _lengths[slot] = payload.Length;
            payload.CopyTo(_payloads.AsSpan(slot * _maxPayloadBytes, payload.Length));
            _count++;
            return true;
        }

        public bool TryBeginPreparedBatch()
        {
            if (_preparing)
            {
                return false;
            }

            _preparing = true;
            _preparedCount = 0;
            return true;
        }

        public bool TryEnqueuePrepared(ConnectionId connection, ChannelId channel, ReadOnlySpan<byte> payload)
        {
            if (!_preparing || AvailableCapacity == 0 || payload.Length > _maxPayloadBytes)
            {
                return false;
            }

            int slot = (_head + _count + _preparedCount) % Capacity;
            _connections[slot] = connection.Value;
            _channels[slot] = channel.Value;
            _lengths[slot] = payload.Length;
            payload.CopyTo(_payloads.AsSpan(slot * _maxPayloadBytes, payload.Length));
            _preparedCount++;
            return true;
        }

        public void CommitPreparedBatch()
        {
            if (!_preparing)
            {
                throw new InvalidOperationException("The server send queue has no prepared batch to commit.");
            }

            _count += _preparedCount;
            _preparedCount = 0;
            _preparing = false;
        }

        public void CancelPreparedBatch()
        {
            if (!_preparing)
            {
                return;
            }

            for (int index = 0; index < _preparedCount; index++)
            {
                int slot = (_head + _count + index) % Capacity;
                _lengths[slot] = 0;
            }

            _preparedCount = 0;
            _preparing = false;
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
