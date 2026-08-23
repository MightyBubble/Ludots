using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>昼夜天空：RaylibSkyEnvironment 手工 MergedConfigEntry 渐变 + 全天相位驱动，日光/环境随相位联动。</summary>
    public sealed class SkyDayNightScene : IEngineScene
    {
        private const float CycleSeconds = 48f;

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private RaylibSkyEnvironment _sky = new(GalleryAssetPaths.Instance);
        private RaylibFrameLighting _lighting = null!;
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private bool _disposed;

        public string Id => "sky_daynight";
        public string Title => "昼夜天空";
        public string Summary => "RaylibSkyEnvironment 渐变烘焙 + 全天相位驱动";

        public void Load()
        {
            _meshes.Register("gallery.cube", MeshAssetDescriptor.Primitive(101, PrimitiveMeshKind.Cube));
            _meshes.Register("gallery.sphere", MeshAssetDescriptor.Primitive(102, PrimitiveMeshKind.Sphere));

            var skyConfig = new JsonObject
            {
                ["id"] = "gallery.daynight",
                ["backendId"] = RaylibSkyEnvironment.BackendIdRaylib,
                ["enabled"] = true,
                ["mapIds"] = new JsonArray("gallery"),
                ["clearSampleV"] = 0.85f,
                ["initialPhase"] = 0.3f,
                ["gradientWidth"] = 256,
                ["gradientHeight"] = 64,
                ["gradientStops"] = new JsonArray(
                    new JsonObject
                    {
                        ["phase"] = 0f,
                        ["zenith"] = new JsonArray(0.02f, 0.03f, 0.09f),
                        ["horizon"] = new JsonArray(0.10f, 0.11f, 0.22f),
                    },
                    new JsonObject
                    {
                        ["phase"] = 0.24f,
                        ["zenith"] = new JsonArray(0.14f, 0.18f, 0.44f),
                        ["horizon"] = new JsonArray(0.94f, 0.56f, 0.30f),
                    },
                    new JsonObject
                    {
                        ["phase"] = 0.38f,
                        ["zenith"] = new JsonArray(0.16f, 0.36f, 0.66f),
                        ["horizon"] = new JsonArray(0.76f, 0.84f, 0.90f),
                    },
                    new JsonObject
                    {
                        ["phase"] = 0.62f,
                        ["zenith"] = new JsonArray(0.12f, 0.30f, 0.60f),
                        ["horizon"] = new JsonArray(0.80f, 0.86f, 0.92f),
                    },
                    new JsonObject
                    {
                        ["phase"] = 0.78f,
                        ["zenith"] = new JsonArray(0.16f, 0.14f, 0.36f),
                        ["horizon"] = new JsonArray(0.96f, 0.44f, 0.22f),
                    },
                    new JsonObject
                    {
                        ["phase"] = 1f,
                        ["zenith"] = new JsonArray(0.02f, 0.03f, 0.09f),
                        ["horizon"] = new JsonArray(0.10f, 0.11f, 0.22f),
                    }),
            };

            _sky.LoadDescriptors(new MergedConfigEntry[] { new("gallery.daynight", skyConfig) });
            _sky.EnsureActiveForMap("gallery");
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.3f);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Immediate,
                vfs: null,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register);
            _shadowMap = new RaylibDirectionalShadowMap();
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 34f);

            float phase = (((float)totalTimeSeconds + (CycleSeconds * 0.34f)) % CycleSeconds) / CycleSeconds;
            _sky.ApplyDayPhase(phase);
            _lighting.SetDayPhase(phase);
            _sky.SetSun(_lighting.SunDirectionToward, _lighting.LightColor);

            _snapshot.BeginFrame();
            _snapshot.Add(GalleryItems.Mesh(101, 999, new Vector3(0f, -0.10f, 0f), new Vector3(42f, 0.20f, 36f), GalleryColors.ShadowReceiverGray));
            _snapshot.Add(GalleryItems.Mesh(101, 1, new Vector3(-6f, 1.6f, 0f), new Vector3(3.2f), new Vector4(0.92f, 0.90f, 0.86f, 1f)));
            _snapshot.Add(GalleryItems.Mesh(102, 2, new Vector3(5f, 1.5f, -3f), new Vector3(3f), new Vector4(0.86f, 0.60f, 0.34f, 1f)));
            for (int i = 0; i < 6; i++)
            {
                float angle = (i * MathF.Tau / 6f) + ((float)totalTimeSeconds * 0.2f);
                _snapshot.Add(GalleryItems.Mesh(
                    101,
                    10 + i,
                    new Vector3(MathF.Cos(angle) * 14f, 0.9f + (MathF.Sin(angle * 3f) * 0.4f), MathF.Sin(angle) * 14f),
                    new Vector3(1.6f, 1.8f, 1.6f),
                    new Vector4(0.55f, 0.62f, 0.72f, 1f)));
            }

            _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(0f, 1f, 0f), 28f);
            _primitives.DrawShadow(_snapshot, _shadowMap, _meshes, camera);
            _shadowMap.EndFrame();

            Rl.ClearBackground(_sky.ResolveClearColor());
            CameraRenderState3D cameraState = GalleryCamera.StateOf(camera);

            Rl.BeginMode3D(camera);
            _sky.Draw(camera, cameraState);

            _primitives.ApplyFrameLighting(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.06f);
            _primitives.Draw(_snapshot, camera, _meshes, timeSeconds: totalTimeSeconds);
            Rl.EndMode3D();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _primitives?.Dispose();
            _shadowMap?.Dispose();
            _sky?.Dispose();
            _sky = null!;
            _shadowMap = null!;
            _disposed = true;
        }
    }
}
