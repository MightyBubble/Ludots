using System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation.Hud;
using Ludots.Presentation.Skia;
using SkiaSharp;

namespace Ludots.Adapter.Raylib
{
    internal sealed class RaylibSkiaFramebufferOverlaySurface : IDisposable
    {
        private const uint GlRgba8 = 0x8058;

        private readonly GRGlInterface _glInterface;
        private readonly GRContext _context;

        private GRBackendRenderTarget? _renderTarget;
        private SKSurface? _surface;
        private int _width;
        private int _height;

        public RaylibSkiaFramebufferOverlaySurface()
        {
            (_glInterface, _context) = RaylibSkiaGlContext.Create("framebuffer overlay");
            Log.Info(in LogChannels.Presentation, "GPU Accelerated: True (Raylib Skia direct framebuffer overlay)");
        }

        public void Render(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            PresentationOverlayLayer layer,
            int width,
            int height)
        {
            Render(scene, renderer, layer, default, hasRefreshPlan: false, width, height);
        }

        public void Render(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            PresentationOverlayLayer layer,
            in PresentationOverlayLanePacer.LaneRefreshPlan refreshPlan,
            int width,
            int height)
        {
            Render(scene, renderer, layer, refreshPlan, hasRefreshPlan: true, width, height);
        }

        private void Render(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            PresentationOverlayLayer layer,
            in PresentationOverlayLanePacer.LaneRefreshPlan refreshPlan,
            bool hasRefreshPlan,
            int width,
            int height)
        {
            EnsureSurface(width, height);

            _context.ResetContext(GRGlBackendState.All);
            SKCanvas canvas = _surface!.Canvas;
            canvas.ResetMatrix();
            if (hasRefreshPlan)
            {
                renderer.Render(scene, canvas, layer, refreshPlan);
            }
            else
            {
                renderer.Render(scene, canvas, layer);
            }

            _surface.Flush(submit: true, synchronous: false);
            _context.Submit(synchronous: false);
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _surface = null;
            _renderTarget?.Dispose();
            _renderTarget = null;
            _context.Dispose();
            _glInterface.Dispose();
        }

        private void EnsureSurface(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (_surface != null && _width == width && _height == height)
            {
                return;
            }

            _surface?.Dispose();
            _surface = null;
            _renderTarget?.Dispose();
            _renderTarget = null;

            _renderTarget = new GRBackendRenderTarget(
                width,
                height,
                sampleCount: 0,
                stencilBits: 8,
                glInfo: new GRGlFramebufferInfo(0, GlRgba8));
            _surface = SKSurface.Create(
                _context,
                _renderTarget,
                GRSurfaceOrigin.BottomLeft,
                SKColorType.Rgba8888);
            if (_surface == null)
            {
                throw new InvalidOperationException("SKSurface.Create returned null for Raylib framebuffer.");
            }

            _width = width;
            _height = height;
        }
    }
}
