using System;
using System.Numerics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib.Services
{
    public sealed class RaylibBenchmarkRenderService : IRaylibBenchmarkRenderer
    {
        private const int MinInstances = 3_000;
        private const int DefaultInstances = 30_000;
        private const int HudBarCount = 960;
        private const int HudTextCount = 960;
        private const int SliderTrackWidth = 460;
        private const int SliderTrackHeight = 16;
        private const int SliderLeft = 34;
        private const int SliderTop = 92;

        private readonly RaylibBenchmarkRenderer _renderer;
        private readonly ScreenHudBatchBuffer _screenHud;
        private readonly ScreenOverlayBuffer _screenOverlay;
        private float _time;
        private bool _sliderDragging;
        private int _targetInstanceCount = DefaultInstances;
        private int _frameSerial = 1;

        public RaylibBenchmarkRenderService(
            RaylibPrimitiveRenderer primitiveRenderer,
            MeshAssetRegistry meshes,
            ScreenHudBatchBuffer screenHud,
            ScreenOverlayBuffer screenOverlay)
        {
            _renderer = new RaylibBenchmarkRenderer(primitiveRenderer, meshes);
            _screenHud = screenHud ?? throw new ArgumentNullException(nameof(screenHud));
            _screenOverlay = screenOverlay ?? throw new ArgumentNullException(nameof(screenOverlay));
        }

        public RaylibBenchmarkStats LastStats => _renderer.LastStats;

        public bool HasActiveScene => _renderer.CurrentScene.Enabled;

        public void SetScene(in RaylibBenchmarkScene scene)
        {
            _renderer.SetScene(scene);
            if (!scene.Enabled)
            {
                _time = 0f;
                _sliderDragging = false;
                _targetInstanceCount = DefaultInstances;
                _frameSerial = 1;
                _screenHud.Clear();
                _screenOverlay.Clear();
                return;
            }

            _targetInstanceCount = Math.Clamp(scene.InitialActiveInstanceCount, MinInstances, scene.Instances.Length);
            _frameSerial = 1;
        }

        public bool Draw(Camera3D fallbackCamera)
        {
            return _renderer.Draw(fallbackCamera);
        }

        public bool SetActiveInstanceCount(int count)
        {
            return _renderer.SetActiveInstanceCount(count);
        }

        public int GetActiveInstanceCount()
        {
            return _renderer.GetActiveInstanceCount();
        }

        public ScreenHudBatchBuffer GetScreenHudBuffer()
        {
            return _screenHud;
        }

        public ScreenOverlayBuffer GetScreenOverlayBuffer()
        {
            return _screenOverlay;
        }

        public void PrepareFrame(PresentationTimingDiagnostics? timing, int screenWidth, int screenHeight)
        {
            RaylibBenchmarkScene scene = _renderer.CurrentScene;
            if (!scene.Enabled)
            {
                return;
            }

            _time += MathF.Max(0.0001f, Rl.GetFrameTime());
            _frameSerial++;
            UpdateSlider(scene.Instances.Length);
            _renderer.SetActiveInstanceCount(_targetInstanceCount);
            PopulateSkiaHud(LastStats, timing, screenWidth, screenHeight);
        }

        private void UpdateSlider(int maxInstances)
        {
            Vector2 mouse = Rl.GetMousePosition();
            bool overTrack = mouse.X >= SliderLeft &&
                             mouse.X <= SliderLeft + SliderTrackWidth &&
                             mouse.Y >= SliderTop - 12 &&
                             mouse.Y <= SliderTop + SliderTrackHeight + 12;

            if (Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON) && overTrack)
            {
                _sliderDragging = true;
            }

            if (!Rl.IsMouseButtonDown(MouseButton.MOUSE_LEFT_BUTTON))
            {
                _sliderDragging = false;
            }

            if (!_sliderDragging)
            {
                return;
            }

            float normalized = Math.Clamp((mouse.X - SliderLeft) / SliderTrackWidth, 0f, 1f);
            int unclamped = MinInstances + (int)MathF.Round(normalized * (maxInstances - MinInstances));
            _targetInstanceCount = RoundInstanceCount(Math.Clamp(unclamped, MinInstances, maxInstances));
        }

        private void PopulateSkiaHud(RaylibBenchmarkStats stats, PresentationTimingDiagnostics? timing, int screenWidth, int screenHeight)
        {
            _screenHud.Clear();
            _screenOverlay.Clear();

            AddControlPanel(stats, timing);
            AddAnimatedHudBars(screenWidth);
            AddAnimatedOverlayTexts(stats, timing, screenWidth, screenHeight);
        }

        private void AddControlPanel(RaylibBenchmarkStats stats, PresentationTimingDiagnostics? timing)
        {
            int maxInstances = Math.Max(MinInstances, _renderer.CurrentScene.Instances.Length);
            float ratio = maxInstances == MinInstances
                ? 0f
                : (_targetInstanceCount - MinInstances) / (float)(maxInstances - MinInstances);
            int knobCenterX = SliderLeft + (int)MathF.Round(SliderTrackWidth * ratio);
            const int panelLeft = 18;
            const int panelTop = 18;
            const int panelWidth = 560;
            const int panelHeight = 154;

            _screenOverlay.AddRect(panelLeft, panelTop, panelWidth, panelHeight,
                new Vector4(0.05f, 0.07f, 0.1f, 0.88f),
                new Vector4(0.28f, 0.34f, 0.42f, 0.95f),
                stableId: 10,
                dirtySerial: _frameSerial);

            _screenOverlay.AddText(panelLeft + 16, panelTop + 14,
                "Raylib ISM Final Render Benchmark",
                22,
                new Vector4(0.95f, 0.98f, 1f, 1f),
                stableId: 11,
                dirtySerial: _frameSerial);

            _screenOverlay.AddText(panelLeft + 16, panelTop + 44,
                $"instances={_targetInstanceCount:n0}  buckets={stats.BucketCount}  visible={stats.VisibleCount:n0}  fps={EstimateFps():000}",
                18,
                new Vector4(0.82f, 0.94f, 1f, 1f),
                stableId: 12,
                dirtySerial: _frameSerial);

            float overlayBuild = timing?.ScreenOverlayBuildMs ?? 0f;
            float overlayPaint = timing?.ScreenOverlayPaintMs ?? 0f;
            float overlayComposite = timing?.ScreenOverlayCompositeMs ?? 0f;
            float overlayUpload = timing?.UiUploadMs ?? 0f;
            float overlayDraw = timing?.ScreenOverlayFinalDrawMs ?? 0f;
            float primitiveDraw = timing?.PrimitiveRenderMs ?? 0f;
            _screenOverlay.AddText(panelLeft + 16, panelTop + 68,
                $"bucketRebuild={stats.CpuBuildMs:0.00}ms  ismDraw={stats.CpuDrawMs:0.00}ms  build={overlayBuild:0.00}ms  paint={overlayPaint:0.00}ms  primitive={primitiveDraw:0.00}ms",
                16,
                new Vector4(0.67f, 0.93f, 0.72f, 1f),
                stableId: 13,
                dirtySerial: _frameSerial);

            _screenOverlay.AddText(panelLeft + 16, panelTop + 94,
                $"composite={overlayComposite:0.00}ms  upload={overlayUpload:0.00}ms  finalDraw={overlayDraw:0.00}ms  overlayTotal={(timing?.ScreenOverlayDrawMs ?? 0f):0.00}ms",
                16,
                new Vector4(0.93f, 0.8f, 0.56f, 1f),
                stableId: 14,
                dirtySerial: _frameSerial);

            _screenOverlay.AddText(panelLeft + 16, panelTop + 118,
                "drag slider: 3k -> 300k instances | bars/text are Skia final overlay stress",
                16,
                new Vector4(0.93f, 0.8f, 0.56f, 1f),
                stableId: 18,
                dirtySerial: _frameSerial);

            _screenOverlay.AddRect(SliderLeft, SliderTop, SliderTrackWidth, SliderTrackHeight,
                new Vector4(0.12f, 0.15f, 0.19f, 1f),
                new Vector4(0.36f, 0.44f, 0.56f, 1f),
                stableId: 15,
                dirtySerial: _frameSerial);

            _screenOverlay.AddRect(SliderLeft, SliderTop, Math.Max(8, knobCenterX - SliderLeft), SliderTrackHeight,
                new Vector4(0.21f, 0.58f, 0.96f, 0.92f),
                new Vector4(0.21f, 0.58f, 0.96f, 0f),
                stableId: 16,
                dirtySerial: _frameSerial);

            _screenOverlay.AddRect(knobCenterX - 8, SliderTop - 8, 16, SliderTrackHeight + 16,
                new Vector4(0.96f, 0.98f, 1f, 0.96f),
                new Vector4(0.06f, 0.08f, 0.12f, 1f),
                stableId: 17,
                dirtySerial: _frameSerial);
        }

        private void AddAnimatedHudBars(int screenWidth)
        {
            int columns = Math.Max(18, Math.Min(32, (screenWidth - 80) / 50));
            float baseX = 24f;
            float baseY = 172f;
            float cellW = 46f;

            for (int i = 0; i < HudBarCount; i++)
            {
                int col = i % columns;
                int row = i / columns;
                float wave = 0.5f + (0.5f * MathF.Sin((_time * 2.35f) + (i * 0.17f)));
                float phase = 0.5f + (0.5f * MathF.Cos((_time * 1.4f) + (i * 0.11f)));
                float value = Math.Clamp((wave * 0.82f) + (phase * 0.18f), 0.04f, 1f);
                value = MathF.Round(value * 64f) / 64f;
                _screenHud.TryAddBar(new ScreenHudBarItem
                {
                    StableId = 1000 + i,
                    DirtySerial = _frameSerial,
                    ScreenX = baseX + (col * cellW),
                    ScreenY = baseY + (row * 12f),
                    Width = cellW - 6f,
                    Height = 9f,
                    Value0 = value,
                    Color0 = new Vector4(0.14f, 0.17f, 0.2f, 0.94f),
                    Color1 = new Vector4(
                        0.24f + (0.62f * value),
                        0.18f + (0.76f * (1f - value)),
                        0.22f + (0.66f * MathF.Abs(MathF.Sin((_time * 0.7f) + (i * 0.09f)))),
                        0.98f)
                });
            }
        }

        private void AddAnimatedOverlayTexts(RaylibBenchmarkStats stats, PresentationTimingDiagnostics? timing, int screenWidth, int screenHeight)
        {
            int columns = Math.Max(4, Math.Min(7, Math.Max(4, (screenWidth - 960) / 170)));
            int startX = Math.Max(620, screenWidth - (columns * 170) - 24);
            int startY = 172;
            int maxRows = Math.Max(10, Math.Min(200, (screenHeight - startY - 24) / 14));
            int count = Math.Min(HudTextCount, columns * maxRows);

            for (int i = 0; i < count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                float hpWave = 0.5f + (0.5f * MathF.Sin((_time * 2.8f) + (i * 0.23f)));
                float textWave = 0.5f + (0.5f * MathF.Cos((_time * 1.9f) + (i * 0.19f)));
                int hp = 12 + (int)MathF.Round(hpWave * 88f);
                int energy = (int)MathF.Round(textWave * 100f);
                int x = startX + (col * 166);
                int y = startY + (row * 14);
                _screenOverlay.AddText(
                    x,
                    y,
                    $"hud[{i:000}] hp={hp:000} en={energy:000} draw={stats.CpuDrawMs:0.0} ui={(timing?.ScreenOverlayDrawMs ?? 0f):0.0}",
                    13,
                    new Vector4(0.92f, 0.96f, 1f, 0.95f),
                    stableId: 8000 + i,
                    dirtySerial: _frameSerial);
            }
        }

        private static int RoundInstanceCount(int count)
        {
            if (count < 10_000)
            {
                return ((count + 249) / 500) * 500;
            }

            if (count < 100_000)
            {
                return ((count + 999) / 2_000) * 2_000;
            }

            return ((count + 2_499) / 5_000) * 5_000;
        }

        private static int EstimateFps()
        {
            float frameTime = Rl.GetFrameTime();
            return frameTime > 0.0001f ? (int)MathF.Round(1f / frameTime) : 0;
        }
    }
}
