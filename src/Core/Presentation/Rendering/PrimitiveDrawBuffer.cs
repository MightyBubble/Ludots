using System;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class PrimitiveDrawBuffer
    {
        private readonly PrimitiveDrawItem[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public PrimitiveDrawBuffer(int capacity = 8192)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new PrimitiveDrawItem[capacity];
        }

        public bool TryAdd(in PrimitiveDrawItem item)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _buffer[_count++] = item;
            return true;
        }

        public ReadOnlySpan<PrimitiveDrawItem> GetSpan() => new ReadOnlySpan<PrimitiveDrawItem>(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
