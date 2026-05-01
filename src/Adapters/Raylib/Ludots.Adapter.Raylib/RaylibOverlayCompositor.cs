using System;
using System.Diagnostics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation.Hud;
using Ludots.Presentation.Skia;
using Ludots.UI;
using Ludots.UI.Skia;
using SkiaSharp;

namespace Ludots.Adapter.Raylib
{
    internal sealed class RaylibOverlayCompositor : IDisposable
    {
        private readonly RaylibSkiaRenderer _compositeRenderer;
        private readonly SkiaRasterLayer _underlayLayer = new();
        private readonly SkiaRasterLayer _uiLayer = new();
        private readonly SkiaRasterLayer _overlayLayer = new();
        private readonly SkiaOverlayRenderer _overlayRenderer = new();
        private RaylibSkiaGpuOverlaySurface? _gpuUnderlaySurface;
        private RaylibSkiaFramebufferOverlaySurface? _framebufferUnderlaySurface;

        private bool _underlayHadContent;
        private bool _overlayHadContent;
        private bool _uiHadContent;
        private bool _compositeHadContent;
        private int _underlayLayerVersion = -1;
        private int _topOverlayLayerVersion = -1;
        private readonly bool _useGpuDirectUnderlay;
        private readonly bool _useFramebufferDirectUnderlay;

        public RaylibOverlayCompositor(int width, int height)
        {
            _compositeRenderer = new RaylibSkiaRenderer(width, height);
            _useGpuDirectUnderlay = !ReadEnvBool("LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY");
            _useFramebufferDirectUnderlay = !ReadEnvBool("LUDOTS_RAYLIB_DISABLE_SKIA_FRAMEBUFFER_UNDERLAY");
            LogConfiguredOverlayBackend();
            Resize(width, height);
        }

        public SkiaOverlayRenderer OverlayRenderer => _overlayRenderer;

        public bool CompositeHadContent => _compositeHadContent;

        public void Resize(int width, int height)
        {
            _compositeRenderer.Resize(width, height);
            _underlayLayer.Resize(width, height);
            _uiLayer.Resize(width, height);
            _overlayLayer.Resize(width, height);
        }

