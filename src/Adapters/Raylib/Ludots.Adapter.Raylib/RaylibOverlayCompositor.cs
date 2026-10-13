using System;
using System.Diagnostics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation.Hud;
using Ludots.Presentation.Skia;
using Ludots.UI;
using Ludots.UI.Skia;
using SkiaSharp;
using Ludots.Raylib.Render;

namespace Ludots.Adapter.Raylib
{
    internal sealed class RaylibOverlayCompositor : IDisposable
    {
        private readonly RaylibSkiaRenderer _compositeRenderer;
        private readonly SkiaRasterLayer _underlayLayer = new();
        private readonly SkiaRasterLayer _uiLayer = new();
        private readonly SkiaRasterLayer _overlayLayer = new();
        private readonly SkiaOverlayRenderer _overlayRenderer = new();
        private RaylibSkiaGpuCanvasSurface? _gpuUnderlaySurface;
        private RaylibSkiaGpuCanvasSurface? _gpuTopOverlaySurface;
        private RaylibSkiaGpuCanvasSurface? _gpuUiSurface;
        private RaylibSkiaFramebufferOverlaySurface? _framebufferUnderlaySurface;
        private RaylibSkiaFramebufferOverlaySurface? _framebufferTopOverlaySurface;

        private bool _underlayHadContent;
        private static readonly bool OverlayTraceEnabled = ReadEnvBool("LUDOTS_OVERLAY_TRACE");
        private int _traceFrame;
        private bool _overlayHadContent;
        private bool _uiHadContent;
        private bool _compositeHadContent;
        private int _underlayLayerVersion = -1;
        private int _topOverlayLayerVersion = -1;
        private readonly PresentationOverlayLanePacer _underlayPacer = new(PresentationOverlayLayer.UnderUi);
        private readonly bool _useGpuDirectUnderlay;
        private readonly bool _useFramebufferDirectUnderlay;
        private readonly bool _useGpuDirectUi;

