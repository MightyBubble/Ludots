using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>后处理调色：RaylibPostProcessRenderer 世界帧 RT，曝光/对比/饱和/暗角随时间正弦调制。</summary>
    public sealed class PostProcessScene : IEngineScene
    {
        private RaylibPostProcessRenderer _postProcess = new();
        private RaylibPostProcessConfig _baseConfig = RaylibPostProcessConfig.CreateDefault();
        private bool _disposed;

        public string Id => "postprocess";
        public string Title => "后处理调色";
        public string Summary => "RaylibPostProcessRenderer 曝光/对比/饱和/暗角调制";

        public void Load()
        {
            _baseConfig = RaylibPostProcessConfig.CreateDefault();
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 42f);
            float t = (float)totalTimeSeconds;

            RaylibPostProcessConfig config = _baseConfig with
            {
                Enabled = true,
                Exposure = 0.78f + (0.6f * (0.5f + (0.5f * MathF.Sin(t * 0.9f)))),
                Contrast = 0.85f + (0.55f * (0.5f + (0.5f * MathF.Sin(t * 0.6f + 1.7f)))),
                Saturation = 0.25f + (1.35f * (0.5f + (0.5f * MathF.Sin(t * 0.75f + 3.1f)))),
                VignetteStrength = 0.05f + (0.38f * (0.5f + (0.5f * MathF.Sin(t * 1.1f + 0.6f)))),
            };

            _postProcess.BeginWorldFrame(Rl.GetScreenWidth(), Rl.GetScreenHeight(), new Color(16, 20, 30, 255), config);

            Rl.BeginMode3D(camera);
            Rl.DrawGrid(24, 3f);
            for (int i = 0; i < 8; i++)
            {
                float angle = (i * MathF.Tau / 8f) + (t * 0.3f);
                var position = new Vector3(MathF.Cos(angle) * 12f, 1.6f + (i * 0.22f), MathF.Sin(angle) * 12f);
                byte r = (byte)(90 + (i * 20));
                byte g = (byte)(140 - (i * 12));
                byte b = (byte)(200 - (i * 18));
                Rl.DrawCube(position, 2.6f, 2.6f + (i * 0.2f), 2.6f, new Color(r, g, b, 255));
                Rl.DrawSphere(position + new Vector3(0f, 3.2f, 0f), 0.8f, new Color(240, 210, 120, 255));
            }

            Rl.DrawCube(new Vector3(0f, 0.4f, 0f), 5f, 0.8f, 5f, new Color(70, 76, 92, 255));
            Rl.EndMode3D();

            _postProcess.EndWorldFrame(totalTimeSeconds, config);
            GalleryFont.Draw(
                $"exposure {config.Exposure:0.00}  contrast {config.Contrast:0.00}  saturation {config.Saturation:0.00}  vignette {config.VignetteStrength:0.00}",
                12,
                28,
                20,
                GalleryColors.RayWhite);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _postProcess?.Dispose();
            _postProcess = null!;
            _disposed = true;
        }
    }
}
