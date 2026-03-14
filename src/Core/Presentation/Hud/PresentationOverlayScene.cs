using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationOverlayScene
    {
        private readonly PresentationOverlayItem[] _items;
        private int _count;

        public PresentationOverlayScene(int capacity = 32768)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _items = new PresentationOverlayItem[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public ReadOnlySpan<PresentationOverlayItem> GetSpan() => new(_items, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }

        public bool ContainsLayer(PresentationOverlayLayer layer)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].Layer == layer)
                {
                    return true;
                }
            }

            return false;
        }

        public bool Contains(PresentationOverlayLayer layer, PresentationOverlayItemKind kind)
        {
            for (int i = 0; i < _count; i++)
            {
                ref readonly PresentationOverlayItem item = ref _items[i];
                if (item.Layer == layer && item.Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryAddText(
            PresentationOverlayLayer layer,
            float x,
            float y,
            string text,
            int fontSize,
            in Vector4 color)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (!TryReserve(out int index))
            {
                return false;
            }

            _items[index] = new PresentationOverlayItem
            {
                Kind = PresentationOverlayItemKind.Text,
                Layer = layer,
                X = x,
                Y = y,
                FontSize = fontSize,
                Text = text,
                Color0 = color,
            };

            return true;
        }

        public bool TryAddRect(
            PresentationOverlayLayer layer,
            float x,
            float y,
            float width,
            float height,
            in Vector4 fill,
            in Vector4 border)
        {
            if (width <= 0f || height <= 0f)
            {
                return true;
            }

            if (!TryReserve(out int index))
            {
                return false;
            }

            _items[index] = new PresentationOverlayItem
            {
                Kind = PresentationOverlayItemKind.Rect,
                Layer = layer,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Color0 = fill,
                Color1 = border,
            };

            return true;
        }

        public bool TryAddBar(
            PresentationOverlayLayer layer,
            float x,
            float y,
            float width,
            float height,
            float value,
            in Vector4 background,
            in Vector4 foreground)
        {
            if (width <= 0f || height <= 0f)
            {
                return true;
            }

            if (!TryReserve(out int index))
            {
                return false;
            }

            _items[index] = new PresentationOverlayItem
            {
                Kind = PresentationOverlayItemKind.Bar,
                Layer = layer,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Value0 = value,
                Color0 = background,
                Color1 = foreground,
            };

            return true;
        }

        private bool TryReserve(out int index)
        {
            if (_count >= _items.Length)
            {
                index = -1;
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            index = _count++;
            return true;
        }
    }
}
