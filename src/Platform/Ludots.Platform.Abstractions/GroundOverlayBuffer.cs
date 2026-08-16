using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public enum GroundOverlayShape : byte
    {
        Circle = 0,
        Cone   = 1,
        Line   = 2,
        Ring   = 3,
    }

    public struct GroundOverlayItem
    {
        public int StableId;
        public GroundOverlayShape Shape;
        public Vector3 Center;       // world-space (meters)
        public float   Radius;       // for Circle/Cone/Ring
        public float   InnerRadius;  // for Ring
        public float   Angle;        // cone half-angle (radians)
        public float   Rotation;     // cone/line direction (radians, 0 = +X)
        public float   Length;       // line length
        public float   Width;        // line width
        public Vector4 FillColor;
        public Vector4 BorderColor;
        public float   BorderWidth;
    }

    /// <summary>
    /// Per-frame buffer for ground-projected overlay shapes (range circles, cones, lines).
    /// Cleared each frame; presentation systems write, renderer reads.
    /// </summary>
    public sealed class GroundOverlayBuffer
    {
        private readonly GroundOverlayItem[] _items;
        private readonly Dictionary<int, int> _retainedIndexByStableId = new();
        private int _count;
        private int _transientCount;

        public int Count => _count;
        public int Capacity => _items.Length;

        public GroundOverlayBuffer(int capacity = 256)
        {
            if (capacity <= 0) capacity = 256;
            _items = new GroundOverlayItem[capacity];
        }

        public bool TryAdd(in GroundOverlayItem item)
        {
            if (_count >= _items.Length) return false;
            _items[_count++] = item;
            if (item.StableId <= 0)
            {
                _transientCount++;
            }

            return true;
        }

        public bool Upsert(in GroundOverlayItem item)
        {
            if (item.StableId <= 0)
            {
                return TryAdd(in item);
            }

            if (_retainedIndexByStableId.TryGetValue(item.StableId, out int existingIndex))
            {
                _items[existingIndex] = item;
                return true;
            }

            if (_count >= _items.Length)
            {
                return false;
            }

            int index = _count++;
            _items[index] = item;
            _retainedIndexByStableId[item.StableId] = index;
            return true;
        }

        public void Remove(int stableId)
        {
            if (stableId <= 0 || !_retainedIndexByStableId.TryGetValue(stableId, out int index))
            {
                return;
            }

            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                GroundOverlayItem moved = _items[lastIndex];
                _items[index] = moved;
                if (moved.StableId > 0)
                {
                    _retainedIndexByStableId[moved.StableId] = index;
                }
            }

            _count = lastIndex;
            _retainedIndexByStableId.Remove(stableId);
        }

        public void ClearTransient()
        {
            if (_transientCount == 0)
            {
                return;
            }

            for (int index = _count - 1; index >= 0; index--)
            {
                if (_items[index].StableId > 0)
                {
                    continue;
                }

                RemoveAt(index);
            }

            _transientCount = 0;
        }

        private void RemoveAt(int index)
        {
            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                GroundOverlayItem moved = _items[lastIndex];
                _items[index] = moved;
                if (moved.StableId > 0)
                {
                    _retainedIndexByStableId[moved.StableId] = index;
                }
            }

            _count = lastIndex;
        }

        public ReadOnlySpan<GroundOverlayItem> GetSpan() => new(_items, 0, _count);

        public void Clear()
        {
            _count = 0;
            _transientCount = 0;
            _retainedIndexByStableId.Clear();
        }
    }
}
