using System;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Raylib.Render;
using SkiaSharp;

namespace Ludots.App.RaylibAssetAcceptance
{
    /// <summary>
    /// 验收台 HUD 文字：raylib 默认字体无 CJK，经 RaylibSkiaRenderer 以系统字体渲染，
    /// 帧内累积、Flush 一次性上屏（与画廊 GalleryFont 同一合成路径）。
    /// </summary>
    public sealed class AcceptanceFont
    {
        private const float BaselineOffsetFactor = 0.82f;

        private static readonly Lazy<AcceptanceFont> Instance = new(() => new AcceptanceFont());

        private RaylibSkiaRenderer? _renderer;
        private SKTypeface? _typeface;
        private bool _frameBegun;

        private AcceptanceFont()
        {
        }

        public static void Draw(string text, int x, int y, float size, Color color)
        {
            AcceptanceFont self = Instance.Value;
            self.EnsureFrame();
            SKCanvas canvas = self._renderer!.Canvas;
            using var font = new SKFont(self.GetTypeface(), size);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(color.r, color.g, color.b, color.a),
            };
            canvas.DrawText(text, x, y + (size * BaselineOffsetFactor), font, paint);
        }

        public static void Reset()
        {
            if (!Instance.IsValueCreated)
            {
                return;
            }

            AcceptanceFont self = Instance.Value;
            self._renderer?.Dispose();
            self._renderer = null;
            self._frameBegun = false;
        }

        public static void Flush()
        {
            if (!Instance.IsValueCreated)
            {
                return;
            }

            AcceptanceFont self = Instance.Value;
            if (self._renderer == null || !self._frameBegun)
            {
                return;
            }

            self._renderer.UpdateTexture();
            self._renderer.Draw();
            self._frameBegun = false;
        }

        private void EnsureFrame()
        {
            int width = Rl.GetScreenWidth();
            int height = Rl.GetScreenHeight();
            if (_renderer == null)
            {
                _renderer = new RaylibSkiaRenderer(width, height);
            }
            else if (_renderer.Width != width || _renderer.Height != height)
            {
                _renderer.Resize(width, height);
            }

            if (!_frameBegun)
            {
                _renderer.ClearTransparent();
                _frameBegun = true;
            }
        }

        private SKTypeface GetTypeface()
        {
            if (_typeface != null)
            {
                return _typeface;
            }

            _typeface = SKTypeface.FromFamilyName("Microsoft YaHei")
                ?? SKTypeface.FromFamilyName("SimHei")
                ?? SKTypeface.Default;
            return _typeface;
        }
    }
}
