using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using SkiaSharp;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class RaylibWorldHudRenderer : IDisposable
    {
        private const int MaxTextCacheEntries = 131072;
        private const int MaxTextTextureCacheEntries = 4096;
        private readonly WorldHudStringTable? _worldHudStrings;
        private readonly PresentationTextCatalog? _textCatalog;
        private readonly PresentationTextLocaleSelection? _localeSelection;
        private readonly Dictionary<int, CachedHudText> _textCache = new(MaxTextCacheEntries);
        private readonly Dictionary<TextTextureKey, CachedTextTexture> _textTextureCache = new(MaxTextTextureCacheEntries);
        private readonly SKPaint _textPaint = new()
        {
            IsAntialias = true,
            Typeface = SKTypeface.Default
        };
        private int _lastStableTextId;
        private int _lastStableTextDirtySerial;
        private CachedHudText? _lastStableText;

        public double LastBarMs { get; private set; }
        public double LastTextMs { get; private set; }
        public int LastBarCount { get; private set; }
        public int LastTextCount { get; private set; }
        public int LastTextTextureCacheCount => _textTextureCache.Count;

        public RaylibWorldHudRenderer(
            WorldHudStringTable? worldHudStrings,
            PresentationTextCatalog? textCatalog,
            PresentationTextLocaleSelection? localeSelection)
        {
            _worldHudStrings = worldHudStrings;
            _textCatalog = textCatalog;
            _localeSelection = localeSelection;
        }

        public void Draw(ScreenHudBatchBuffer screenHud)
        {
            if (screenHud == null) throw new ArgumentNullException(nameof(screenHud));

            long barStart = Stopwatch.GetTimestamp();
            DrawBars(screenHud.GetBarSpan());
            LastBarMs = ElapsedMs(barStart);

            long textStart = Stopwatch.GetTimestamp();
            DrawTexts(screenHud.GetTextSpan());
            LastTextMs = ElapsedMs(textStart);
        }

        private void DrawBars(ReadOnlySpan<ScreenHudBarItem> bars)
        {
            LastBarCount = bars.Length;
            for (int i = 0; i < bars.Length; i++)
            {
                ref readonly ScreenHudBarItem item = ref bars[i];
                int x = (int)item.ScreenX;
                int y = (int)item.ScreenY;
                int width = Math.Max(1, (int)MathF.Round(item.Width));
                int height = Math.Max(1, (int)MathF.Round(item.Height));
                int fillWidth = Math.Clamp((int)MathF.Round(width * Math.Clamp(item.Value0, 0f, 1f)), 0, width);

                Rl.DrawRectangle(x, y, width, height, RaylibColorUtil.ToRaylibColor(in item.Color0));
                if (fillWidth > 0)
                {
                    Rl.DrawRectangle(x, y, fillWidth, height, RaylibColorUtil.ToRaylibColor(in item.Color1));
                }
            }
        }

        private unsafe void DrawTexts(ReadOnlySpan<ScreenHudTextItem> texts)
        {
            LastTextCount = texts.Length;
            for (int i = 0; i < texts.Length; i++)
            {
                ref readonly ScreenHudTextItem item = ref texts[i];
                CachedHudText? text = ResolveText(in item);
                if (text == null || string.IsNullOrEmpty(text.Value.Text))
                {
                    continue;
                }

                CachedTextTexture texture = ResolveTextTexture(text.Value.Text, item.FontSize <= 0 ? 16 : item.FontSize, RaylibColorUtil.ToRaylibColor(in item.Color0));
                Rl.DrawTexture(texture.Texture, (int)item.ScreenX, (int)item.ScreenY, Color.WHITE);
            }
        }

        private CachedHudText? ResolveText(in ScreenHudTextItem item)
        {
            if (item.StableId != 0 &&
                item.StableId == _lastStableTextId &&
                item.DirtySerial == _lastStableTextDirtySerial)
            {
                return _lastStableText;
            }

            if (item.StableId != 0 &&
                _textCache.TryGetValue(item.StableId, out CachedHudText cached) &&
                cached.DirtySerial == item.DirtySerial)
            {
                _lastStableTextId = item.StableId;
                _lastStableTextDirtySerial = item.DirtySerial;
                _lastStableText = cached;
                return cached;
            }

            string? resolved = ResolveTextString(in item);
            if (string.IsNullOrEmpty(resolved))
            {
                return null;
            }

            var entry = new CachedHudText(item.DirtySerial, resolved);
            if (item.StableId != 0)
            {
                if (_textCache.Count >= MaxTextCacheEntries)
                {
                    _textCache.Clear();
                }

                _textCache[item.StableId] = entry;
                _lastStableTextId = item.StableId;
                _lastStableTextDirtySerial = item.DirtySerial;
                _lastStableText = entry;
            }

            return entry;
        }

        private string? ResolveTextString(in ScreenHudTextItem item)
        {
            if (TryFormatTextPacket(in item.Text, out string? packetText))
            {
                return packetText;
            }

            if (item.Id0 != 0 && _worldHudStrings != null)
            {
                return _worldHudStrings.TryGet(item.Id0);
            }

            return ResolveLegacyHudText(item.Id1, item.Value0, item.Value1);
        }

        private bool TryFormatTextPacket(in PresentationTextPacket packet, out string? text)
        {
            text = null;
            if (!packet.HasValue || _textCatalog == null || _localeSelection == null)
            {
                return false;
            }

            if (!PresentationTextFormatter.TryFormat(_textCatalog, _localeSelection.ActiveLocaleId, in packet, out string formatted))
            {
                return false;
            }

            text = formatted;
            return true;
        }

        private static string? ResolveLegacyHudText(int modeId, float value0, float value1)
        {
            WorldHudValueMode mode = (WorldHudValueMode)modeId;
            return mode switch
            {
                WorldHudValueMode.AttributeCurrentOverBase => $"{(int)value0}/{(int)value1}",
                WorldHudValueMode.AttributeCurrent => $"{(int)value0}",
                WorldHudValueMode.Constant => $"{value0}",
                _ => null
            };
        }

        private unsafe CachedTextTexture ResolveTextTexture(string text, int fontSize, Color color)
        {
            var key = new TextTextureKey(text, fontSize, color.r, color.g, color.b, color.a);
            if (_textTextureCache.TryGetValue(key, out CachedTextTexture cached))
            {
                return cached;
            }

            if (_textTextureCache.Count >= MaxTextTextureCacheEntries)
            {
                ClearTextTextureCache();
            }

            _textPaint.TextSize = fontSize;
            _textPaint.Color = new SKColor(color.r, color.g, color.b, color.a);
            SKRect bounds = default;
            _textPaint.MeasureText(text, ref bounds);
            int width = Math.Max(1, (int)MathF.Ceiling(bounds.Width + 2f));
            int height = Math.Max(1, (int)MathF.Ceiling(fontSize * 1.35f));
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawText(text, -bounds.Left + 1f, -bounds.Top + 1f, _textPaint);
            canvas.Flush();

            IntPtr pixels = bitmap.GetPixels();
            if (pixels == IntPtr.Zero)
            {
                return default;
            }

            Image image = Rl.GenImageColor(width, height, Color.BLANK);
            Texture2D texture = Rl.LoadTextureFromImage(image);
            Rl.UnloadImage(image);
            Rl.UpdateTexture(texture, (void*)pixels);

            var created = new CachedTextTexture(texture);
            _textTextureCache[key] = created;
            return created;
        }

        private void ClearTextTextureCache()
        {
            foreach ((_, CachedTextTexture cached) in _textTextureCache)
            {
                if (cached.Texture.id != 0)
                {
                    Rl.UnloadTexture(cached.Texture);
                }
            }

            _textTextureCache.Clear();
        }

        public void Dispose()
        {
            ClearTextTextureCache();
            _textPaint.Dispose();
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private readonly record struct CachedHudText(int DirtySerial, string Text);

        private readonly record struct CachedTextTexture(Texture2D Texture);

        private readonly record struct TextTextureKey(string Text, int FontSize, byte R, byte G, byte B, byte A);
    }
}
