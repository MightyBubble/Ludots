using System;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

internal sealed class FixedDatagramQueue
{
    private readonly byte[][] _payloads;
    private readonly int[] _lengths;
    private readonly int[] _connectionValues;
    private readonly byte[] _channels;
    private int _head;
    private int _count;

    public FixedDatagramQueue(int capacity, int maxPayloadBytes)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maxPayloadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

        _payloads = new byte[capacity][];
        _lengths = new int[capacity];
        _connectionValues = new int[capacity];
        _channels = new byte[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _payloads[i] = new byte[maxPayloadBytes];
        }
    }

    public void Enqueue(int connectionValue, byte channel, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > _payloads[0].Length)
        {
            throw new InvalidOperationException(
                $"Datagram payload {payload.Length} exceeds configured maximum {_payloads[0].Length} bytes.");
        }

        if (_count == _payloads.Length)
        {
            throw new InvalidOperationException(
                $"Datagram receive capacity {_payloads.Length} is exhausted.");
        }

        int tail = (_head + _count) % _payloads.Length;
        payload.CopyTo(_payloads[tail]);
        _lengths[tail] = payload.Length;
        _connectionValues[tail] = connectionValue;
        _channels[tail] = channel;
        _count++;
    }

    public bool TryDequeue(
        Span<byte> destination,
        out int bytesReceived,
        out int connectionValue,
        out byte channel)
    {
        if (_count == 0)
        {
            bytesReceived = 0;
            connectionValue = 0;
            channel = 0;
            return false;
        }

        int length = _lengths[_head];
        if (length > destination.Length)
        {
            throw new ArgumentException(
                $"Caller buffer {destination.Length} is smaller than pending datagram {length}.",
                nameof(destination));
        }

        _payloads[_head].AsSpan(0, length).CopyTo(destination);
        bytesReceived = length;
        connectionValue = _connectionValues[_head];
        channel = _channels[_head];
        _head = (_head + 1) % _payloads.Length;
        _count--;
        return true;
    }
}

internal sealed class FixedServerConnectionEventQueue
{
    private readonly ServerConnectionEvent[] _items;
    private int _head;
    private int _count;

    public FixedServerConnectionEventQueue(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new ServerConnectionEvent[capacity];
    }

    public void Enqueue(in ServerConnectionEvent value)
    {
        if (_count == _items.Length)
        {
            throw new InvalidOperationException($"Connection event capacity {_items.Length} is exhausted.");
        }

        _items[(_head + _count) % _items.Length] = value;
        _count++;
    }

    public bool TryDequeue(out ServerConnectionEvent value)
    {
        if (_count == 0)
        {
            value = default;
            return false;
        }

        value = _items[_head];
        _head = (_head + 1) % _items.Length;
        _count--;
        return true;
    }
}

internal sealed class FixedClientConnectionEventQueue
{
    private readonly ClientConnectionEvent[] _items;
    private int _head;
    private int _count;

    public FixedClientConnectionEventQueue(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new ClientConnectionEvent[capacity];
    }

    public void Enqueue(in ClientConnectionEvent value)
    {
        if (_count == _items.Length)
        {
            throw new InvalidOperationException($"Connection event capacity {_items.Length} is exhausted.");
        }

        _items[(_head + _count) % _items.Length] = value;
        _count++;
    }

    public bool TryDequeue(out ClientConnectionEvent value)
    {
        if (_count == 0)
        {
            value = default;
            return false;
        }

        value = _items[_head];
        _head = (_head + 1) % _items.Length;
        _count--;
        return true;
    }
}
