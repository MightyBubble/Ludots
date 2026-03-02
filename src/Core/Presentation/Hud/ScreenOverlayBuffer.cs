using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    public enum ScreenOverlayItemKind : byte
    {
        Text = 0,
        Bar = 1,
        Rect = 2,
    }

    /// <summary>
    /// Screen-space overlay item. Written by presentation systems (Mod layer),
    /// consumed by the platform renderer each frame.
    /// Coordinates are in screen pixels (origin = top-left).
    /// </summary>
    public struct ScreenOverlayItem
    {
        public ScreenOverlayItemKind Kind;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int FontSize;
        public Vector4 Color;
        public Vector4 BackgroundColor;
        public int StringId;
    }

    /// <summary>
    /// Per-frame buffer for screen-space overlay items.
    /// Follows the same pattern as WorldHudBatchBuffer: systems Add() items,
    /// renderer reads GetSpan(), then Clear() each frame.
    /// Strings are stored in a parallel array to avoid GC in the item struct.
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

        public void Clear()
        {
            _count = 0;
            _stringCount = 0;
        }

        public int RegisterString(string text)
        {
            if (_stringCount >= MaxStrings) return -1;
            int id = _stringCount;
            _strings[_stringCount++] = text;
            return id;
        }

        public string GetString(int id)
        {
            if ((uint)id >= (uint)_stringCount) return null;
            return _strings[id];
        }

        public bool AddText(int x, int y, string text, int fontSize, Vector4 color)
        {
            if (_count >= MaxItems) return false;
            int sid = RegisterString(text);
            if (sid < 0) return false;
            _items[_count++] = new ScreenOverlayItem
            {
                Kind = ScreenOverlayItemKind.Text,
                X = x, Y = y,
                FontSize = fontSize,
                Color = color,
                StringId = sid,
            };
            return true;
        }

        public bool AddRect(int x, int y, int w, int h, Vector4 bgColor, Vector4 borderColor)
        {
            if (_count >= MaxItems) return false;
            _items[_count++] = new ScreenOverlayItem
            {
                Kind = ScreenOverlayItemKind.Rect,
                X = x, Y = y, Width = w, Height = h,
                BackgroundColor = bgColor,
                Color = borderColor,
            };
            return true;
        }

        public ReadOnlySpan<ScreenOverlayItem> GetSpan() => _items.AsSpan(0, _count);
    }
}
