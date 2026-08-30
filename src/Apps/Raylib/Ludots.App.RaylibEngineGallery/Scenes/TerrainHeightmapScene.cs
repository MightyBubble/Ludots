using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>视觉高度图：RaylibContinuousHeightmapRenderer 消费程序化岛屿高度场，绝对海拔色带 + 水下陆架。</summary>
    public sealed class TerrainHeightmapScene : IEngineScene
    {
        private readonly GalleryIslandHeightmap _heightmap = new(
            chunksPerSide: 16,
            samplesPerChunk: 33,
            worldSizeMeters: 480,
            seed: 47);

        private RaylibContinuousHeightmapRenderer _renderer = new() { VisibleRadiusCm = 90_000f };
        private readonly RaylibSkyboxRenderer _skybox = new();
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private bool _disposed;

        public string Id => "terrain_heightmap";
        public string Title => "视觉高度图";
        public string Summary => "RaylibContinuousHeightmapRenderer 程序化岛屿高度场";

        public void Load()
        {
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.46f);
            _shadowMap = new RaylibDirectionalShadowMap();
            _renderer.ApplyFrameLighting(_lighting);
            _renderer.AbsoluteColorSeaLevelCm = _heightmap.RenderProfile.SeaLevelCm;
            _renderer.AbsoluteColorPeakSpanCm = _heightmap.RenderProfile.AbsoluteColorPeakSpanCm;
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 620f);
            camera.target.Y = 2f;

            float phase = 0.40f + (0.14f * MathF.Sin((float)totalTimeSeconds * 0.04f));
            _lighting.SetDayPhase(phase);
            _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(camera.target.X, 12f, camera.target.Z), 360f);
            _renderer.RenderShadow(_heightmap, camera, _shadowMap);
            _shadowMap.EndFrame();
            _renderer.ApplyFrameLighting(_lighting, _shadowMap, shadowTexelWorld: 0.55f);

            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_lighting, sizeMeters: 2200f);
            Rl.ClearBackground(skyConfig.Skybox.ClearColor);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);
            _renderer.Render(_heightmap, camera);
            Rl.EndMode3D();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _renderer?.Dispose();
            _shadowMap?.Dispose();
            _skybox.Dispose();
            _renderer = null!;
            _disposed = true;
        }
    }
}
