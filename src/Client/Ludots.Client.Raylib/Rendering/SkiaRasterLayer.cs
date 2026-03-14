using System;
using SkiaSharp;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class SkiaRasterLayer : IDisposable
    {
        private SKSurface? _surface;
        private int _width;
        private int _height;

        public SKCanvas Canvas => _surface?.Canvas ?? throw new InvalidOperationException("Skia raster layer surface is not initialized.");

        public bool HasContent { get; private set; }

        public void Resize(int width, int height)
        {
            if (_surface != null && _width == width && _height == height)
            {
                return;
            }

            _surface?.Dispose();
            _width = width;
            _height = height;
            _surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            Clear();
        }

        public void Clear()
        {
            if (_surface == null)
            {
                return;
            }

            _surface.Canvas.Clear(SKColors.Transparent);
            HasContent = false;
        }

        public void SetHasContent(bool hasContent)
        {
            HasContent = hasContent;
        }

        public void DrawTo(SKCanvas canvas)
        {
            if (!HasContent || _surface == null)
            {
                return;
            }

            canvas.DrawSurface(_surface, 0f, 0f);
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _surface = null;
            HasContent = false;
        }
    }
}
