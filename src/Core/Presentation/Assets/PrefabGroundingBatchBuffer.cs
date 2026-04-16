using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    internal sealed class PrefabGroundingBatchBuffer
    {
        private PrefabGroundingRequest[] _items;

        public PrefabGroundingBatchBuffer(int capacity = 32)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new PrefabGroundingRequest[capacity];
        }

        public int Count { get; private set; }

        public void Clear()
        {
            Count = 0;
        }

        public int Add(in PrefabGroundingRequest item)
        {
            EnsureCapacity(Count + 1);
            _items[Count] = item;
            return Count++;
        }

        public ref PrefabGroundingRequest this[int index] => ref _items[index];

        public ReadOnlySpan<PrefabGroundingRequest> GetSpan() => _items.AsSpan(0, Count);

        private void EnsureCapacity(int required)
        {
            if (required <= _items.Length)
            {
                return;
            }

            int next = Math.Max(required, _items.Length * 2);
            Array.Resize(ref _items, next);
        }
    }

    internal struct PrefabGroundingRequest
    {
        public int MeshAssetId;
        public int StableId;
        public PrefabPartGrounding Grounding;
        public Vector3 Position;
        public Quaternion Rotation;
    }
}
