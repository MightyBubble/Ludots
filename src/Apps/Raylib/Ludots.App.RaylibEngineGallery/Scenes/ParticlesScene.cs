using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// Quarks 粒子：手工构造三组 ParticleVfxAssetData（加色火花 / 逐帧贴图烟雾 / 拉伸火星拖尾），
    /// 经 RaylibPrimitiveRenderer 的 VFX 通道驱动 ParticleSystemRuntime。
    /// </summary>
    public sealed class ParticlesScene : IEngineScene
    {
        private const int SparkAssetId = 301;
        private const int SmokeAssetId = 302;
        private const int EmberAssetId = 303;
        private const int SmokeSheetAssetId = 310;

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private bool _disposed;

        public string Id => "particles";
        public string Title => "Quarks 粒子";
        public string Summary => "ParticleVfxAssetData 火花/烟雾/拉伸火星三组效果";

        public void Load()
        {
            const int frame = 64;
            GalleryTextureFactory.WritePng("smoke_sheet.png", frame * 4, frame, (x, y) =>
            {
                int index = Math.Min(3, x / frame);
                float u = ((x % frame) - (frame * 0.5f)) / (frame * 0.5f);
                float v = (y - (frame * 0.5f)) / (frame * 0.5f);
                float radial = MathF.Sqrt((u * u) + (v * v));
                float density = Math.Clamp(1f - radial, 0f, 1f);
                float alpha = MathF.Pow(density, 1.6f) * (0.30f + (index * 0.18f));
                byte gray = (byte)(205 - (index * 22));
                return new Color(gray, gray, gray, (byte)(alpha * 255f));
            });

            _meshes.Register("gallery.spark", new MeshAssetDescriptor
            {
                Id = SparkAssetId,
                Type = MeshAssetType.Billboard,
                VfxData = new VfxAssetData(BuildSparkEffect(), SparkAssetId),
            });
            _meshes.Register("gallery.smoke", new MeshAssetDescriptor
            {
                Id = SmokeAssetId,
                Type = MeshAssetType.Billboard,
                VfxData = new VfxAssetData(BuildSmokeEffect(), SmokeAssetId),
            });
            _meshes.Register("gallery.ember", new MeshAssetDescriptor
            {
                Id = EmberAssetId,
                Type = MeshAssetType.Billboard,
                VfxData = new VfxAssetData(BuildEmberEffect(), EmberAssetId),
            });
            _meshes.Register(
                "gallery.smoke_sheet",
                MeshAssetDescriptor.Billboard(SmokeSheetAssetId, "generated/smoke_sheet.png"));
            _meshes.Register("gallery.cube", MeshAssetDescriptor.Primitive(101, PrimitiveMeshKind.Cube));

            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.60f);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Immediate,
                GalleryAssetPaths.Instance,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register);
        }

        private static ParticleVfxAssetData BuildSparkEffect()
        {
            return new ParticleVfxAssetData(
                spawnMode: ParticleVfxSpawnMode.Loop,
                emitterShape: ParticleEmitterShapeKind.Cone,
                renderMode: ParticleRenderMode.Primitive,
                blendMode: ParticleBlendMode.Additive,
                primitiveKind: ParticlePrimitiveKind.Cube,
                maxParticles: 220,
                seed: 1301u,
                durationSeconds: 2.5f,
                emissionRatePerSecond: 90f,
                burstCount: 24,
                shapeRadius: 0.12f,
                shapeAngleRadians: 0.55f,
                shapeThickness: 0f,
                startLife: new ParticleValueRange(0.45f, 1.1f),
                startSpeed: new ParticleValueRange(3.2f, 6.4f),
                startSize: new ParticleValueRange(0.05f, 0.11f),
                startColor: new Vector4(1f, 0.78f, 0.32f, 1f),
                sizeOverLife: new ParticleScalarCurve(new[]
                {
                    new ParticleCurveKey(0f, 1f),
                    new ParticleCurveKey(0.25f, 0.85f),
                    new ParticleCurveKey(1f, 0.15f),
                }),
                colorOverLife: new ParticleColorGradient(new[]
                {
                    new ParticleColorKey(0f, new Vector4(1f, 0.86f, 0.42f, 1f)),
                    new ParticleColorKey(0.45f, new Vector4(1f, 0.42f, 0.12f, 0.9f)),
                    new ParticleColorKey(1f, new Vector4(0.55f, 0.08f, 0.02f, 0f)),
                }),
                gravity: new Vector3(0f, -7.5f, 0f),
                drag: 0.7f,
                worldSpace: false,
                textureSheet: null,
                stretchedLengthScale: 0f,
                trailLengthSeconds: 0f);
        }

        private static ParticleVfxAssetData BuildSmokeEffect()
        {
            return new ParticleVfxAssetData(
                spawnMode: ParticleVfxSpawnMode.Loop,
                emitterShape: ParticleEmitterShapeKind.Sphere,
                renderMode: ParticleRenderMode.Billboard,
                blendMode: ParticleBlendMode.Alpha,
                primitiveKind: ParticlePrimitiveKind.Sphere,
                maxParticles: 140,
                seed: 1302u,
                durationSeconds: 5f,
                emissionRatePerSecond: 30f,
                burstCount: 12,
                shapeRadius: 0.35f,
                shapeAngleRadians: 0f,
                shapeThickness: 0.6f,
                startLife: new ParticleValueRange(2.2f, 3.6f),
                startSpeed: new ParticleValueRange(0.5f, 1.1f),
                startSize: new ParticleValueRange(0.55f, 0.85f),
                startColor: new Vector4(0.82f, 0.80f, 0.78f, 0.62f),
                sizeOverLife: new ParticleScalarCurve(new[]
                {
                    new ParticleCurveKey(0f, 0.7f),
                    new ParticleCurveKey(0.4f, 1.6f),
                    new ParticleCurveKey(1f, 2.6f),
                }),
                colorOverLife: new ParticleColorGradient(new[]
                {
                    new ParticleColorKey(0f, new Vector4(1f, 1f, 1f, 0.7f)),
                    new ParticleColorKey(0.6f, new Vector4(0.85f, 0.85f, 0.88f, 0.36f)),
                    new ParticleColorKey(1f, new Vector4(0.7f, 0.72f, 0.78f, 0f)),
                }),
                gravity: new Vector3(0f, 0.3f, 0f),
                drag: 0.25f,
                worldSpace: false,
                textureSheet: new ParticleTextureSheetAsset(
                    textureAssetId: "gallery.smoke_sheet",
                    columns: 4,
                    rows: 1,
                    frameCount: 4,
                    framesPerSecond: 6f,
                    startFrame: new ParticleIntRange(0, 0),
                    playbackMode: ParticleTextureSheetPlaybackMode.Loop),
                stretchedLengthScale: 0f,
                trailLengthSeconds: 0f);
        }

        private static ParticleVfxAssetData BuildEmberEffect()
        {
            return new ParticleVfxAssetData(
                spawnMode: ParticleVfxSpawnMode.Loop,
                emitterShape: ParticleEmitterShapeKind.Point,
                renderMode: ParticleRenderMode.Trail,
                blendMode: ParticleBlendMode.Additive,
                primitiveKind: ParticlePrimitiveKind.Sphere,
                maxParticles: 90,
                seed: 1303u,
                durationSeconds: 3.5f,
                emissionRatePerSecond: 26f,
                burstCount: 4,
                shapeRadius: 0f,
                shapeAngleRadians: 0f,
                shapeThickness: 0f,
                startLife: new ParticleValueRange(0.7f, 1.3f),
                startSpeed: new ParticleValueRange(1.4f, 2.8f),
                startSize: new ParticleValueRange(0.03f, 0.06f),
                startColor: new Vector4(1f, 0.55f, 0.18f, 1f),
                sizeOverLife: new ParticleScalarCurve(new[]
                {
                    new ParticleCurveKey(0f, 1f),
                    new ParticleCurveKey(1f, 0.4f),
                }),
                colorOverLife: new ParticleColorGradient(new[]
                {
                    new ParticleColorKey(0f, new Vector4(1f, 0.85f, 0.35f, 1f)),
                    new ParticleColorKey(1f, new Vector4(0.85f, 0.20f, 0.05f, 0.2f)),
                }),
                gravity: new Vector3(0.25f, 1.7f, 0.15f),
                drag: 0.15f,
                worldSpace: false,
                textureSheet: null,
                stretchedLengthScale: 0f,
                trailLengthSeconds: 0.30f);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 17f);
            camera.target.Y = 2.4f;

            _lighting.SetDayPhase(0.60f);

            Rl.ClearBackground(new Color(9, 10, 16, 255));
            Rl.BeginMode3D(camera);
            Rl.DrawGrid(20, 2f);

            _primitives.ApplyFrameLighting(_lighting, camera.position);
            _snapshot.BeginFrame();
            _snapshot.Add(GalleryItems.Mesh(101, 50, new Vector3(-5.5f, 0.7f, 0f), new Vector3(1.6f, 1.4f, 1.6f), new Vector4(0.32f, 0.30f, 0.34f, 1f)));
            _snapshot.Add(GalleryItems.Mesh(101, 51, new Vector3(0f, 0.7f, 2.5f), new Vector3(1.6f, 1.4f, 1.6f), new Vector4(0.30f, 0.32f, 0.38f, 1f)));
            _snapshot.Add(GalleryItems.Mesh(101, 52, new Vector3(5.5f, 0.7f, 0f), new Vector3(1.6f, 1.4f, 1.6f), new Vector4(0.34f, 0.30f, 0.30f, 1f)));
            _snapshot.Add(GalleryItems.Vfx(SparkAssetId, 1, new Vector3(-5.5f, 1.9f, 0f), new Vector4(1f, 0.9f, 0.7f, 1f)));
            _snapshot.Add(GalleryItems.Vfx(SmokeAssetId, 2, new Vector3(0f, 1.9f, 2.5f), new Vector4(0.9f, 0.92f, 1f, 0.85f)));
            _snapshot.Add(GalleryItems.Vfx(EmberAssetId, 3, new Vector3(5.5f, 1.9f, 0f), new Vector4(1f, 0.75f, 0.45f, 1f)));
            _primitives.Draw(_snapshot, camera, _meshes, timeSeconds: totalTimeSeconds);
            Rl.EndMode3D();

            Rl.DrawText($"vfx drawn last frame {_primitives.LastDrawnVfxCount}", 12, 28, 20, GalleryColors.RayWhite);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _primitives?.Dispose();
            _disposed = true;
        }
    }
}
