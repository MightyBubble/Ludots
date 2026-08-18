using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 大量动画实例合批：4096 个图元士兵走环形行军——CPU 侧每帧重写变换
    /// （行军相位起伏/朝向切线/环带颜色分层），GPU 侧经 RaylibPrimitiveRenderer
    /// Instanced 车道按颜色 bucket 合批绘制。
    /// 注意边界：Model 资产在该车道退化为逐实例立即绘制（不算合批），
    /// 真骨骼逐实例蒙皮见 gpu_skinning 场景；GPU 骨骼实例化车道待 native ≥ 5.5。
    /// </summary>
    public sealed class CrowdAnimScene : IEngineScene
    {
        private const int TargetInstances = 4096;
        private const int RingCount = 14;
        private const int SoldierAssetId = 9001;

        private static readonly Vector3 SoldierScale = new(1.0f, 2.2f, 0.7f);

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private readonly Vector3[] _ringParams = new Vector3[TargetInstances];
        private bool _disposed;

        public string Id => "crowd_anim";
        public string Title => "大量动画实例合批";
        public string Summary => "4k 图元士兵环形行军——Instanced 合批 × CPU 变换动画";

        public void Load()
        {
            _meshes.Register(
                "gallery.soldier",
                MeshAssetDescriptor.Primitive(SoldierAssetId, PrimitiveMeshKind.Cube));
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.58f);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Instanced,
                vfs: null,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register);

            var random = new Random(20260818);
            for (int i = 0; i < TargetInstances; i++)
            {
                int ring = i % RingCount;
                float radius = 8f + (ring * 2.2f);
                float baseAngle = (i / (float)RingCount) * MathF.Tau + (random.NextSingle() * 0.24f);
                float speed = 0.16f - (ring * 0.006f);
                _ringParams[i] = new Vector3(baseAngle, radius, speed);
            }
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            camera.target.Y = 3f;
            camera.position = camera.target + new Vector3(0.55f, 0.62f, 0.55f);
            GalleryCamera.EnforceDistance(ref camera, 60f);
            float t = (float)totalTimeSeconds;

            _lighting.SetDayPhase(0.5f);

            Rl.ClearBackground(new Color(12, 14, 20, 255));
            Rl.BeginMode3D(camera);
            Rl.DrawGrid(60, 6f);
            _primitives.ApplyFrameLighting(_lighting, camera.position);

            _snapshot.BeginFrame();
            for (int i = 0; i < TargetInstances; i++)
            {
                Vector3 p = _ringParams[i];
                float angle = p.X + (t * p.Z);
                int ring = i % RingCount;
                float phase = (angle / MathF.Tau) + (i * 0.113f);
                float bob = MathF.Abs(MathF.Sin(phase * MathF.Tau * 2f)) * 0.34f;
                float sway = MathF.Sin(phase * MathF.Tau * 2f) * 0.09f;
                Vector3 position = new(
                    MathF.Cos(angle) * p.Y,
                    (SoldierScale.Y * 0.5f) + bob,
                    MathF.Sin(angle) * p.Y);
                Quaternion facing = Quaternion.CreateFromYawPitchRoll(-angle - (MathF.PI * 0.5f) + sway, 0f, 0f);
                float depth = ring / (float)RingCount;
                Vector4 tint = new(0.95f - (depth * 0.25f), 0.92f - (depth * 0.15f), 1.0f - (depth * 0.35f), 1f);
                _snapshot.Add(GalleryItems.Mesh(
                    SoldierAssetId,
                    10000 + i,
                    position,
                    SoldierScale,
                    tint,
                    rotation: facing));
            }

            _primitives.Draw(_snapshot, camera, _meshes, timeSeconds: totalTimeSeconds);

            Rl.EndMode3D();

            GalleryFont.Draw(
                $"crowd {TargetInstances} instanced  rings {RingCount}  visuals {_primitives.LastMeshVisualCount}  batches {_primitives.LastInstancedBatches}",
                12,
                28,
                20,
                GalleryColors.RayWhite);
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
