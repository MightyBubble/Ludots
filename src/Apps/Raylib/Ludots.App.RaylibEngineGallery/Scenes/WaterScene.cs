using System.Text.Json.Nodes;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 反射水面：RaylibWaterPass 反射/折射双 RenderTexture 通道 + 程序化 DUDV 扭曲，
    /// 海床网格在反射/折射两 pass 中各渲染一次，主 pass 由 water.fs 采样两张 RT。
    /// </summary>
    public sealed class WaterScene : IEngineScene
    {
        private const float WaterPlaneY = 0f;

        private readonly GalleryChunkTerrainSource _terrain = new(
            chunksPerSide: 24,
            chunkSpacingMeters: 12f,
            quadsPerChunk: 4,
            seed: 71,
            waterLevelMeters: WaterPlaneY,
            emitWater: true,
            islandMode: false);

        private RaylibWaterPass _water = new(GalleryAssetPaths.Instance);
        private RaylibTerrainRenderer _terrainRenderer = new() { VisibleRadius = 260f, SimplifiedCliffRadius = 120f, HeightScale = 1f };
        private RaylibSkyboxRenderer _skybox = new();
        private RaylibRenderEnvironmentConfig _environment = RaylibRenderEnvironmentConfig.CreateDefault();
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private bool _disposed;

        public string Id => "water";
        public string Title => "反射水面";
        public string Summary => "RaylibWaterPass 反射/折射双通道 + DUDV 扭曲";

        public void Load()
        {
            GalleryTextureFactory.WritePng("water_dudv.png", 256, 256, (x, y) =>
            {
                byte r = (byte)(128 + ((GalleryTextureFactory.SmoothNoise(x, y, 11) - 0.5f) * 70f));
                byte g = (byte)(128 + ((GalleryTextureFactory.SmoothNoise(y, x, 29) - 0.5f) * 70f));
                return new Color(r, g, 255, 255);
            });

            var waterConfig = new JsonObject
            {
                ["id"] = "gallery.ocean",
                ["backendId"] = RaylibWaterPass.BackendIdRaylib,
                ["enabled"] = true,
                ["mapIds"] = new JsonArray("gallery"),
                ["waterPlaneY"] = WaterPlaneY,
                ["resolutionScale"] = 0.5f,
                ["waveStrength"] = 0.035f,
                ["moveSpeed"] = 0.05f,
                ["dudvUri"] = "generated/water_dudv.png",
            };

            _water.LoadDescriptors(new MergedConfigEntry[] { new("gallery.ocean", waterConfig) });
            _water.EnsureActiveForMap("gallery");
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.42f);
            _shadowMap = new RaylibDirectionalShadowMap();
            _environment = _environment with
            {
                Skybox = _environment.Skybox with
                {
                    SizeMeters = 1200f,
                    ZenithColor = new System.Numerics.Vector3(0.22f, 0.46f, 0.74f),
                    HorizonColor = new System.Numerics.Vector3(0.80f, 0.86f, 0.90f),
                    GroundHazeColor = new System.Numerics.Vector3(0.40f, 0.52f, 0.60f),
                },
            };
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 90f);
            camera.target.Y = 1.5f;

            _lighting.SetDayPhase(0.42f);
            _environment = GallerySunSky.CreateConfig(_lighting, sizeMeters: 1200f);
            _shadowMap.BeginFrame(_lighting.SunDirectionToward, new System.Numerics.Vector3(camera.target.X, 3f, camera.target.Z), 180f);
            _terrainRenderer.RenderTerrainShadow(_terrain, camera, _shadowMap);
            _shadowMap.EndFrame();
            _terrainRenderer.ApplyFrameLighting(_lighting, _shadowMap, shadowTexelWorld: 0.25f);

            _water.EnsureRenderTargets(Rl.GetScreenWidth(), Rl.GetScreenHeight());
            _water.Advance(deltaSeconds);

            var deepClear = new Color(8, 24, 38, 255);
            Rl.ClearBackground(deepClear);

            Camera3D reflectionCamera = _water.BuildReflectionCamera(camera);
            _water.BeginReflectionPass(deepClear);
            Rl.BeginMode3D(reflectionCamera);
            _skybox.Draw(reflectionCamera, totalTimeSeconds, _environment);
            _terrainRenderer.RenderTerrainOnly(_terrain, reflectionCamera);
            Rl.EndMode3D();
            _water.EndPass();

            _water.BeginRefractionPass(deepClear);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, _environment);
            _terrainRenderer.RenderTerrainOnly(_terrain, camera);
            Rl.EndMode3D();
            _water.EndPass();

            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, _environment);
            _terrainRenderer.BindReflectiveWater(_water);
            _terrainRenderer.Render(_terrain, camera);
            Rl.EndMode3D();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _terrainRenderer?.Dispose();
            _skybox?.Dispose();
            _water?.Dispose();
            _shadowMap?.Dispose();
            _water = null!;
            _terrainRenderer = null!;
            _skybox = null!;
            _disposed = true;
        }
    }
}
