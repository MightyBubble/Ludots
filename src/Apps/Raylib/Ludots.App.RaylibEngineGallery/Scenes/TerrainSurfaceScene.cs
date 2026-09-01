using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>地表着色：RaylibTerrainRenderer 消费画廊程序化 chunk 源，高地分带顶点色 + 湖面水体网格。</summary>
    [EngineSceneComponent("terrain_surface")]
    public sealed class TerrainSurfaceScene : IEngineSceneComponent
    {
        private readonly GalleryChunkTerrainSource _terrain = new(
            chunksPerSide: 32,
            chunkSpacingMeters: 14f,
            quadsPerChunk: 4,
            seed: 23,
            waterLevelMeters: 0f,
            emitWater: true,
            islandMode: true);

        private RaylibTerrainRenderer _renderer = new() { VisibleRadius = 420f, SimplifiedCliffRadius = 200f, HeightScale = 1f };
        private readonly RaylibSkyboxRenderer _skybox = new();
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private bool _disposed;

        public void Load()
        {
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.45f);
            _shadowMap = new RaylibDirectionalShadowMap();
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 260f);
            camera.target.Y = 8f;

            float phase = 0.38f + (0.12f * MathF.Sin((float)totalTimeSeconds * 0.05f));
            _lighting.SetDayPhase(phase);
            _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(camera.target.X, 6f, camera.target.Z), 260f);
            _renderer.RenderTerrainShadow(_terrain, camera, _shadowMap);
            _shadowMap.EndFrame();
            _renderer.ApplyFrameLighting(_lighting, _shadowMap, shadowTexelWorld: 0.35f);
            _renderer.ClearReflectiveWater();

            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_lighting, sizeMeters: 1800f);
            Rl.ClearBackground(skyConfig.Skybox.ClearColor);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);
            _renderer.Render(_terrain, camera);
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