        public OverlayCompositeResult Render(
            PresentationOverlayScene? scene,
            UIRoot uiRoot,
            SkiaUiRenderer skiaRenderer,
            bool drawSkiaUi,
            bool suppressHostDiagnosticUi)
        {
            _overlayRenderer.ResetFrameStats();

            double paintMs = 0d;
            double compositeMs = 0d;
            double uploadMs = 0d;
            double finalDrawMs = 0d;
            double uiRenderMs = 0d;

            bool hasUnderlay = scene != null && scene.ContainsLayer(PresentationOverlayLayer.UnderUi);
            bool hasTopOverlay = !suppressHostDiagnosticUi && scene != null && scene.ContainsLayer(PresentationOverlayLayer.TopMost);
            bool hasUiLayer = !suppressHostDiagnosticUi && drawSkiaUi && uiRoot.Scene != null;
            bool directUnderlayComposite = hasUnderlay && !hasUiLayer && !hasTopOverlay;
            bool framebufferDirectUnderlay = directUnderlayComposite && _useGpuDirectUnderlay && _useFramebufferDirectUnderlay;
            bool gpuDirectUnderlay = directUnderlayComposite && _useGpuDirectUnderlay && !framebufferDirectUnderlay;

            int currentUnderlayVersion = scene?.GetLayerVersion(PresentationOverlayLayer.UnderUi) ?? 0;
            int currentTopOverlayVersion = scene?.GetLayerVersion(PresentationOverlayLayer.TopMost) ?? 0;
            bool refreshUnderlay = scene != null && (hasUnderlay || _underlayHadContent) &&
                (currentUnderlayVersion != _underlayLayerVersion || hasUnderlay != _underlayHadContent);
            if (framebufferDirectUnderlay && hasUnderlay)
            {
                refreshUnderlay = true;
            }

            bool underlayCanvasChanged = false;
            if (refreshUnderlay)
            {
                long underlayRenderStart = Stopwatch.GetTimestamp();
                RenderUnderlay(scene!, hasUnderlay, directUnderlayComposite, framebufferDirectUnderlay, gpuDirectUnderlay);
                underlayCanvasChanged = true;
                _underlayHadContent = hasUnderlay;
                _underlayLayerVersion = currentUnderlayVersion;
                paintMs += ElapsedMs(underlayRenderStart);
            }

            bool refreshUiLayer = hasUiLayer
                ? (!_uiHadContent || uiRoot.IsDirty)
                : _uiHadContent;
            if (refreshUiLayer)
            {
                long uiRenderStart = Stopwatch.GetTimestamp();
                _uiLayer.Clear();
                if (hasUiLayer)
                {
                    skiaRenderer.SetCanvas(_uiLayer.Canvas);
                    uiRoot.Render();
                    _uiLayer.SetHasContent(true);
                }

                uiRenderMs = ElapsedMs(uiRenderStart);
                paintMs += uiRenderMs;
                _uiHadContent = hasUiLayer;
            }

            bool refreshTopOverlay = scene != null && (hasTopOverlay || _overlayHadContent) &&
                (currentTopOverlayVersion != _topOverlayLayerVersion || hasTopOverlay != _overlayHadContent);
            if (refreshTopOverlay)
            {
                long topOverlayRenderStart = Stopwatch.GetTimestamp();
                _overlayLayer.Clear();
                if (hasTopOverlay)
                {
                    _overlayRenderer.Render(scene!, _overlayLayer.Canvas, PresentationOverlayLayer.TopMost);
                    _overlayLayer.SetHasContent(true);
                }

                paintMs += ElapsedMs(topOverlayRenderStart);
                _overlayHadContent = hasTopOverlay;
                _topOverlayLayerVersion = currentTopOverlayVersion;
            }

            bool hasCompositeContent = hasUnderlay || hasUiLayer || hasTopOverlay;
            bool refreshComposite = underlayCanvasChanged || refreshUiLayer || refreshTopOverlay ||
                hasCompositeContent != _compositeHadContent;

            if (framebufferDirectUnderlay || gpuDirectUnderlay)
            {
                _compositeHadContent = hasCompositeContent;
            }
            else if (refreshComposite && directUnderlayComposite)
            {
                long uploadStart = Stopwatch.GetTimestamp();
                _compositeRenderer.UpdateTexture();
                uploadMs = hasCompositeContent ? ElapsedMs(uploadStart) : 0d;
                _compositeHadContent = hasCompositeContent;
            }
            else if (refreshComposite)
            {
                long compositeStart = Stopwatch.GetTimestamp();
                _compositeRenderer.Canvas.Clear(SKColors.Transparent);
                if (hasUnderlay)
                {
                    _underlayLayer.DrawTo(_compositeRenderer.Canvas);
                }

                if (hasUiLayer)
                {
                    _uiLayer.DrawTo(_compositeRenderer.Canvas);
                }

                if (hasTopOverlay)
                {
                    _overlayLayer.DrawTo(_compositeRenderer.Canvas);
                }

                compositeMs = ElapsedMs(compositeStart);

                long uploadStart = Stopwatch.GetTimestamp();
                _compositeRenderer.UpdateTexture();
                uploadMs = hasCompositeContent ? ElapsedMs(uploadStart) : 0d;
                _compositeHadContent = hasCompositeContent;
            }

            if (hasCompositeContent || _compositeHadContent)
            {
                long finalDrawStart = Stopwatch.GetTimestamp();
                if (framebufferDirectUnderlay)
                {
                    // Direct framebuffer Skia draws during paint; no final fullscreen composite is needed.
                }
                else if (gpuDirectUnderlay)
                {
                    _gpuUnderlaySurface?.Draw();
                }
                else
                {
                    _compositeRenderer.Draw();
                }

                finalDrawMs = ElapsedMs(finalDrawStart);
            }

            return new OverlayCompositeResult(
                PaintMs: paintMs,
                CompositeMs: compositeMs,
                UploadMs: uploadMs,
                FinalDrawMs: finalDrawMs,
                RefreshComposite: refreshComposite,
                UiRenderMs: uiRenderMs);
        }

