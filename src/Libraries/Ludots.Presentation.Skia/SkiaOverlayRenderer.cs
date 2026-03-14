using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ludots.Core.Presentation.Hud;
using Ludots.UI.Runtime;
using SkiaSharp;

namespace Ludots.Presentation.Skia
{
    public sealed class SkiaOverlayRenderer : IDisposable
    {
        private const int LaneCount = 6;
        private const int MaxTextLayoutCacheEntries = 8192;

        private static readonly PresentationOverlayItemKind[] RenderOrder =
        {
            PresentationOverlayItemKind.Rect,
            PresentationOverlayItemKind.Bar,
            PresentationOverlayItemKind.Text
        };

        private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        private readonly SKPaint _textPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly Dictionary<FontCacheKey, SKFont> _fontCache = new();
        private readonly Dictionary<TextLayoutCacheKey, CachedTextLayout> _textLayoutCache = new();
        private readonly SKPicture?[] _lanePictures = new SKPicture?[LaneCount];
        private readonly int[] _laneVersions = new int[LaneCount];
        private readonly StringBuilder _runText = new();

        public SkiaOverlayRenderer()
        {
            Array.Fill(_laneVersions, -1);
        }

        public int CachedTextLayoutCount => _textLayoutCache.Count;

        public int RebuiltLaneCountLastFrame { get; private set; }

        public void ResetFrameStats()
        {
            RebuiltLaneCountLastFrame = 0;
        }

        public void Render(PresentationOverlayScene scene, SKCanvas canvas, PresentationOverlayLayer layer)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            for (int i = 0; i < RenderOrder.Length; i++)
            {
                PresentationOverlayItemKind kind = RenderOrder[i];
                int laneIndex = GetLaneIndex(layer, kind);
                int laneVersion = scene.GetLaneVersion(layer, kind);
                if (_laneVersions[laneIndex] != laneVersion)
                {
                    RebuildLanePicture(scene, layer, kind, laneIndex, laneVersion);
                }

                SKPicture? picture = _lanePictures[laneIndex];
                if (picture != null)
                {
                    canvas.DrawPicture(picture);
                }
            }
        }

        public void Dispose()
        {
            ClearTextLayoutCache();
            foreach ((_, SKFont font) in _fontCache)
            {
                font.Dispose();
            }

            for (int i = 0; i < _lanePictures.Length; i++)
            {
                _lanePictures[i]?.Dispose();
                _lanePictures[i] = null;
            }

            _fontCache.Clear();
            _fillPaint.Dispose();
            _strokePaint.Dispose();
            _textPaint.Dispose();
        }

        private void DrawRect(SKCanvas canvas, in PresentationOverlayItem item)
        {
            SKRect rect = new(item.X, item.Y, item.X + item.Width, item.Y + item.Height);
            _fillPaint.Color = ToSkColor(item.Color0);
            canvas.DrawRect(rect, _fillPaint);

            if (item.Color1.W > 0.01f)
            {
                _strokePaint.Color = ToSkColor(item.Color1);
                canvas.DrawRect(rect, _strokePaint);
            }
        }

        private void DrawBar(SKCanvas canvas, in PresentationOverlayItem item)
        {
            SKRect rect = new(item.X, item.Y, item.X + item.Width, item.Y + item.Height);
            _fillPaint.Color = ToSkColor(item.Color0);
            canvas.DrawRect(rect, _fillPaint);

            float clampedValue = Math.Clamp(item.Value0, 0f, 1f);
            if (clampedValue > 0f)
            {
                _fillPaint.Color = ToSkColor(item.Color1);
                canvas.DrawRect(item.X, item.Y, item.Width * clampedValue, item.Height, _fillPaint);
            }

            _strokePaint.Color = SKColors.Black;
            canvas.DrawRect(rect, _strokePaint);
        }

        private void DrawText(SKCanvas canvas, in PresentationOverlayItem item)
        {
            if (string.IsNullOrEmpty(item.Text))
            {
                return;
            }

            int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
            _textPaint.Color = ToSkColor(item.Color0);
            CachedTextLayout layout = GetTextLayout(item.Text, fontSize);
            float baselineY = item.Y + fontSize;
            for (int i = 0; i < layout.Runs.Length; i++)
            {
                CachedTextRun run = layout.Runs[i];
                if (run.Blob != null)
                {
                    canvas.DrawText(run.Blob, item.X + run.XOffset, baselineY, _textPaint);
                }
            }
        }

