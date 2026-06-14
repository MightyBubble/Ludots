using System;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class PrimitiveDrawBuffer
    {
        private readonly PrimitiveDrawItem[] _buffer;
        private PrimitiveDrawItem[] _staticMeshDeltaItems = Array.Empty<PrimitiveDrawItem>();
        private int[] _staticMeshRemovedStableIds = Array.Empty<int>();
        private int _count;
        private int _revision;
        private int _staticMeshGeometryRevision;
        private int _staticMeshDeltaBaseRevision;
        private int _staticMeshLaneItemCount;
        private int _skinnedLaneItemCount;
        private int _staticMeshDeltaItemCount;
        private int _staticMeshRemovedStableIdCount;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int Revision => _revision;
        public int StaticMeshGeometryRevision => _staticMeshGeometryRevision;
        public int StaticMeshDeltaBaseRevision => _staticMeshDeltaBaseRevision;
        public int StaticMeshLaneItemCount => _staticMeshLaneItemCount;
        public int SkinnedLaneItemCount => _skinnedLaneItemCount;
        public int StaticMeshDeltaItemCount => _staticMeshDeltaItemCount;
        public int StaticMeshRemovedStableIdCount => _staticMeshRemovedStableIdCount;
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
            if (item.RenderPath.IsStaticInstanceLane())
            {
                _staticMeshLaneItemCount++;
            }
            else if (item.RenderPath.IsSkinnedLane())
            {
                _skinnedLaneItemCount++;
            }

            return true;
        }

        public ReadOnlySpan<PrimitiveDrawItem> GetSpan() => new ReadOnlySpan<PrimitiveDrawItem>(_buffer, 0, _count);

        public ReadOnlySpan<PrimitiveDrawItem> GetStaticMeshDeltaItems() =>
            new ReadOnlySpan<PrimitiveDrawItem>(_staticMeshDeltaItems, 0, _staticMeshDeltaItemCount);

        public ReadOnlySpan<int> GetStaticMeshRemovedStableIds() =>
            new ReadOnlySpan<int>(_staticMeshRemovedStableIds, 0, _staticMeshRemovedStableIdCount);

        public void SetRevision(int revision)
        {
            _revision = revision;
        }

        public void SetStaticMeshGeometryRevision(int revision)
        {
            _staticMeshGeometryRevision = revision;
        }

        public void SetStaticMeshDeltas(int baseRevision, ReadOnlySpan<PrimitiveDrawItem> changedItems, ReadOnlySpan<int> removedStableIds)
        {
            if (_staticMeshDeltaItems.Length < changedItems.Length)
            {
                _staticMeshDeltaItems = new PrimitiveDrawItem[changedItems.Length];
            }

            if (_staticMeshRemovedStableIds.Length < removedStableIds.Length)
            {
                _staticMeshRemovedStableIds = new int[removedStableIds.Length];
            }

            changedItems.CopyTo(_staticMeshDeltaItems);
            removedStableIds.CopyTo(_staticMeshRemovedStableIds);
            _staticMeshDeltaBaseRevision = baseRevision;
            _staticMeshDeltaItemCount = changedItems.Length;
            _staticMeshRemovedStableIdCount = removedStableIds.Length;
        }

        public void Clear()
        {
            _count = 0;
            _staticMeshLaneItemCount = 0;
            _skinnedLaneItemCount = 0;
            _staticMeshDeltaBaseRevision = _revision;
            _staticMeshDeltaItemCount = 0;
            _staticMeshRemovedStableIdCount = 0;
            DroppedSinceClear = 0;
        }
    }
}
