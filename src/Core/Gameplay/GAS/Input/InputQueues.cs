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

        public bool TryPeek(out T request)
        {
            if (_count == 0)
            {
                request = default;
                return false;
            }

            request = _items[_head];
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

    public sealed class InputRequestQueue : RingBuffer<InputRequest>
    {
        public InputRequestQueue(int capacity = 1024) : base(capacity) { }
    }
}
