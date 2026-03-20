using System;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class SkinnedVisualBatchBuffer
    {
        private readonly SkinnedVisualBatchItem[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public SkinnedVisualBatchBuffer(int capacity = 4096)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new SkinnedVisualBatchItem[capacity];
        }

        public bool TryAdd(in SkinnedVisualBatchItem item)
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

        public ReadOnlySpan<SkinnedVisualBatchItem> GetSpan() => new ReadOnlySpan<SkinnedVisualBatchItem>(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
