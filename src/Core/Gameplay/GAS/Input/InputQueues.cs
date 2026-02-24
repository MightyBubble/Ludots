namespace Ludots.Core.Gameplay.GAS.Input
{
    /// <summary>
    /// Generic ring buffer for request types with auto-incrementing RequestId.
    /// </summary>
    public class RingBuffer<T> where T : struct, IHasRequestId
    {
        private readonly T[] _items;
        private int _head;
        private int _tail;
        private int _count;
        private int _nextRequestId = 1;

        public RingBuffer(int capacity = 1024)
        {
            if (capacity < 16) capacity = 16;
            _items = new T[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;

        public bool TryEnqueue(in T request)
        {
            if (_count >= _items.Length) return false;
            var r = request;
            if (r.RequestId == 0) r.RequestId = _nextRequestId++;
            _items[_tail] = r;
            _tail = (_tail + 1) % _items.Length;
            _count++;
            return true;
        }

        public bool TryDequeue(out T request)
        {
            if (_count == 0)
            {
                request = default;
                return false;
            }

            request = _items[_head];
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// Generic swap-remove buffer for response types matched by RequestId.
    /// </summary>
    public class SwapRemoveBuffer<T> where T : struct, IHasRequestId
    {
        private readonly T[] _items;
        private int _count;

        public SwapRemoveBuffer(int capacity = 1024)
        {
            if (capacity < 16) capacity = 16;
            _items = new T[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;

        public bool TryAdd(in T response)
        {
            if (_count >= _items.Length) return false;
            _items[_count++] = response;
            return true;
        }

        public bool TryConsume(int requestId, out T response)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].RequestId != requestId) continue;
                response = _items[i];
                _count--;
                if (i != _count) _items[i] = _items[_count];
                return true;
            }

            response = default;
            return false;
        }

        public void Clear()
        {
            _count = 0;
        }
    }

    // ── Concrete types (backward-compatible aliases) ──

    public sealed class InputRequestQueue : RingBuffer<InputRequest>
    {
        public InputRequestQueue(int capacity = 1024) : base(capacity) { }
    }

    public sealed class InputResponseBuffer : SwapRemoveBuffer<InputResponse>
    {
        public InputResponseBuffer(int capacity = 1024) : base(capacity) { }
    }

    public sealed class SelectionRequestQueue : RingBuffer<SelectionRequest>
    {
        public SelectionRequestQueue(int capacity = 1024) : base(capacity) { }
    }

    public sealed class SelectionResponseBuffer : SwapRemoveBuffer<SelectionResponse>
    {
        public SelectionResponseBuffer(int capacity = 1024) : base(capacity) { }
    }
}
