using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Rendering
{
    public enum SlashRibbonShape : byte
    {
        Arc = 0,
        Segment = 1,
    }

    public struct SlashRibbonItem
    {
        public SlashRibbonShape Shape;
        public Vector3 Origin;
        public float Rotation;
        public float Radius;
        public float Span;
        public float Length;
        public float Width;
        public float Height;
        public Vector4 FillColor;
        public Vector4 EdgeColor;
    }

    /// <summary>
    /// Per-frame buffer for stylized melee slash ribbons.
    /// Stores procedural parameters rather than mesh instances so adapters can render curved trails directly.
    /// </summary>
    public sealed class SlashRibbonBuffer
    {
        private readonly SlashRibbonItem[] _items;
        private int _count;

        public int Count => _count;
        public int Capacity => _items.Length;

        public SlashRibbonBuffer(int capacity = 256)
        {
            if (capacity <= 0) capacity = 256;
            _items = new SlashRibbonItem[capacity];
        }

        public bool TryAdd(in SlashRibbonItem item)
        {
            if (_count >= _items.Length)
            {
                return false;
            }

            _items[_count++] = item;
            return true;
        }

        public ReadOnlySpan<SlashRibbonItem> GetSpan() => new(_items, 0, _count);

        public void Clear() => _count = 0;
    }
}
