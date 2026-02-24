using System;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class WorldHudBatchBuffer
    {
        private readonly WorldHudItem[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public WorldHudBatchBuffer(int capacity = 16384)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new WorldHudItem[capacity];
        }

        public bool TryAdd(in WorldHudItem item)
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

        public ReadOnlySpan<WorldHudItem> GetSpan() => new ReadOnlySpan<WorldHudItem>(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
