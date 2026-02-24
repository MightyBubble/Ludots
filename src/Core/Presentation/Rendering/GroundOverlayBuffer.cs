using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Rendering
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
        private int _count;

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
            return true;
        }

        public ReadOnlySpan<GroundOverlayItem> GetSpan() => new(_items, 0, _count);

        public void Clear() => _count = 0;
    }
}
