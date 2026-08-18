using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using SkiaSharp;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>Skia 2D 覆盖层：3D 场景上叠 RaylibSkiaRenderer 合成 HUD，面板绘制走 SkiaRasterLayer 分层。</summary>
    public sealed class SkiaOverlayScene : IEngineScene
    {
        private readonly Queue<float> _frameMs = new();
        private readonly float[] _cubePhases = new float[8];
        private readonly GalleryLitProps _litProps = new();

        private RaylibSkiaRenderer _skia = null!;
        private SkiaRasterLayer _panelLayer = new();
        private SKTypeface? _typeface;
        private bool _disposed;

        public string Id => "skia_overlay";
        public string Title => "Skia 2D 覆盖层";
        public string Summary => "RaylibSkiaRenderer + SkiaRasterLayer HUD 合成";

        public void Load()
        {
            _litProps.Load();
            _skia = new RaylibSkiaRenderer(Rl.GetScreenWidth(), Rl.GetScreenHeight());
            _panelLayer.Resize(Rl.GetScreenWidth(), Rl.GetScreenHeight());
            _typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal) ?? SKTypeface.Default;
            for (int i = 0; i < _cubePhases.Length; i++)
            {
                _cubePhases[i] = i * 0.78f;
            }
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 36f);
            TrackFrameMs(deltaSeconds);

            Rl.ClearBackground(new Color(10, 12, 20, 255));
            _litProps.BeginFrame(camera.position);
            Rl.BeginMode3D(camera);
            Rl.DrawGrid(24, 3f);
            float t = (float)totalTimeSeconds;
            for (int i = 0; i < _cubePhases.Length; i++)
            {
                float angle = (i * MathF.Tau / _cubePhases.Length) + (t * 0.4f);
                float bob = MathF.Sin((t * 1.6f) + _cubePhases[i]);
                var position = new Vector3(MathF.Cos(angle) * 12f, 1.8f + (bob * 1.1f), MathF.Sin(angle) * 12f);
                byte channel = (byte)(110 + (i * 18));
                _litProps.DrawCube(
                    position,
                    new Vector3(2.4f),
                    new Vector4(channel / 255f, (240 - channel) / 255f, 190 / 255f, 1f),
                    roughness: 0.6f);
            }

            _litProps.DrawSphere(Vector3.Zero, 2.6f, new Vector4(0.92f, 0.75f, 0.35f, 1f));
            Rl.EndMode3D();

            DrawHud(deltaSeconds, t);
            _skia.RenderToScreen();
        }

        private void TrackFrameMs(float deltaSeconds)
        {
            _frameMs.Enqueue(deltaSeconds * 1000f);
            while (_frameMs.Count > 96)
            {
                _frameMs.Dequeue();
            }
        }

        private void DrawHud(float deltaSeconds, float t)
        {
            SKCanvas canvas = _panelLayer.Canvas;
            _panelLayer.Clear();

            using (var backdrop = new SKPaint { Color = new SKColor(16, 20, 32, 205) })
            {
                canvas.DrawRoundRect(24f, 24f, 430f, 210f, 14f, 14f, backdrop);
            }

            using (var frame = new SKPaint { Color = new SKColor(120, 200, 255, 230), Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f })
            {
                canvas.DrawRoundRect(24f, 24f, 430f, 210f, 14f, 14f, frame);
            }

            if (_typeface != null)
            {
                using var titleFont = new SKFont(_typeface, 21f);
                using var bodyFont = new SKFont(_typeface, 15f);
                using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
                using var bodyPaint = new SKPaint { Color = new SKColor(190, 205, 225, 255), IsAntialias = true };
                canvas.DrawText("LUDOTS SKIA HUD", 44f, 60f, titleFont, titlePaint);
                canvas.DrawText($"frame {deltaSeconds * 1000f:0.00} ms   cubes {_cubePhases.Length}   t {t:0.0}s", 44f, 90f, bodyFont, bodyPaint);

                float barX = 44f;
                float baseY = 178f;
                foreach (float ms in _frameMs)
                {
                    float height = Math.Clamp(ms, 0f, 40f) * 2.2f;
                    using var bar = new SKPaint { Color = new SKColor(110, 220, 160, 235) };
                    canvas.DrawRect(barX, baseY - height, 3.4f, height, bar);
                    barX += 4.2f;
                }

                using var axis = new SKPaint { Color = new SKColor(140, 150, 170, 255), StrokeWidth = 1f, Style = SKPaintStyle.Stroke };
                canvas.DrawLine(44f, baseY, 434f, baseY, axis);
                canvas.DrawText("frame time history (0-40ms)", 44f, 200f, bodyFont, bodyPaint);
            }

            using (var compassEdge = new SKPaint { Color = new SKColor(255, 200, 90, 220), Style = SKPaintStyle.Stroke, StrokeWidth = 3f })
            {
                canvas.DrawCircle(Rl.GetScreenWidth() - 120f, 120f, 64f + (MathF.Sin(t * 2f) * 3f), compassEdge);
                canvas.DrawLine(Rl.GetScreenWidth() - 120f, 120f, Rl.GetScreenWidth() - 120f + (MathF.Cos(t) * 58f), 120f + (MathF.Sin(t) * 58f), compassEdge);
            }

            _panelLayer.SetHasContent(true);
            _skia.ClearTransparent();
            _panelLayer.DrawTo(_skia.Canvas);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _panelLayer?.Dispose();
            _skia?.Dispose();
            _typeface?.Dispose();
            _litProps.Dispose();
            _skia = null!;
            _disposed = true;
        }
    }
}
