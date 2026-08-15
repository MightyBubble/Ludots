using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class PrimitiveDrawBuffer
    {
        public const string OverflowErrorCode = "PRIMITIVE_DRAW_BUFFER_OVERFLOW";

        private readonly PrimitiveDrawItem[] _buffer;
        private readonly Dictionary<int, int> _staticSlotByStableId = new();
        private PrimitiveDrawItem[] _staticMeshDeltaItems = Array.Empty<PrimitiveDrawItem>();
        private int[] _staticMeshRemovedStableIds = Array.Empty<int>();
        private int _count;
        private int _revision;
        private int _projectionGeneration;
        private int _staticMeshGeometryRevision;
        private int _staticMeshDeltaBaseRevision;
        private int _staticMeshLaneItemCount;
        private int _skinnedLaneItemCount;
        private int _staticMeshDeltaItemCount;
        private int _staticMeshRemovedStableIdCount;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int Revision => _revision;
        public int ProjectionGeneration => _projectionGeneration;
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

            Insert(in item);
            return true;
        }

        public void Add(in PrimitiveDrawItem item)
        {
            if (_count >= _buffer.Length)
            {
                throw CreateOverflowException(item.StableId, item.RenderPath);
            }

            Insert(in item);
        }

        public InvalidOperationException CreateOverflowException(int stableId, VisualRenderPath renderPath)
        {
            return new InvalidOperationException(
                $"PrimitiveDrawBuffer overflowed ({OverflowErrorCode}) while adding stableId={stableId}, renderPath={renderPath}; capacity={Capacity}, count={_count}.");
        }

        private void Insert(in PrimitiveDrawItem item)
        {
            int slot = _count++;
            _buffer[slot] = item;
            if (item.RenderPath.IsStaticInstanceLane())
            {
                _staticMeshLaneItemCount++;
                _staticSlotByStableId[item.StableId] = slot;
            }
            else if (item.RenderPath.IsSkinnedLane())
            {
                _skinnedLaneItemCount++;
            }
        }

        public void ApplyStaticMeshDelta(
            ReadOnlySpan<PrimitiveDrawItem> changedItems,
            ReadOnlySpan<int> removedStableIds,
            bool visibleOnly = false)
        {
            for (int i = 0; i < removedStableIds.Length; i++)
            {
                RemoveStaticMeshInstance(removedStableIds[i]);
            }

            for (int i = 0; i < changedItems.Length; i++)
            {
                ref readonly PrimitiveDrawItem item = ref changedItems[i];
                if (!item.RenderPath.IsStaticInstanceLane())
                {
                    throw new InvalidOperationException(
                        $"ApplyStaticMeshDelta received a non-static-instance-lane item stableId={item.StableId}, renderPath={item.RenderPath}.");
                }

                bool keep = !visibleOnly || item.Visibility == VisualVisibility.Visible;
                if (_staticSlotByStableId.TryGetValue(item.StableId, out int slot))
                {
                    if (keep)
                    {
                        _buffer[slot] = item;
                    }
                    else
                    {
                        RemoveStaticMeshInstance(item.StableId);
                    }
                }
                else if (keep)
                {
                    Add(in item);
                }
            }
        }

        private void RemoveStaticMeshInstance(int stableId)
        {
            if (stableId <= 0 || !_staticSlotByStableId.TryGetValue(stableId, out int slot))
            {
                return;
            }

            _staticSlotByStableId.Remove(stableId);
            _staticMeshLaneItemCount--;
            int last = _count - 1;
            if (slot != last)
            {
                PrimitiveDrawItem moved = _buffer[last];
                _buffer[slot] = moved;
                if (moved.RenderPath.IsStaticInstanceLane())
                {
                    _staticSlotByStableId[moved.StableId] = slot;
                }
            }

            _buffer[last] = default;
            _count = last;
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

        public void SetProjectionGeneration(int generation)
        {
            _projectionGeneration = generation;
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
            _staticSlotByStableId.Clear();
            _staticMeshLaneItemCount = 0;
            _skinnedLaneItemCount = 0;
            _staticMeshDeltaBaseRevision = _revision;
            _staticMeshDeltaItemCount = 0;
            _staticMeshRemovedStableIdCount = 0;
            DroppedSinceClear = 0;
        }
    }
}
