using System;
using Ludots.Core.Diagnostics;
using SkiaSharp;
using Raylib_cs;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class RaylibSkiaRenderer : IDisposable
    {
        private SKBitmap? _bitmap;
        private SKCanvas? _canvas;
        private Texture2D _texture;
        private int _width;
        private int _height;

        public SKCanvas Canvas => _canvas ?? throw new InvalidOperationException("Raylib Skia canvas is not initialized.");
        public int Width => _width;
        public int Height => _height;

        public RaylibSkiaRenderer(int width, int height)
        {
            Log.Info(in LogChannels.Presentation, "GPU Accelerated: False (raster UI compositor)");
            Resize(width, height);
        }

        public void Resize(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (_width == width && _height == height && _canvas != null && _bitmap != null)
            {
                return;
            }

            _width = width;
            _height = height;

            _canvas?.Dispose();
            _canvas = null;
            _bitmap?.Dispose();
            _bitmap = null;
            if (_texture.id != 0)
            {
                Raylib_cs.Raylib.UnloadTexture(_texture);
            }

            Image img = Raylib_cs.Raylib.GenImageColor(width, height, Raylib_cs.Color.BLANK);
            _texture = Raylib_cs.Raylib.LoadTextureFromImage(img);
            Raylib_cs.Raylib.UnloadImage(img);

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            _bitmap = new SKBitmap(info);
            _canvas = new SKCanvas(_bitmap);
            ClearTransparent();
        }

        public void ClearTransparent()
        {
            if (_bitmap == null)
            {
                return;
            }

            IntPtr ptr = _bitmap.GetPixels();
            if (ptr == IntPtr.Zero)
            {
                _canvas?.Clear(SKColors.Transparent);
                return;
            }

            unsafe
            {
                new Span<byte>((void*)ptr, _bitmap.ByteCount).Clear();
            }

            _canvas?.ResetMatrix();
        }

        public void UpdateTexture()
        {
            if (_bitmap == null || _texture.id == 0)
            {
                return;
            }

            _canvas?.Flush();
            IntPtr ptr = _bitmap.GetPixels();
            if (ptr == IntPtr.Zero)
            {
                return;
            }

            unsafe
            {
                Raylib_cs.Raylib.UpdateTexture(_texture, (void*)ptr);
            }
        }

        public void Draw()
        {
            Raylib_cs.Raylib.BeginBlendMode(BlendMode.BLEND_ALPHA_PREMULTIPLY);
            Raylib_cs.Raylib.DrawTexture(_texture, 0, 0, Raylib_cs.Color.WHITE);
            Raylib_cs.Raylib.EndBlendMode();
        }

        public void RenderToScreen()
        {
            UpdateTexture();
            Draw();
        }

        public void Dispose()
        {
            _canvas?.Dispose();
            _canvas = null;
            _bitmap?.Dispose();
            _bitmap = null;
            if (_texture.id != 0)
            {
                Raylib_cs.Raylib.UnloadTexture(_texture);
            }
        }
    }
}
