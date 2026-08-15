using System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation.Hud;
using Ludots.Presentation.Skia;
using Raylib_cs;
using SkiaSharp;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib
{
    internal sealed class RaylibSkiaGpuOverlaySurface : IDisposable
    {
        private const uint GlRgba8 = 0x8058;

        private readonly GRGlInterface _glInterface;
        private readonly GRContext _context;

        private RenderTexture2D _target;
        private GRBackendRenderTarget? _renderTarget;
        private SKSurface? _surface;
        private int _width;
        private int _height;
        private bool _warnedResizeFailure;

        public RaylibSkiaGpuOverlaySurface()
        {
            (_glInterface, _context) = RaylibSkiaGlContext.Create("GPU overlay");
            Log.Info(in LogChannels.Presentation, "GPU Accelerated: True (Raylib Skia render-texture overlay)");
        }

        public bool HasTarget => _target.id != 0;

        public bool TryRender(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            PresentationOverlayLayer layer,
            int width,
            int height)
        {
            return TryRender(scene, renderer, layer, default, hasRefreshPlan: false, width, height);
        }

        public bool TryRender(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            PresentationOverlayLayer layer,
            in PresentationOverlayLanePacer.LaneRefreshPlan refreshPlan,
            int width,
            int height)
        {
            return TryRender(scene, renderer, layer, refreshPlan, hasRefreshPlan: true, width, height);
        }

        private bool TryRender(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            PresentationOverlayLayer layer,
            in PresentationOverlayLanePacer.LaneRefreshPlan refreshPlan,
            bool hasRefreshPlan,
            int width,
            int height)
        {
            if (!EnsureSurface(width, height))
            {
                return false;
            }

            _context.ResetContext(GRGlBackendState.All);
            Rl.BeginTextureMode(_target);
            Rl.ClearBackground(Color.BLANK);
            if (hasRefreshPlan)
            {
                renderer.Render(scene, _surface!.Canvas, layer, refreshPlan);
            }
            else
            {
                renderer.Render(scene, _surface!.Canvas, layer);
            }
            _surface!.Flush(submit: true, synchronous: false);
            _context.Submit(synchronous: false);
            Rl.EndTextureMode();
            return true;
        }

        public void Clear(int width, int height)
        {
            if (!EnsureSurface(width, height))
            {
                return;
            }

            Rl.BeginTextureMode(_target);
            Rl.ClearBackground(Color.BLANK);
            Rl.EndTextureMode();
        }

        public void Draw()
        {
            if (_target.id == 0)
            {
                return;
            }

            Rl.BeginBlendMode(BlendMode.BLEND_ALPHA_PREMULTIPLY);
            Rl.DrawTextureRec(
                _target.texture,
                new Rectangle(0f, 0f, _width, -_height),
                new System.Numerics.Vector2(0f, 0f),
                Color.WHITE);
            Rl.EndBlendMode();
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _surface = null;
            _renderTarget?.Dispose();
            _renderTarget = null;
            if (_target.id != 0)
            {
                Rl.UnloadRenderTexture(_target);
                _target = default;
            }

            _context.Dispose();
            _glInterface.Dispose();
        }

        private bool EnsureSurface(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (_surface != null && _width == width && _height == height)
            {
                return true;
            }

            _surface?.Dispose();
            _surface = null;
            _renderTarget?.Dispose();
            _renderTarget = null;
            if (_target.id != 0)
            {
                Rl.UnloadRenderTexture(_target);
                _target = default;
            }

            try
            {
                _target = Rl.LoadRenderTexture(width, height);
                if (_target.id == 0 || _target.texture.id == 0)
                {
                    throw new InvalidOperationException("Raylib LoadRenderTexture returned an empty render target.");
                }

                _renderTarget = new GRBackendRenderTarget(
                    width,
                    height,
                    sampleCount: 0,
                    stencilBits: 8,
                    glInfo: new GRGlFramebufferInfo(_target.id, GlRgba8));
                _surface = SKSurface.Create(
                    _context,
                    _renderTarget,
                    GRSurfaceOrigin.TopLeft,
                    SKColorType.Rgba8888);
                if (_surface == null)
                {
                    throw new InvalidOperationException("SKSurface.Create returned null for Raylib render texture.");
                }

                _width = width;
                _height = height;
                _warnedResizeFailure = false;
                return true;
            }
            catch (Exception ex)
            {
                if (!_warnedResizeFailure)
                {
                    Log.Warn(in LogChannels.Presentation, $"Skia GPU framebuffer overlay unavailable; raster compositor remains active. Reason: {ex.Message}");
                    _warnedResizeFailure = true;
                }

                _surface?.Dispose();
                _surface = null;
                _renderTarget?.Dispose();
                _renderTarget = null;
                if (_target.id != 0)
                {
                    Rl.UnloadRenderTexture(_target);
                    _target = default;
                }

                return false;
            }
        }
    }
}