        private CachedTextLayout GetTextLayout(string text, int fontSize)
        {
            var cacheKey = new TextLayoutCacheKey(text, fontSize);
            if (_textLayoutCache.TryGetValue(cacheKey, out CachedTextLayout? cached))
            {
                return cached;
            }

            if (_textLayoutCache.Count >= MaxTextLayoutCacheEntries)
            {
                ClearTextLayoutCache();
            }

            var runs = new List<CachedTextRun>(8);
            _runText.Clear();

            SKTypeface? activeTypeface = null;
            float cursorX = 0f;
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                string element = enumerator.GetTextElement();
                SKTypeface typeface = UiFontRegistry.ResolveTypefaceForTextElement(null, bold: false, element);
                if (activeTypeface != null && !UiFontRegistry.SameTypeface(activeTypeface, typeface))
                {
                    cursorX = FlushRun(runs, activeTypeface, fontSize, cursorX);
                    _runText.Clear();
                }

                activeTypeface = typeface;
                _runText.Append(element);
            }

            if (_runText.Length > 0 && activeTypeface != null)
            {
                cursorX = FlushRun(runs, activeTypeface, fontSize, cursorX);
            }

            var created = new CachedTextLayout(runs.ToArray(), cursorX);
            _textLayoutCache[cacheKey] = created;
            return created;
        }

        private float FlushRun(List<CachedTextRun> runs, SKTypeface typeface, int fontSize, float cursorX)
        {
            string runText = _runText.ToString();
            SKFont font = GetFont(typeface, fontSize);
            SKTextBlob? blob = SKTextBlob.Create(runText, font);
            float width = font.MeasureText(runText, _textPaint);
            runs.Add(new CachedTextRun(blob, cursorX));
            return cursorX + width;
        }

        private void RebuildLanePicture(
            PresentationOverlayScene scene,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            int laneIndex,
            int laneVersion)
        {
            _lanePictures[laneIndex]?.Dispose();
            _lanePictures[laneIndex] = null;
            _laneVersions[laneIndex] = laneVersion;

            ReadOnlySpan<PresentationOverlayItem> span = scene.GetLaneSpan(layer, kind);
            if (span.Length == 0)
            {
                return;
            }

            using var recorder = new SKPictureRecorder();
            SKCanvas pictureCanvas = recorder.BeginRecording(new SKRect(-1f, -1f, 4096f, 4096f));
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                switch (kind)
                {
                    case PresentationOverlayItemKind.Text:
                        DrawText(pictureCanvas, item);
                        break;

                    case PresentationOverlayItemKind.Rect:
                        DrawRect(pictureCanvas, item);
                        break;

                    case PresentationOverlayItemKind.Bar:
                        DrawBar(pictureCanvas, item);
                        break;
                }
            }

            _lanePictures[laneIndex] = recorder.EndRecording();
            RebuiltLaneCountLastFrame++;
        }

        private void ClearTextLayoutCache()
        {
            foreach ((_, CachedTextLayout layout) in _textLayoutCache)
            {
                layout.Dispose();
            }

            _textLayoutCache.Clear();
        }

        private SKFont GetFont(SKTypeface typeface, int fontSize)
        {
            string familyName = typeface.FamilyName ?? string.Empty;
            var key = new FontCacheKey(familyName, fontSize);
            if (_fontCache.TryGetValue(key, out SKFont? font))
            {
                return font;
            }

            font = new SKFont(typeface, fontSize);
            _fontCache[key] = font;
            return font;
        }

        private static SKColor ToSkColor(in System.Numerics.Vector4 color)
        {
            byte a = (byte)Math.Clamp(color.W * 255f, 0f, 255f);
            byte r = (byte)Math.Clamp(color.X * 255f, 0f, 255f);
            byte g = (byte)Math.Clamp(color.Y * 255f, 0f, 255f);
            byte b = (byte)Math.Clamp(color.Z * 255f, 0f, 255f);
            return new SKColor(r, g, b, a);
        }

        private static int GetLaneIndex(PresentationOverlayLayer layer, PresentationOverlayItemKind kind)
        {
            return ((int)layer * 3) + ((int)kind - 1);
        }

        private readonly record struct FontCacheKey(string FamilyName, int FontSize);

        private readonly record struct TextLayoutCacheKey(string Text, int FontSize);

        private readonly record struct CachedTextRun(SKTextBlob? Blob, float XOffset);

        private sealed class CachedTextLayout : IDisposable
        {
            public CachedTextLayout(CachedTextRun[] runs, float width)
            {
                Runs = runs;
                Width = width;
            }

            public CachedTextRun[] Runs { get; }

            public float Width { get; }

            public void Dispose()
            {
                for (int i = 0; i < Runs.Length; i++)
                {
                    Runs[i].Blob?.Dispose();
                }
            }
        }
    }
}
