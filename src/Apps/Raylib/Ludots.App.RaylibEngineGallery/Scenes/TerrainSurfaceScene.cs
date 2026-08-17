using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>地表着色：RaylibTerrainRenderer 消费画廊程序化 chunk 源，高地分带顶点色 + 湖面水体网格。</summary>
    public sealed class TerrainSurfaceScene : IEngineScene
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
        private RaylibFrameLighting _lighting = null!;
        private bool _disposed;

        public string Id => "terrain_surface";
        public string Title => "地表着色";
        public string Summary => "RaylibTerrainRenderer chunk 网格 + 分带顶点色";

        public void Load()
        {
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.45f);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 260f);
            camera.target.Y = 8f;

            float phase = 0.38f + (0.12f * MathF.Sin((float)totalTimeSeconds * 0.05f));
            _lighting.SetDayPhase(phase);
            _renderer.ApplyFrameLighting(_lighting);
            _renderer.ClearReflectiveWater();

            Rl.ClearBackground(new Color(96, 130, 158, 255));
            Rl.BeginMode3D(camera);
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
            _renderer = null!;
            _disposed = true;
        }
    }
}
