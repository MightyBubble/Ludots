using System;
using Ludots.Core.Presentation.Hud;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    /// <summary>
    /// Composites screen-space HUD bars into one texture so Raylib avoids per-item rectangle calls.
    /// </summary>
    public sealed unsafe class RaylibHudBarBatchRenderer : IDisposable
    {
        private static readonly Color BorderColor = new(0, 0, 0, 255);

        private Texture2D _texture;
        private Color[] _pixels = Array.Empty<Color>();
        private int _width;
        private int _height;
        private bool _initialized;

        public int LastBarCount { get; private set; }

        public int Draw(ReadOnlySpan<ScreenHudItem> items, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                LastBarCount = 0;
                return 0;
            }

            if (!ContainsBar(items))
            {
                LastBarCount = 0;
                return 0;
            }

            EnsureTexture(screenWidth, screenHeight);
            Array.Clear(_pixels, 0, _pixels.Length);

            int barCount = RasterizeBars(items, _pixels, _width, _height);
            LastBarCount = barCount;
            if (barCount <= 0)
            {
                return 0;
            }

            fixed (Color* pixels = _pixels)
            {
                Rl.UpdateTexture(_texture, pixels);
            }

            Rl.DrawTexture(_texture, 0, 0, Color.WHITE);
            return barCount;
        }

        public void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            Rl.UnloadTexture(_texture);
            _texture = default;
            _pixels = Array.Empty<Color>();
            _width = 0;
            _height = 0;
            _initialized = false;
            LastBarCount = 0;
        }

        private void EnsureTexture(int screenWidth, int screenHeight)
        {
            if (_initialized && _width == screenWidth && _height == screenHeight)
            {
                return;
            }

            if (_initialized)
            {
                Rl.UnloadTexture(_texture);
                _initialized = false;
            }

            Image image = Rl.GenImageColor(screenWidth, screenHeight, Color.BLANK);
            _texture = Rl.LoadTextureFromImage(image);
            Rl.UnloadImage(image);

            _pixels = new Color[screenWidth * screenHeight];
            _width = screenWidth;
            _height = screenHeight;
            _initialized = true;
        }

        private static bool ContainsBar(ReadOnlySpan<ScreenHudItem> items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Kind == WorldHudItemKind.Bar)
                {
                    return true;
                }
            }

            return false;
        }

        private static int RasterizeBars(ReadOnlySpan<ScreenHudItem> items, Color[] pixels, int textureWidth, int textureHeight)
        {
            int barCount = 0;
            for (int i = 0; i < items.Length; i++)
            {
                ref readonly var item = ref items[i];
                if (item.Kind != WorldHudItemKind.Bar)
                {
                    continue;
                }

                int x = (int)item.ScreenX;
                int y = (int)item.ScreenY;
                int width = (int)item.Width;
                int height = (int)item.Height;
                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                int fillWidth = Math.Clamp((int)(width * item.Value0), 0, width);
                DrawFilledRect(pixels, textureWidth, textureHeight, x, y, width, height, RaylibColorUtil.ToRaylibColor(item.Color0));
                if (fillWidth > 0)
                {
                    DrawFilledRect(pixels, textureWidth, textureHeight, x, y, fillWidth, height, RaylibColorUtil.ToRaylibColor(item.Color1));
                }

                DrawOutlineRect(pixels, textureWidth, textureHeight, x, y, width, height, BorderColor);
                barCount++;
            }

            return barCount;
        }

        private static void DrawFilledRect(Color[] pixels, int textureWidth, int textureHeight, int x, int y, int width, int height, Color color)
        {
            if (width <= 0 || height <= 0 || color.a == 0)
            {
                return;
            }

            int x0 = Math.Max(0, x);
            int y0 = Math.Max(0, y);
            int x1 = Math.Min(textureWidth, x + width);
            int y1 = Math.Min(textureHeight, y + height);
            if (x0 >= x1 || y0 >= y1)
            {
                return;
            }

            for (int py = y0; py < y1; py++)
            {
                int rowStart = py * textureWidth;
                for (int px = x0; px < x1; px++)
                {
                    BlendPixel(ref pixels[rowStart + px], color);
                }
            }
        }

        private static void DrawOutlineRect(Color[] pixels, int textureWidth, int textureHeight, int x, int y, int width, int height, Color color)
        {
            if (width <= 0 || height <= 0 || color.a == 0)
            {
                return;
            }

            DrawFilledRect(pixels, textureWidth, textureHeight, x, y, width, 1, color);
            if (height > 1)
            {
                DrawFilledRect(pixels, textureWidth, textureHeight, x, y + height - 1, width, 1, color);
            }

            int innerHeight = height - 2;
            if (innerHeight <= 0)
            {
                return;
            }

            DrawFilledRect(pixels, textureWidth, textureHeight, x, y + 1, 1, innerHeight, color);
            if (width > 1)
            {
                DrawFilledRect(pixels, textureWidth, textureHeight, x + width - 1, y + 1, 1, innerHeight, color);
            }
        }

        private static void BlendPixel(ref Color destination, Color source)
        {
            if (source.a == 0)
            {
                return;
            }

            if (source.a == 255 || destination.a == 0)
            {
                destination = source;
                return;
            }

            int sourceAlpha = source.a;
            int inverseSourceAlpha = 255 - sourceAlpha;
            int destinationAlpha = destination.a;
            int outputAlpha = sourceAlpha + ((destinationAlpha * inverseSourceAlpha + 127) / 255);
            if (outputAlpha <= 0)
            {
                destination = Color.BLANK;
                return;
            }

            int outputPremulR = (source.r * sourceAlpha) + ((destination.r * destinationAlpha * inverseSourceAlpha + 127) / 255);
            int outputPremulG = (source.g * sourceAlpha) + ((destination.g * destinationAlpha * inverseSourceAlpha + 127) / 255);
            int outputPremulB = (source.b * sourceAlpha) + ((destination.b * destinationAlpha * inverseSourceAlpha + 127) / 255);

            destination = new Color(
                (byte)((outputPremulR + (outputAlpha / 2)) / outputAlpha),
                (byte)((outputPremulG + (outputAlpha / 2)) / outputAlpha),
                (byte)((outputPremulB + (outputAlpha / 2)) / outputAlpha),
                (byte)outputAlpha);
        }
    }
}
