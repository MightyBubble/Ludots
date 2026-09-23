using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>帧光照：RaylibFrameLighting 从默认 JSON 装载，日光方向/强度与环境色随相位摆动，物体即时反馈。</summary>
    public sealed class FrameLightingScene : IEngineScene
    {
        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();

        private RaylibFrameLighting _lighting = null!;
        private RaylibPrimitiveRenderer _primitives = null!;
        private readonly RaylibSkyboxRenderer _skybox = new();
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private bool _disposed;

        public string Id => "frame_lighting";
        public string Title => "帧光照";
        public string Summary => "RaylibFrameLighting 日光/环境全天摆动";

        public void Load()
        {
            _meshes.Register("gallery.cube", MeshAssetDescriptor.Primitive(101, PrimitiveMeshKind.Cube));
            _meshes.Register("gallery.sphere", MeshAssetDescriptor.Primitive(102, PrimitiveMeshKind.Sphere));
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.3f);
            _shadowMap = new RaylibDirectionalShadowMap();
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Immediate,
                vfs: null,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 38f);

            float phase = 0.24f + (0.5f * (0.5f + (0.5f * MathF.Sin((float)totalTimeSeconds * 0.18f))));
            _lighting.SetDayPhase(phase);
            Vector3 sun = _lighting.SunDirectionToward;
            BuildSnapshot(totalTimeSeconds);

            _shadowMap.BeginFrame(sun, new Vector3(0f, 2f, 0f), 28f);
            _primitives.DrawShadow(_snapshot, _shadowMap, _meshes, camera);
            _shadowMap.EndFrame();

            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_lighting, sizeMeters: 1200f);
            Rl.ClearBackground(skyConfig.Skybox.ClearColor);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);
            _primitives.ApplyFrameLighting(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.08f);
            _primitives.Draw(_snapshot, camera, _meshes, timeSeconds: totalTimeSeconds);
            Rl.DrawGrid(28, 3f);

            Vector3 sunTip = sun * 60f;
            Rl.DrawLine3D(sunTip - new Vector3(2f, 0f, 0f), sunTip + new Vector3(2f, 0f, 0f), Color.YELLOW);
            Rl.DrawLine3D(sunTip - new Vector3(0f, 2f, 0f), sunTip + new Vector3(0f, 2f, 0f), Color.YELLOW);
            Rl.EndMode3D();

            GalleryFont.Draw($"day phase {phase:0.00}  sun Y {sun.Y:0.00}  ambient W {_lighting.AmbientRgba.W:0.00}", 12, 28, 20, GalleryColors.RayWhite);
        }

        private void BuildSnapshot(double totalTimeSeconds)
        {
            float t = (float)totalTimeSeconds;
            _snapshot.BeginFrame();
            _snapshot.Add(GalleryItems.Mesh(101, 9000, new Vector3(0f, -0.10f, 0f), new Vector3(34f, 0.20f, 34f), GalleryColors.ShadowReceiverGray));
            _snapshot.Add(GalleryItems.Mesh(101, 1, new Vector3(0f, 2f, 0f), new Vector3(4f), new Vector4(0.94f, 0.92f, 0.88f, 1f)));
            for (int i = 0; i < 10; i++)
            {
                float angle = (i * MathF.Tau / 10f) + (t * 0.15f);
                bool cube = (i % 2) == 0;
                float hue = i / 10f;
                Vector4 tint = new(0.35f + hue, 0.75f - (hue * 0.4f), 0.95f - (hue * 0.6f), 1f);
                _snapshot.Add(GalleryItems.Mesh(
                    cube ? 101 : 102,
                    10 + i,
                    new Vector3(MathF.Cos(angle) * 13f, 1.4f + (cube ? 0.6f : 0f), MathF.Sin(angle) * 13f),
                    new Vector3(2.4f),
                    tint));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _primitives?.Dispose();
            _shadowMap?.Dispose();
            _skybox.Dispose();
            _disposed = true;
        }
    }
}
