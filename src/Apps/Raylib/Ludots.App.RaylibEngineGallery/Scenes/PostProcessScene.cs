using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>后处理调色：RaylibPostProcessRenderer 世界帧 RT，曝光/对比/饱和/暗角随时间正弦调制。</summary>
    [EngineSceneComponent("postprocess")]
    public sealed class PostProcessScene : IEngineSceneComponent
    {
        private readonly GalleryLitProps _litProps = new();
        private readonly RaylibSkyboxRenderer _skybox = new();
        private RaylibPostProcessRenderer _postProcess = new();
        private RaylibPostProcessConfig _baseConfig = RaylibPostProcessConfig.CreateDefault();
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private bool _disposed;
        public void Load()
        {
            _litProps.Load();
            _baseConfig = RaylibPostProcessConfig.CreateDefault();
            _shadowMap = new RaylibDirectionalShadowMap();
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

            _litProps.Lighting.SetDayPhase(_litProps.DayPhase01);
            _shadowMap.BeginFrame(_litProps.Lighting.SunDirectionToward, Vector3.Zero, 30f);
            DrawScenePropShadows(t);
            _shadowMap.EndFrame();

            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_litProps.Lighting, sizeMeters: 1000f);
            _postProcess.BeginWorldFrame(Rl.GetScreenWidth(), Rl.GetScreenHeight(), skyConfig.Skybox.ClearColor, config);
            _litProps.BeginFrame(camera.position, _shadowMap, shadowTexelWorld: 0.06f);

            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);
            DrawSceneProps(t);
            Rl.EndMode3D();

            _postProcess.EndWorldFrame(totalTimeSeconds, config);
            GalleryFont.Draw(
                $"exposure {config.Exposure:0.00}  contrast {config.Contrast:0.00}  saturation {config.Saturation:0.00}  vignette {config.VignetteStrength:0.00}",
                12,
                28,
                20,
                GalleryColors.RayWhite);
        }

        private void DrawSceneProps(float t)
        {
            _litProps.DrawCube(new Vector3(0f, -0.08f, 0f), new Vector3(42f, 0.16f, 42f), GalleryColors.ShadowReceiverGray, roughness: 0.9f);
            for (int i = 0; i < 8; i++)
            {
                float angle = (i * MathF.Tau / 8f) + (t * 0.3f);
                var position = new Vector3(MathF.Cos(angle) * 12f, 1.6f + (i * 0.22f), MathF.Sin(angle) * 12f);
                byte r = (byte)(90 + (i * 20));
                byte g = (byte)(140 - (i * 12));
                byte b = (byte)(200 - (i * 18));
                _litProps.DrawCube(
                    position,
                    new Vector3(2.6f, 2.6f + (i * 0.2f), 2.6f),
                    new Vector4(r / 255f, g / 255f, b / 255f, 1f));
                _litProps.DrawSphere(
                    position + new Vector3(0f, 3.2f, 0f),
                    0.8f,
                    new Vector4(0.94f, 0.82f, 0.47f, 1f));
            }

            _litProps.DrawCube(new Vector3(0f, 0.4f, 0f), new Vector3(5f, 0.8f, 5f), new Vector4(0.27f, 0.30f, 0.36f, 1f));
        }

        private void DrawScenePropShadows(float t)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = (i * MathF.Tau / 8f) + (t * 0.3f);
                var position = new Vector3(MathF.Cos(angle) * 12f, 1.6f + (i * 0.22f), MathF.Sin(angle) * 12f);
                _litProps.DrawCubeShadow(_shadowMap, position, new Vector3(2.6f, 2.6f + (i * 0.2f), 2.6f));
                _litProps.DrawSphereShadow(_shadowMap, position + new Vector3(0f, 3.2f, 0f), 0.8f);
            }

            _litProps.DrawCubeShadow(_shadowMap, new Vector3(0f, 0.4f, 0f), new Vector3(5f, 0.8f, 5f));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _postProcess?.Dispose();
            _shadowMap?.Dispose();
            _skybox.Dispose();
            _litProps.Dispose();
            _postProcess = null!;
            _shadowMap = null!;
            _disposed = true;
        }
    }
}