        public RaylibOverlayCompositor(int width, int height)
        {
            _compositeRenderer = new RaylibSkiaRenderer(width, height);
            _useGpuDirectUnderlay = !ReadEnvBool("LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY");
            _useFramebufferDirectUnderlay = !ReadEnvBool("LUDOTS_RAYLIB_DISABLE_SKIA_FRAMEBUFFER_UNDERLAY");
            _useGpuDirectUi = !ReadEnvBool("LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UI");
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
            // 尺寸变化后 GPU 表面会重建为空白纹理，UI 必须在下一帧强制重渲，
            // 否则干净的 IsDirty 门会继续展示空白缓存。
            _uiHadContent = false;
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
            bool gpuUiCompositor = _useGpuDirectUi;
            bool directTopOverlayComposite = hasTopOverlay && _useGpuDirectUnderlay && !hasUnderlay && (!hasUiLayer || gpuUiCompositor);
            bool orderedDirectOverlayComposite = hasUnderlay && hasTopOverlay && _useGpuDirectUnderlay;
            bool framebufferDirectTopOverlay = directTopOverlayComposite && _useFramebufferDirectUnderlay;
            bool gpuDirectTopOverlay = directTopOverlayComposite && !framebufferDirectTopOverlay;
            bool rasterTopOverlay = hasTopOverlay && !directTopOverlayComposite && !orderedDirectOverlayComposite;
            bool gpuOrFramebufferUnderlayEnabled = hasUnderlay &&
                _useGpuDirectUnderlay &&
                (!hasTopOverlay || orderedDirectOverlayComposite);
            bool framebufferDirectUnderlay = gpuOrFramebufferUnderlayEnabled && _useFramebufferDirectUnderlay;
            bool gpuDirectUnderlay = gpuOrFramebufferUnderlayEnabled && !framebufferDirectUnderlay;
            bool rasterDirectUnderlayComposite = hasUnderlay &&
                !gpuOrFramebufferUnderlayEnabled &&
                (!hasUiLayer || gpuUiCompositor) &&
                !rasterTopOverlay;
            bool directUnderlayComposite = gpuDirectUnderlay || framebufferDirectUnderlay || rasterDirectUnderlayComposite;

            int currentUnderlayVersion = scene?.GetLayerVersion(PresentationOverlayLayer.UnderUi) ?? 0;
            int currentTopOverlayVersion = scene?.GetLayerVersion(PresentationOverlayLayer.TopMost) ?? 0;
            bool refreshUnderlay = scene != null && (hasUnderlay || _underlayHadContent) &&
                (currentUnderlayVersion != _underlayLayerVersion || hasUnderlay != _underlayHadContent);
            if (framebufferDirectUnderlay && hasUnderlay)
            {
                refreshUnderlay = true;
            }

            if (OverlayTraceEnabled && (_traceFrame++ % 30) == 0)
            {
                int underUiTexts = 0, underUiBars = 0, topMost = 0;
                var orphanIds = new System.Text.StringBuilder();
                if (scene != null)
                {
                    foreach (ref readonly PresentationOverlayItem orphan in scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text))
                    {
                        underUiTexts++;
                        if (orphanIds.Length < 120)
                        {
                            orphanIds.Append($" id={orphan.StableId}({(int)orphan.X},{(int)orphan.Y})");
                        }
                    }

                    underUiBars = scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar).Length;
                    topMost = scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.MinimapMarker).Length +
                        scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text).Length;
                }

                Ludots.Core.Diagnostics.Log.Info(
                    in Ludots.Core.Diagnostics.LogChannels.Presentation,
                    $"[overlay-trace] f={_traceFrame} underlay={hasUnderlay} fbDirect={framebufferDirectUnderlay} uText={underUiTexts} uBar={underUiBars} miss={scene?.RemoveStableMisses ?? 0}{orphanIds}");
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
                if (gpuUiCompositor)
                {
                    if (hasUiLayer)
                    {
                        _gpuUiSurface ??= new RaylibSkiaGpuCanvasSurface("UI compositor");
                        if (!_gpuUiSurface.TryRender(
                            _compositeRenderer.Width,
                            _compositeRenderer.Height,
                            surface =>
                            {
                                skiaRenderer.SetTarget(surface);
                                uiRoot.Render();
                            }))
                        {
                            throw new InvalidOperationException("Raylib Skia GPU UI compositor is required for this production path but could not render.");
                        }
                    }
                    else
                    {
                        _gpuUiSurface?.Clear(_compositeRenderer.Width, _compositeRenderer.Height);
                    }

                    _uiLayer.SetHasContent(false);
                }
                else
                {
                    _uiLayer.Clear();
                    if (hasUiLayer)
                    {
                        skiaRenderer.SetCanvas(_uiLayer.Canvas);
                        uiRoot.Render();
                        _uiLayer.SetHasContent(true);
                    }
                }

                uiRenderMs = ElapsedMs(uiRenderStart);
                paintMs += uiRenderMs;
                _uiHadContent = hasUiLayer;
            }

            bool refreshTopOverlay = scene != null && (hasTopOverlay || _overlayHadContent) &&
                (currentTopOverlayVersion != _topOverlayLayerVersion ||
                 hasTopOverlay != _overlayHadContent ||
                 (framebufferDirectTopOverlay && hasTopOverlay));
            if (refreshTopOverlay)
            {
                long topOverlayRenderStart = Stopwatch.GetTimestamp();
                if (gpuDirectTopOverlay)
                {
                    if (hasTopOverlay)
                    {
                        _gpuTopOverlaySurface ??= new RaylibSkiaGpuCanvasSurface("overlay");
                        if (!_gpuTopOverlaySurface.TryRender(
                            _compositeRenderer.Width,
                            _compositeRenderer.Height,
                            surface => _overlayRenderer.Render(scene!, surface.Canvas, PresentationOverlayLayer.TopMost)))
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

            bool rasterUnderlayInComposite = hasUnderlay && !gpuDirectUnderlay && !framebufferDirectUnderlay;
            bool rasterUiInComposite = hasUiLayer && !gpuUiCompositor;
            bool hasRasterCompositeContent = rasterUnderlayInComposite || rasterUiInComposite || rasterTopOverlay;
            bool refreshRasterComposite = (rasterUnderlayInComposite && underlayCanvasChanged) ||
                (refreshUiLayer && !gpuUiCompositor) ||
                (refreshTopOverlay && rasterTopOverlay) ||
                hasRasterCompositeContent != _compositeHadContent;

            if (refreshRasterComposite && rasterDirectUnderlayComposite)
            {
                long uploadStart = Stopwatch.GetTimestamp();
                _compositeRenderer.UpdateTexture();
                uploadMs = hasRasterCompositeContent ? ElapsedMs(uploadStart) : 0d;
                _compositeHadContent = hasRasterCompositeContent;
            }
            else if (refreshRasterComposite && hasRasterCompositeContent)
            {
                long compositeStart = Stopwatch.GetTimestamp();
                _compositeRenderer.Canvas.Clear(SKColors.Transparent);
                if (rasterUnderlayInComposite)
                {
                    _underlayLayer.DrawTo(_compositeRenderer.Canvas);
                }

                if (rasterUiInComposite)
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
                uploadMs = ElapsedMs(uploadStart);
                _compositeHadContent = true;
            }
            else if (refreshRasterComposite)
            {
                _compositeHadContent = false;
            }

            bool drawCompositeTexture = _compositeHadContent && hasRasterCompositeContent;
            bool drawGpuUiSurface = gpuUiCompositor && _uiHadContent && hasUiLayer;
            if (gpuDirectUnderlay ||
                framebufferDirectUnderlay ||
                drawCompositeTexture ||
                drawGpuUiSurface ||
                directTopOverlayComposite ||
                orderedDirectOverlayComposite)
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

                if (drawGpuUiSurface)
                {
                    _gpuUiSurface?.Draw();
                }

                if (drawCompositeTexture)
                {
                    // Raster UI (and any raster TopMost) blit after GPU UnderUi HUD so a mounted
                    // panel does not force the world HUD back onto the full-window raster path.
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
                RefreshComposite: refreshRasterComposite,
                UiRenderMs: uiRenderMs);
        }

        public void Dispose()
        {
            _overlayRenderer.Dispose();
            _gpuUiSurface?.Dispose();
            _gpuUiSurface = null;
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
                    ? "Skia overlay backend: GPU direct framebuffer underlay"
                    : "Skia overlay backend: GPU render-texture underlay");

            if (!_useGpuDirectUi)
            {
                Log.Warn(
                    in LogChannels.Presentation,
                    "Skia GPU UI compositor disabled by LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UI. UI panel layer falls back to raster texture upload.");
            }
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
                    _gpuUnderlaySurface ??= new RaylibSkiaGpuCanvasSurface("overlay");
                    PresentationOverlayLanePacer.LaneRefreshPlan plan = refreshPlan;
                    if (!_gpuUnderlaySurface.TryRender(
                        _compositeRenderer.Width,
                        _compositeRenderer.Height,
                        surface => _overlayRenderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi, plan)))
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
