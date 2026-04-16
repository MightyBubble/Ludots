using System;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PrefabFinalizedLeafBuffer
    {
        private PrefabFinalizedLeaf[] _items;

        public PrefabFinalizedLeafBuffer(int capacity = 32)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new PrefabFinalizedLeaf[capacity];
        }

        public int Count { get; private set; }

        public void Clear()
        {
            Count = 0;
        }

        public void Add(in PrefabFinalizedLeaf leaf)
        {
            EnsureCapacity(Count + 1);
            _items[Count++] = leaf;
        }

        public void Replace(int index, in PrefabFinalizedLeaf leaf)
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            _items[index] = leaf;
        }

        public ReadOnlySpan<PrefabFinalizedLeaf> GetSpan()
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
