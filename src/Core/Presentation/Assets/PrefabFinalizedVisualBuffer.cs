using System;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PrefabFinalizedVisualBuffer
    {
        private PrefabFinalizedVisual[] _items;
        private int _count;

        public PrefabFinalizedVisualBuffer(int capacity = 32)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new PrefabFinalizedVisual[capacity];
        }

        public int Count => _count;

        public void Clear() => _count = 0;

        public void Add(in PrefabFinalizedVisual visual)
        {
            if (_count >= _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            _items[_count++] = visual;
        }

        public ReadOnlySpan<PrefabFinalizedVisual> GetSpan()
            => new ReadOnlySpan<PrefabFinalizedVisual>(_items, 0, _count);
    }
}
