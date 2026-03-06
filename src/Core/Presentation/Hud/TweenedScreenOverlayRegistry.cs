using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    public struct TweenedScreenOverlayItem
    {
        public bool Active;
        public ScreenOverlayItemKind Kind;
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public int FontSize;
        public string Text;
        public Vector4 TextColor;
        public Vector4 FillColor;
        public Vector4 BorderColor;
    }

    /// <summary>
    /// Persistent screen overlay state used by presentation systems before emitting into the per-frame ScreenOverlayBuffer.
    /// </summary>
    public sealed class TweenedScreenOverlayRegistry
    {
        private readonly TweenedScreenOverlayItem[] _items;
        private readonly int[] _freeStack;
        private int _freeCount;
        private int _highWaterMark;

        public int Capacity => _items.Length;

        public TweenedScreenOverlayRegistry(int capacity = 128)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new TweenedScreenOverlayItem[capacity];
            _freeStack = new int[capacity];
        }

        public bool TryAllocateRect(int x, int y, int width, int height, Vector4 fill, Vector4 border, out int handle)
        {
            if (!TryAllocate(out handle)) return false;

            _items[handle] = new TweenedScreenOverlayItem
            {
                Active = true,
                Kind = ScreenOverlayItemKind.Rect,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                FillColor = fill,
                BorderColor = border,
                Text = string.Empty,
                FontSize = 0,
                TextColor = Vector4.One
            };
            return true;
        }

        public bool TryAllocateText(int x, int y, string text, int fontSize, Vector4 color, out int handle)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (!TryAllocate(out handle)) return false;

            _items[handle] = new TweenedScreenOverlayItem
            {
                Active = true,
                Kind = ScreenOverlayItemKind.Text,
                X = x,
                Y = y,
                Width = 0f,
                Height = 0f,
                FillColor = Vector4.Zero,
                BorderColor = Vector4.Zero,
                Text = text,
                FontSize = fontSize,
                TextColor = color
            };
            return true;
        }

        public bool IsActive(int handle)
        {
            return handle >= 0 && handle < _highWaterMark && _items[handle].Active;
        }

        public ref TweenedScreenOverlayItem Get(int handle)
        {
            return ref _items[handle];
        }

        public void Release(int handle)
        {
            if (handle < 0 || handle >= _highWaterMark) return;
            if (!_items[handle].Active) return;
            _items[handle] = default;
            if (_freeCount < _freeStack.Length)
                _freeStack[_freeCount++] = handle;
        }

        public void EmitTo(ScreenOverlayBuffer buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            for (int i = 0; i < _highWaterMark; i++)
            {
                ref readonly var item = ref _items[i];
                if (!item.Active) continue;

                switch (item.Kind)
                {
                    case ScreenOverlayItemKind.Text:
                        buffer.AddText(
                            x: (int)MathF.Round(item.X),
                            y: (int)MathF.Round(item.Y),
                            text: item.Text ?? string.Empty,
                            fontSize: item.FontSize <= 0 ? 16 : item.FontSize,
                            color: item.TextColor);
                        break;

                    case ScreenOverlayItemKind.Rect:
                        buffer.AddRect(
                            x: (int)MathF.Round(item.X),
                            y: (int)MathF.Round(item.Y),
                            width: Math.Max(0, (int)MathF.Round(item.Width)),
                            height: Math.Max(0, (int)MathF.Round(item.Height)),
                            fill: item.FillColor,
                            border: item.BorderColor);
                        break;
                }
            }
        }

        private bool TryAllocate(out int handle)
        {
            if (_freeCount > 0)
            {
                handle = _freeStack[--_freeCount];
                return true;
            }

            if (_highWaterMark < _items.Length)
            {
                handle = _highWaterMark++;
                return true;
            }

            handle = -1;
            return false;
        }
    }
}