        public void Dispose()
        {
            _overlayRenderer.Dispose();
            _framebufferUnderlaySurface?.Dispose();
            _framebufferUnderlaySurface = null;
            _gpuUnderlaySurface?.Dispose();
            _gpuUnderlaySurface = null;
            _overlayLayer.Dispose();
            _uiLayer.Dispose();
            _underlayLayer.Dispose();
            _compositeRenderer.Dispose();
        }

        private void LogConfiguredOverlayBackend()
        {
            if (ReadEnvBool("LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY"))
            {
                Log.Warn(
                    in LogChannels.Presentation,
                    "Skia GPU underlay disabled by LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY. Using raster texture compositor.");
                Log.Info(
                    in LogChannels.Presentation,
                    "Skia overlay backend: raster texture compositor");
                return;
            }

            Log.Info(
                in LogChannels.Presentation,
                _useFramebufferDirectUnderlay
                    ? "Skia overlay backend: GPU direct framebuffer underlay with raster compositor fallback for UI/top overlay"
                    : "Skia overlay backend: GPU render-texture underlay with raster compositor fallback for UI/top overlay");
        }

        private void RenderUnderlay(
            PresentationOverlayScene scene,
            bool hasUnderlay,
            bool directUnderlayComposite,
            bool framebufferDirectUnderlay,
            bool gpuDirectUnderlay)
        {
            if (framebufferDirectUnderlay)
            {
            }
            else if (gpuDirectUnderlay)
            {
                if (!hasUnderlay)
                {
                    _gpuUnderlaySurface?.Clear(_compositeRenderer.Width, _compositeRenderer.Height);
                }
            }
            else if (directUnderlayComposite)
            {
                _compositeRenderer.ClearTransparent();
            }
            else
            {
                _underlayLayer.Clear();
            }

            if (hasUnderlay)
            {
                if (framebufferDirectUnderlay)
                {
                    _framebufferUnderlaySurface ??= new RaylibSkiaFramebufferOverlaySurface();
                    _framebufferUnderlaySurface.Render(
                        scene,
                        _overlayRenderer,
                        PresentationOverlayLayer.UnderUi,
                        _compositeRenderer.Width,
                        _compositeRenderer.Height);

                    _underlayLayer.SetHasContent(false);
                    return;
                }

                if (gpuDirectUnderlay)
                {
                    _gpuUnderlaySurface ??= new RaylibSkiaGpuOverlaySurface();
                    if (!_gpuUnderlaySurface.TryRender(
                        scene,
                        _overlayRenderer,
                        PresentationOverlayLayer.UnderUi,
                        _compositeRenderer.Width,
                        _compositeRenderer.Height))
                    {
                        throw new InvalidOperationException("Raylib Skia GPU underlay is required for this production path but could not render.");
                    }

                    _underlayLayer.SetHasContent(false);
                    return;
                }

                SKCanvas targetCanvas = directUnderlayComposite
                    ? _compositeRenderer.Canvas
                    : _underlayLayer.Canvas;

                _overlayRenderer.Render(scene, targetCanvas, PresentationOverlayLayer.UnderUi);

                _underlayLayer.SetHasContent(!directUnderlayComposite);
            }
        }

        private static double ElapsedMs(long start)
        {
            return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        private static bool ReadEnvBool(string key)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

    }

    internal readonly record struct OverlayCompositeResult(
        double PaintMs,
        double CompositeMs,
        double UploadMs,
        double FinalDrawMs,
        bool RefreshComposite,
        double UiRenderMs);
}
