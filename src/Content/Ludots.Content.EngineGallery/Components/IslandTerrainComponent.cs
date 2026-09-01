using System.Numerics;
using System.Text.Json;
using Ludots.Raylib.Render;
using Ludots.Raylib.SceneKit;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Content.EngineGallery.Components
{
    /// <summary>
    /// 组合场景的岛屿地形基座：程序化高度场 + 全帧环境（天/雾/自身阴影），
    /// 不触碰相机——组合场景的相机由关卡文档 camera 声明唯一决定。
    /// </summary>
    [EngineSceneComponent("island_terrain")]
    public sealed class IslandTerrainComponent : IEngineSceneComponent, IEngineSceneComponentConfigurable
    {
        private GalleryIslandHeightmap _heightmap = new(chunksPerSide: 8, samplesPerChunk: 33, worldSizeMeters: 160, seed: 47);
        private int _worldSizeMeters = 160;
        private float _dayPhase = 0.46f;
        private RaylibContinuousHeightmapRenderer _renderer = new() { VisibleRadiusCm = 60_000f };
        private readonly RaylibSkyboxRenderer _skybox = new();
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private bool _disposed;

        public void Configure(JsonElement config)
        {
            _worldSizeMeters = ReadInt(config, "worldSizeMeters", 160);
            _heightmap = new GalleryIslandHeightmap(
                chunksPerSide: ReadInt(config, "chunksPerSide", 8),
                samplesPerChunk: ReadInt(config, "samplesPerChunk", 33),
                worldSizeMeters: _worldSizeMeters,
                seed: ReadInt(config, "seed", 47));
            _renderer = new RaylibContinuousHeightmapRenderer { VisibleRadiusCm = _worldSizeMeters * 375f };
            if (config.TryGetProperty("dayPhase", out JsonElement phase) && phase.ValueKind == JsonValueKind.Number)
            {
                _dayPhase = phase.GetSingle();
            }
        }

        public void Load()
        {
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: _dayPhase);
            _shadowMap = new RaylibDirectionalShadowMap();
            _renderer.ApplyFrameLighting(_lighting);
            _renderer.AbsoluteColorSeaLevelCm = _heightmap.RenderProfile.SeaLevelCm;
            _renderer.AbsoluteColorPeakSpanCm = _heightmap.RenderProfile.AbsoluteColorPeakSpanCm;
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            _lighting.SetDayPhase(_dayPhase);
            float shadowRadius = _worldSizeMeters * 0.9f;
            _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(0f, 12f, 0f), shadowRadius);
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

        private static int ReadInt(JsonElement config, string name, int fallback)
        {
            return config.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : fallback;
        }
    }
}
