using System;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PrefabFinalizedVisualBuffer
    {
        private PrefabFinalizedVisual[] _items;

        public PrefabFinalizedVisualBuffer(int capacity = 32)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _items = new PrefabFinalizedVisual[capacity];
        }

        public int Count { get; private set; }

        public void Clear()
        {
            Count = 0;
        }

        public void Add(in PrefabFinalizedVisual visual)
        {
            EnsureCapacity(Count + 1);
            _items[Count++] = visual;
        }

        public ReadOnlySpan<PrefabFinalizedVisual> GetSpan()
        {
            return _items.AsSpan(0, Count);
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _items.Length)
            {
                return;
            }

            int nextSize = Math.Max(required, _items.Length * 2);
            Array.Resize(ref _items, nextSize);
        }
    }
}
