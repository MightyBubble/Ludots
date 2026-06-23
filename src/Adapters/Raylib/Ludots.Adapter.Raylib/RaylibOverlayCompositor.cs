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
        private RaylibSkiaGpuOverlaySurface? _gpuTopOverlaySurface;
        private RaylibSkiaFramebufferOverlaySurface? _framebufferUnderlaySurface;
        private RaylibSkiaFramebufferOverlaySurface? _framebufferTopOverlaySurface;

        private bool _underlayHadContent;
        private bool _overlayHadContent;
        private bool _uiHadContent;
        private bool _compositeHadContent;
        private int _underlayLayerVersion = -1;
        private int _topOverlayLayerVersion = -1;
        private readonly PresentationOverlayLanePacer _underlayPacer = new(PresentationOverlayLayer.UnderUi);
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
            bool hasTopOverlay = scene != null && scene.ContainsLayer(PresentationOverlayLayer.TopMost);
            bool hasUiLayer = !suppressHostDiagnosticUi && drawSkiaUi && uiRoot.Scene != null;
            bool directTopOverlayComposite = hasTopOverlay && _useGpuDirectUnderlay && !hasUnderlay && !hasUiLayer;
            bool orderedDirectOverlayComposite = hasUnderlay && hasTopOverlay && !hasUiLayer && _useGpuDirectUnderlay;
            bool framebufferDirectTopOverlay = directTopOverlayComposite && _useFramebufferDirectUnderlay;
            bool gpuDirectTopOverlay = directTopOverlayComposite && !framebufferDirectTopOverlay;
            bool rasterTopOverlay = hasTopOverlay && !directTopOverlayComposite && !orderedDirectOverlayComposite;
            bool directUnderlayComposite = hasUnderlay && !hasUiLayer && (!hasTopOverlay || orderedDirectOverlayComposite);
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
                PresentationOverlayLanePacer.LaneRefreshPlan underlayPlan = hasUnderlay
                    ? _underlayPacer.BuildPlan(scene!)
                    : default;
                RenderUnderlay(scene!, hasUnderlay, directUnderlayComposite, framebufferDirectUnderlay, gpuDirectUnderlay, underlayPlan);
                if (hasUnderlay)
                {
                    _underlayPacer.MarkPresented(scene!, underlayPlan);
                }
                else
                {
                    _underlayPacer.Reset();
                }

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
                (currentTopOverlayVersion != _topOverlayLayerVersion ||
                 hasTopOverlay != _overlayHadContent ||
                 (framebufferDirectTopOverlay && hasTopOverlay));
            bool topOverlayHadContentBeforeRefresh = _overlayHadContent;
            if (refreshTopOverlay)
            {
                long topOverlayRenderStart = Stopwatch.GetTimestamp();
                if (gpuDirectTopOverlay)
                {
                    if (hasTopOverlay)
                    {
                        _gpuTopOverlaySurface ??= new RaylibSkiaGpuOverlaySurface();
                        if (!_gpuTopOverlaySurface.TryRender(
                            scene!,
                            _overlayRenderer,
                            PresentationOverlayLayer.TopMost,
                            _compositeRenderer.Width,
                            _compositeRenderer.Height))
                        {
                            throw new InvalidOperationException("Raylib Skia GPU top overlay is required for this production path but could not render.");
                        }
                    }
                    else
                    {
                        _gpuTopOverlaySurface?.Clear(_compositeRenderer.Width, _compositeRenderer.Height);
                    }

                    _overlayLayer.SetHasContent(false);
                }
                else if (framebufferDirectTopOverlay)
                {
                    _overlayLayer.SetHasContent(false);
                }
                else if (orderedDirectOverlayComposite)
                {
                    _overlayLayer.SetHasContent(false);
                }
                else
                {
                    _overlayLayer.Clear();
                    if (hasTopOverlay)
                    {
                        _overlayRenderer.Render(scene!, _overlayLayer.Canvas, PresentationOverlayLayer.TopMost);
                        _overlayLayer.SetHasContent(true);
                    }
                }

                paintMs += ElapsedMs(topOverlayRenderStart);
                _overlayHadContent = hasTopOverlay;
                _topOverlayLayerVersion = currentTopOverlayVersion;
            }

            bool hasCompositeContent = hasUnderlay || hasUiLayer || rasterTopOverlay;
            bool refreshComposite = ShouldRefreshComposite(
                underlayCanvasChanged,
                refreshUiLayer,
                refreshTopOverlay,
                rasterTopOverlay,
                topOverlayHadContentBeforeRefresh,
                hasCompositeContent,
                _compositeHadContent);

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

                if (rasterTopOverlay)
                {
                    _overlayLayer.DrawTo(_compositeRenderer.Canvas);
                }

                compositeMs = ElapsedMs(compositeStart);

                long uploadStart = Stopwatch.GetTimestamp();
                _compositeRenderer.UpdateTexture();
                uploadMs = hasCompositeContent ? ElapsedMs(uploadStart) : 0d;
                _compositeHadContent = hasCompositeContent;
            }

            if (hasCompositeContent || _compositeHadContent || directTopOverlayComposite || orderedDirectOverlayComposite)
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
                else if (hasCompositeContent || _compositeHadContent)
                {
                    _compositeRenderer.Draw();
                }

                if (gpuDirectTopOverlay)
                {
                    _gpuTopOverlaySurface?.Draw();
                }
                else if (framebufferDirectTopOverlay && hasTopOverlay)
                {
                    _framebufferTopOverlaySurface ??= new RaylibSkiaFramebufferOverlaySurface();
                    _framebufferTopOverlaySurface.Render(
                        scene!,
                        _overlayRenderer,
                        PresentationOverlayLayer.TopMost,
                        _compositeRenderer.Width,
                        _compositeRenderer.Height);
                }
                else if (orderedDirectOverlayComposite && hasTopOverlay)
                {
                    _framebufferTopOverlaySurface ??= new RaylibSkiaFramebufferOverlaySurface();
                    _framebufferTopOverlaySurface.Render(
                        scene!,
                        _overlayRenderer,
                        PresentationOverlayLayer.TopMost,
                        _compositeRenderer.Width,
                        _compositeRenderer.Height);
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

        internal static bool ShouldRefreshComposite(
            bool underlayCanvasChanged,
            bool refreshUiLayer,
            bool refreshTopOverlay,
            bool rasterTopOverlay,
            bool topOverlayHadContentBeforeRefresh,
            bool hasCompositeContent,
            bool compositeHadContent)
        {
            bool topOverlayAffectsComposite = rasterTopOverlay || topOverlayHadContentBeforeRefresh;
            return underlayCanvasChanged ||
                refreshUiLayer ||
                (refreshTopOverlay && topOverlayAffectsComposite) ||
                hasCompositeContent != compositeHadContent;
        }

        public void Dispose()
        {
            _overlayRenderer.Dispose();
            _gpuTopOverlaySurface?.Dispose();
            _gpuTopOverlaySurface = null;
            _framebufferTopOverlaySurface?.Dispose();
            _framebufferTopOverlaySurface = null;
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
                    ? "Skia overlay backend: GPU direct framebuffer underlay with raster compositor for UI/top overlay"
                    : "Skia overlay backend: GPU render-texture underlay with raster compositor for UI/top overlay");
        }

        private void RenderUnderlay(
            PresentationOverlayScene scene,
            bool hasUnderlay,
            bool directUnderlayComposite,
            bool framebufferDirectUnderlay,
            bool gpuDirectUnderlay,
            in PresentationOverlayLanePacer.LaneRefreshPlan refreshPlan)
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
                        refreshPlan,
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
                        refreshPlan,
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

                _overlayRenderer.Render(scene, targetCanvas, PresentationOverlayLayer.UnderUi, refreshPlan);

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
