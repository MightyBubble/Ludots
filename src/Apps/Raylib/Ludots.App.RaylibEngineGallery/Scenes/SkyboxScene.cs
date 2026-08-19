using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>天空盒：RaylibSkyboxRenderer 渐变天空，太阳方位随时间绕行。</summary>
    public sealed class SkyboxScene : IEngineScene
    {
        private readonly GalleryLitProps _litProps = new();
        private RaylibSkyboxRenderer _skybox = new();
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private bool _disposed;

        public string Id => "skybox";
        public string Title => "天空盒";
        public string Summary => "RaylibSkyboxRenderer 程序化渐变天空 + 太阳方位驱动";

        public void Load()
        {
            _litProps.Load();
            _shadowMap = new RaylibDirectionalShadowMap();
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 46f);

            float t = (float)totalTimeSeconds;
            _litProps.DayPhase01 = 0.38f + (float)(totalTimeSeconds * 0.012 % 0.28f);
            _litProps.Lighting.SetDayPhase(_litProps.DayPhase01);
            _shadowMap.BeginFrame(_litProps.Lighting.SunDirectionToward, Vector3.Zero, 42f);
            DrawPropShadows(t);
            _shadowMap.EndFrame();

            RaylibRenderEnvironmentConfig config = GallerySunSky.CreateConfig(_litProps.Lighting, sizeMeters: 1200f);
            Rl.ClearBackground(config.Skybox.ClearColor);
            _litProps.BeginFrame(camera.position, _shadowMap, shadowTexelWorld: 0.08f);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, config);
            DrawProps(t);
            Rl.EndMode3D();
        }

        private void DrawProps(float t)
        {
            _litProps.DrawCube(new Vector3(0f, -0.08f, 0f), new Vector3(70f, 0.16f, 70f), GalleryColors.ShadowReceiverGray, roughness: 0.9f);
            for (int i = 0; i < 7; i++)
            {
                float angle = i * MathF.Tau / 7f;
                var position = new Vector3(MathF.Cos(angle) * 26f, 2.2f, MathF.Sin(angle) * 26f);
                _litProps.DrawCube(position, new Vector3(4f, 4.4f, 4f), new Vector4(0.20f, 0.23f, 0.29f, 1f), roughness: 0.85f);
                _litProps.DrawCube(
                    position + new Vector3(0f, 5.6f, 0f),
                    new Vector3(2.2f),
                    new Vector4(0.59f, 0.55f, 0.47f, 1f),
                    roughness: 0.6f);
            }
        }

        private void DrawPropShadows(float t)
        {
            for (int i = 0; i < 7; i++)
            {
                float angle = i * MathF.Tau / 7f;
                var position = new Vector3(MathF.Cos(angle) * 26f, 2.2f, MathF.Sin(angle) * 26f);
                _litProps.DrawCubeShadow(_shadowMap, position, new Vector3(4f, 4.4f, 4f));
                _litProps.DrawCubeShadow(_shadowMap, position + new Vector3(0f, 5.6f, 0f), new Vector3(2.2f));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _skybox?.Dispose();
            _shadowMap?.Dispose();
            _litProps.Dispose();
            _skybox = null!;
            _shadowMap = null!;
            _disposed = true;
        }
    }
}
