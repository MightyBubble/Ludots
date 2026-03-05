using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    public enum ScreenOverlayItemKind : byte
    {
        Text = 0,
        Rect = 1,
    }

    public struct ScreenOverlayItem
    {
        public ScreenOverlayItemKind Kind;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int FontSize;
        public int StringId;
        public Vector4 Color;
        public Vector4 BackgroundColor;
    }

    /// <summary>
    /// Per-frame screen-space overlay buffer.
    /// Produced by mod/presentation systems and consumed by platform adapter.
    /// </summary>
    public sealed class ScreenOverlayBuffer
    {
        public const int MaxItems = 128;
        public const int MaxStrings = 128;

        private readonly ScreenOverlayItem[] _items = new ScreenOverlayItem[MaxItems];
        private readonly string[] _strings = new string[MaxStrings];
        private int _count;
        private int _stringCount;

        public int Count => _count;

        public ReadOnlySpan<ScreenOverlayItem> GetSpan() => _items.AsSpan(0, _count);

        public void Clear()
        {
            _count = 0;
            _stringCount = 0;
        }

        public string? GetString(int id)
        {
            if ((uint)id >= (uint)_stringCount) return null;
            return _strings[id];
        }

        public bool AddText(int x, int y, string text, int fontSize, Vector4 color)
        {
            if (_count >= MaxItems) return false;
            int stringId = RegisterString(text);
            if (stringId < 0) return false;

            _items[_count++] = new ScreenOverlayItem
            {
                Kind = ScreenOverlayItemKind.Text,
                X = x,
                Y = y,
                FontSize = fontSize,
                StringId = stringId,
                Color = color
            };
            return true;
        }

        public bool AddRect(int x, int y, int width, int height, Vector4 fill, Vector4 border)
        {
            if (_count >= MaxItems) return false;

            _items[_count++] = new ScreenOverlayItem
            {
                Kind = ScreenOverlayItemKind.Rect,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                BackgroundColor = fill,
                Color = border
            };
            return true;
        }

        private int RegisterString(string text)
        {
            if (_stringCount >= MaxStrings) return -1;
            int id = _stringCount;
            _strings[_stringCount++] = text;
            return id;
        }
    }
}
