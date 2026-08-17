using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 投影贴花：RaylibVisualHeightmapRenderer 作为接收面网格投影器，decal_project shader
    /// 把程序化贴花沿世界 Y 投到起伏地表；三枚贴花随时间在地表巡游。
    /// </summary>
    public sealed class DecalProjectionScene : IEngineScene
    {
        private const int RingMaterialId = 601;
        private const int ArrowMaterialId = 602;
        private const int TargetMaterialId = 603;

        private readonly GalleryIslandHeightmap _heightmap = new(
            chunksPerSide: 12,
            samplesPerChunk: 33,
            worldSizeMeters: 360,
            seed: 88);

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryMaterialAssets _materials = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();

        private RaylibVisualHeightmapRenderer _terrain = new() { VisibleRadiusCm = 60_000f };
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private bool _disposed;

        public string Id => "decal_projection";
        public string Title => "投影贴花";
        public string Summary => "decal_project shader 地表移动投影贴花";

        public void Load()
        {
            GalleryTextureFactory.WritePng("decal_ring.png", 128, 128, (x, y) =>
            {
                float u = (x - 64f) / 56f;
                float v = (y - 64f) / 56f;
                float radial = MathF.Sqrt((u * u) + (v * v));
                float band = MathF.Abs(radial - 0.72f);
                byte alpha = band < 0.09f ? (byte)(235 - (band * 900f)) : (byte)0;
                return new Color(90, 220, 150, alpha);
            });
            GalleryTextureFactory.WritePng("decal_arrow.png", 128, 128, (x, y) =>
            {
                float u = (x - 64f) / 56f;
                float v = (y - 64f) / 56f;
                bool inShaft = MathF.Abs(u) < 0.16f && v > -0.75f && v < 0.35f;
                bool inHead = v >= 0.30f && v < 0.85f && MathF.Abs(u) < (0.62f * (0.85f - v));
                return inShaft || inHead ? new Color(255, 176, 64, 255) : new Color(0, 0, 0, 0);
            });
            GalleryTextureFactory.WritePng("decal_target.png", 128, 128, (x, y) =>
            {
                float u = (x - 64f) / 56f;
                float v = (y - 64f) / 56f;
                float radial = MathF.Sqrt((u * u) + (v * v));
                float band = MathF.Abs(radial - 0.5f);
                bool ring = band < 0.07f;
                bool cross = (MathF.Abs(u) < 0.05f || MathF.Abs(v) < 0.05f) && radial < 0.36f;
                return ring || cross ? new Color(255, 92, 92, 215) : new Color(0, 0, 0, 0);
            });

            _materials.Register("gallery.decal.ring", new MaterialAssetDescriptor(
                RingMaterialId,
                MaterialAssetDomain.Surface,
                new[] { "generated/decal_ring.png" },
                MaterialAssetFlags.Transparent));
            _materials.Register("gallery.decal.arrow", new MaterialAssetDescriptor(
                ArrowMaterialId,
                MaterialAssetDomain.Surface,
                new[] { "generated/decal_arrow.png" },
                MaterialAssetFlags.Cutout));
            _materials.Register("gallery.decal.target", new MaterialAssetDescriptor(
                TargetMaterialId,
                MaterialAssetDomain.Surface,
                new[] { "generated/decal_target.png" },
                MaterialAssetFlags.Transparent));

            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.44f);
            _terrain.ApplyFrameLighting(_lighting);
            _terrain.BindStampHeightSampleSource(_heightmap);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Immediate,
                GalleryAssetPaths.Instance,
                _materials,
                channelRegistrar: GalleryAnimationChannels.Register);
            _primitives.BindReceiverMeshProjector(_terrain);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 150f);
            camera.target.Y = 4f;

            float t = (float)totalTimeSeconds;
            _lighting.SetDayPhase(0.44f);
            _terrain.ApplyFrameLighting(_lighting);

            Rl.ClearBackground(new Color(88, 120, 150, 255));
            Rl.BeginMode3D(camera);
            _terrain.Render(_heightmap, camera);

            _primitives.ApplyFrameLighting(_lighting, camera.position);
            _snapshot.BeginFrame();
            Vector3 ringPos = new(MathF.Cos(t * 0.35f) * 46f, 6f, MathF.Sin(t * 0.35f) * 46f);
            Vector3 arrowPos = new(MathF.Cos(t * 0.5f + 2.2f) * 30f, 6f, MathF.Sin(t * 0.5f + 2.2f) * 30f);
            Vector3 targetPos = new(0f, 6f, 0f);
            _snapshot.Add(GalleryItems.Decal(1, RingMaterialId, ringPos, yawRad: t * 0.8f, stampWidth: 16f, stampDepth: 16f, tint: new Vector4(1f, 1f, 1f, 0.9f)));
            _snapshot.Add(GalleryItems.Decal(2, ArrowMaterialId, arrowPos, yawRad: -t * 0.6f, stampWidth: 10f, stampDepth: 10f, tint: Vector4.One));
            _snapshot.Add(GalleryItems.Decal(3, TargetMaterialId, targetPos, yawRad: 0f, stampWidth: 7f, stampDepth: 7f, tint: Vector4.One));
            _primitives.Draw(_snapshot, camera, _meshes, visualHeightmap: _heightmap, timeSeconds: totalTimeSeconds);
            Rl.EndMode3D();

            GalleryFont.Draw($"decals painted last frame {_primitives.LastDecalVisualCount}", 12, 28, 20, GalleryColors.RayWhite);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _primitives?.Dispose();
            _terrain?.Dispose();
            _terrain = null!;
            _disposed = true;
        }
    }
}
