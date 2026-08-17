using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>距离雾与环境色：RaylibRenderEnvironmentRenderer 组帧，一列沿视线远去的方碑展示雾衰减。</summary>
    public sealed class AtmosphereFogScene : IEngineScene
    {
        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private readonly Vector4[] _pillarTints =
        {
            new(0.95f, 0.42f, 0.36f, 1f),
            new(0.98f, 0.72f, 0.32f, 1f),
            new(0.42f, 0.80f, 0.52f, 1f),
            new(0.40f, 0.62f, 0.95f, 1f),
            new(0.78f, 0.52f, 0.92f, 1f),
        };

        private RaylibRenderEnvironmentRenderer _environment = null!;
        private RaylibRenderEnvironmentConfig _config = RaylibRenderEnvironmentConfig.CreateDefault();
        private RaylibFrameLighting _lighting = null!;
        private RaylibPrimitiveRenderer _primitives = null!;
        private bool _disposed;

        public string Id => "atmosphere_fog";
        public string Title => "距离雾与环境";
        public string Summary => "RaylibRenderEnvironmentRenderer 雾 + 环境色调";

        public void Load()
        {
            _meshes.Register("gallery.cube", MeshAssetDescriptor.Primitive(101, PrimitiveMeshKind.Cube));
            _meshes.Register("gallery.sphere", MeshAssetDescriptor.Primitive(102, PrimitiveMeshKind.Sphere));

            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.40f);
            _config = RaylibRenderEnvironmentConfig.CreateDefault() with
            {
                Lighting = _lighting.FogEnabled
                    ? RaylibRenderEnvironmentConfig.CreateDefault().Lighting with
                    {
                        FogColor = _lighting.FogColor,
                        FogNearMeters = _lighting.FogParams.Y,
                        FogFarMeters = _lighting.FogParams.Z,
                        FogDensity = _lighting.FogParams.X,
                    }
                    : RaylibRenderEnvironmentConfig.CreateDefault().Lighting,
                Skybox = RaylibRenderEnvironmentConfig.CreateDefault().Skybox with
                {
                    SizeMeters = 1200f,
                    ZenithColor = new Vector3(0.34f, 0.50f, 0.68f),
                    HorizonColor = _lighting.FogColor,
                    GroundHazeColor = _lighting.FogColor * 0.8f,
                },
            };
            _environment = new RaylibRenderEnvironmentRenderer(_config);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Immediate,
                vfs: null,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 420f);
            camera.target = new Vector3(0f, 18f, -180f);

            _lighting.SetDayPhase(0.40f);
            _environment.BeginWorldFrame(Rl.GetScreenWidth(), Rl.GetScreenHeight(), new Color(120, 146, 168, 255));

            Rl.BeginMode3D(camera);
            _environment.DrawSkybox(camera, totalTimeSeconds);

            _primitives.ApplyFrameLighting(_lighting, camera.position);
            _snapshot.BeginFrame();
            for (int i = 0; i < 20; i++)
            {
                float z = -20f - (i * 34f);
                Vector4 tint = _pillarTints[i % _pillarTints.Length];
                _snapshot.Add(GalleryItems.Mesh(101, 100 + i, new Vector3(-24f, 16f, z), new Vector3(10f, 32f, 10f), tint));
                _snapshot.Add(GalleryItems.Mesh(102, 200 + i, new Vector3(26f, 10f, z), new Vector3(12f), tint * 0.7f));
                _snapshot.Add(GalleryItems.Mesh(101, 300 + i, new Vector3(0f, 6f, z - 17f), new Vector3(20f, 12f, 8f), tint * 0.55f));
            }

            _primitives.Draw(_snapshot, camera, _meshes, timeSeconds: totalTimeSeconds);
            Rl.EndMode3D();

            _environment.EndWorldFrame(totalTimeSeconds);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _primitives?.Dispose();
            _environment?.Dispose();
            _environment = null!;
            _disposed = true;
        }
    }
}
