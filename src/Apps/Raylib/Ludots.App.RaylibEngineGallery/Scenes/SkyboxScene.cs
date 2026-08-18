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
        private RaylibRenderEnvironmentConfig _config = RaylibRenderEnvironmentConfig.CreateDefault();
        private bool _disposed;

        public string Id => "skybox";
        public string Title => "天空盒";
        public string Summary => "RaylibSkyboxRenderer 程序化渐变天空 + 太阳方位驱动";

        public void Load()
        {
            _litProps.Load();
            _config = RaylibRenderEnvironmentConfig.CreateDefault() with
            {
                Skybox = new RaylibSkyboxConfig(
                    Enabled: true,
                    SizeMeters: 1200f,
                    ZenithColor: new Vector3(0.10f, 0.30f, 0.62f),
                    HorizonColor: new Vector3(0.84f, 0.72f, 0.58f),
                    GroundHazeColor: new Vector3(0.46f, 0.42f, 0.38f),
                    ClearColor: new Color(120, 150, 180, 255),
                    DeepClearColor: new Color(6, 10, 16, 255)),
            };
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 46f);

            float sunAngle = (float)(totalTimeSeconds * 0.12f);
            Vector3 sunDirection = Vector3.Normalize(new Vector3(
                MathF.Cos(sunAngle),
                0.55f + (0.35f * MathF.Sin(sunAngle * 0.7f)),
                MathF.Sin(sunAngle)));
            RaylibRenderEnvironmentConfig config = _config with
            {
                Lighting = _config.Lighting with { SunDirection = sunDirection },
            };

            _litProps.BeginFrame(camera.position);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, config);

            Rl.DrawGrid(40, 4f);
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

            Rl.EndMode3D();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _skybox?.Dispose();
            _litProps.Dispose();
            _skybox = null!;
            _disposed = true;
        }
    }
}
