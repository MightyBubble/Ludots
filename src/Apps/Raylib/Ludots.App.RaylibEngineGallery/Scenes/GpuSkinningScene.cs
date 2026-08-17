using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// GPU 骨骼蒙皮：RaylibGpuSkinnedModelCache fail-loud 装载 mannequin GLB（骨骼/动画校验），
    /// RaylibSkinnedPlayback 逐实例解算 clip/帧相位并上传骨骼姿态后绘制。
    /// 当前捆绑的 Windows raylib.dll 缺 UpdateModelAnimationBones 导出，RaylibPrimitiveRenderer 的
    /// GpuSkinnedInstance 合批通道在该二进制上不可用，故本场景经缓存模型逐实例上传姿态绘制。
    /// </summary>
    public sealed unsafe class GpuSkinningScene : IEngineScene
    {
        private const int MeshAssetId = 201;
        private const int InstanceCount = 12;
        private const float RingRadius = 9.5f;

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GallerySkinnedPlayback[] _playbacks = new GallerySkinnedPlayback[InstanceCount];

        private RaylibGpuSkinnedModelCache _modelCache = null!;
        private RaylibGpuSkinnedModelCache.Entry _entry;
        private bool _disposed;

        private sealed class GallerySkinnedPlayback
        {
            private readonly RaylibSkinnedPlayback _playback = new();

            public void Bind(RaylibGpuSkinnedModelCache.Entry entry)
            {
                _playback.BindAnimations(entry.Animations, entry.AnimCount);
            }

            public int ResolveFrame(float phase01)
            {
                var packed = AnimatorPackedState.Create(controllerId: 1);
                packed.SetPrimaryStateIndex(0);
                packed.SetNormalizedTime01(phase01);
                packed.SetFlags(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping);
                _playback.ApplyAnimatorPackedState(in packed);
                return _playback.ResolveFrameIndex();
            }
        }

        public string Id => "gpu_skinning";
        public string Title => "GPU 骨骼蒙皮";
        public string Summary => "RaylibGpuSkinnedModelCache + RaylibSkinnedPlayback 多相位实例";

        public void Load()
        {
            _meshes.Register(
                "gallery.mannequin",
                MeshAssetDescriptor.Model(MeshAssetId, "Models/mannequin_large_walk.glb"));

            _modelCache = new RaylibGpuSkinnedModelCache(GalleryAssetPaths.Instance);
            MeshAssetDescriptor descriptor = MeshAssetDescriptor.Model(MeshAssetId, "Models/mannequin_large_walk.glb");
            _entry = _modelCache.GetOrLoad(MeshAssetId, in descriptor);
            for (int i = 0; i < InstanceCount; i++)
            {
                _playbacks[i] = new GallerySkinnedPlayback();
                _playbacks[i].Bind(_entry);
            }
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 24f);
            camera.target.Y = 2.2f;

            Rl.BeginMode3D(camera);
            Rl.DrawGrid(24, 2f);
            Rl.DrawCube(new Vector3(0f, 0.15f, 0f), 24f, 0.3f, 24f, new Color(46, 50, 62, 255));

            ModelAnimation animation = _entry.Animations[0];
            for (int i = 0; i < InstanceCount; i++)
            {
                float angle = (i * MathF.Tau / InstanceCount) + ((float)totalTimeSeconds * 0.1f);
                float phase = ((float)totalTimeSeconds * 0.55f + (i / (float)InstanceCount)) % 1f;
                int frame = _playbacks[i].ResolveFrame(phase);
                Rl.UpdateModelAnimation(_entry.Model, animation, frame);
                byte lift = (byte)(190 + (55 * MathF.Sin(i)));
                Rl.DrawModelEx(
                    _entry.Model,
                    new Vector3(MathF.Cos(angle) * RingRadius, 0f, MathF.Sin(angle) * RingRadius),
                    Vector3.UnitY,
                    (angle + (MathF.PI * 0.5f)) * (180f / MathF.PI),
                    new Vector3(1.15f),
                    new Color(235, lift, 255, 255));
            }

            Rl.EndMode3D();
            Rl.DrawText($"skinned instances {InstanceCount}  clip frames {animation.frameCount}", 12, 28, 20, GalleryColors.RayWhite);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _modelCache?.Dispose();
            _modelCache = null!;
            _disposed = true;
        }
    }
}
