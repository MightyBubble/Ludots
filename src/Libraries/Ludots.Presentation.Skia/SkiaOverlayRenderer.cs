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
        private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        private readonly SKPaint _textPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly Dictionary<FontCacheKey, SKFont> _fontCache = new();
        private readonly List<TextRun> _runBuffer = new(8);
        private readonly StringBuilder _runText = new();

        public void Render(PresentationOverlayScene scene, SKCanvas canvas, PresentationOverlayLayer layer)
        {
            Render(scene, canvas, layer, kindFilter: null);
        }

        public void Render(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind? kindFilter)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            ReadOnlySpan<PresentationOverlayItem> span = scene.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (item.Layer != layer)
                {
                    continue;
                }

                if (kindFilter.HasValue && item.Kind != kindFilter.Value)
                {
                    continue;
                }

                switch (item.Kind)
                {
                    case PresentationOverlayItemKind.Text:
                        DrawText(canvas, item);
                        break;

                    case PresentationOverlayItemKind.Rect:
                        DrawRect(canvas, item);
                        break;

                    case PresentationOverlayItemKind.Bar:
                        DrawBar(canvas, item);
                        break;
                }
            }
        }

        public void Dispose()
        {
            foreach ((_, SKFont font) in _fontCache)
            {
                font.Dispose();
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
            BuildRuns(item.Text, fontSize);
            float baselineY = item.Y + fontSize;
            float cursorX = item.X;

            for (int i = 0; i < _runBuffer.Count; i++)
            {
                TextRun run = _runBuffer[i];
                canvas.DrawText(run.Text, cursorX, baselineY, SKTextAlign.Left, run.Font, _textPaint);
                cursorX += run.Font.MeasureText(run.Text, _textPaint);
            }
        }

        private void BuildRuns(string text, int fontSize)
        {
            _runBuffer.Clear();
            _runText.Clear();

            SKTypeface? activeTypeface = null;
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                string element = enumerator.GetTextElement();
                SKTypeface typeface = UiFontRegistry.ResolveTypefaceForTextElement(null, bold: false, element);
                if (activeTypeface != null && !UiFontRegistry.SameTypeface(activeTypeface, typeface))
                {
                    _runBuffer.Add(new TextRun(_runText.ToString(), GetFont(activeTypeface, fontSize)));
                    _runText.Clear();
                }

                activeTypeface = typeface;
                _runText.Append(element);
            }

            if (_runText.Length > 0 && activeTypeface != null)
            {
                _runBuffer.Add(new TextRun(_runText.ToString(), GetFont(activeTypeface, fontSize)));
            }
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

        private readonly record struct FontCacheKey(string FamilyName, int FontSize);

        private readonly record struct TextRun(string Text, SKFont Font);
    }
}
