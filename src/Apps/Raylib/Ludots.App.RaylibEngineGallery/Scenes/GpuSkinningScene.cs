using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// GPU 骨骼蒙皮：RaylibGpuSkinnedModelCache fail-loud 装载 mannequin GLB（骨骼/动画校验），
    /// RaylibSkinnedPlayback 逐实例解算 clip/帧相位并上传骨骼姿态后绘制——演示逐实例非合批蒙皮路径；
    /// 大规模合批蒙皮（UpdateModelAnimationBones 每 bucket 一次 + DrawMeshInstanced）见 crowd_anim 场景。
    /// </summary>
    public sealed unsafe class GpuSkinningScene : IEngineScene
    {
        private const int MeshAssetId = 201;
        private const int InstanceCount = 12;
        private const float RingRadius = 9.5f;

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GallerySkinnedPlayback[] _playbacks = new GallerySkinnedPlayback[InstanceCount];

        private RaylibGpuSkinnedModelCache _modelCache = null!;
        private RaylibFrameLighting _lighting = null!;
        private RaylibLitModel _lit = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private Mesh _groundMesh;
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
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.35f);
            _lit = new RaylibLitModel();
            _shadowMap = new RaylibDirectionalShadowMap();
            _groundMesh = Rl.GenMeshCube(48f, 0.3f, 48f);
            _entry = _modelCache.GetOrLoad(MeshAssetId, in descriptor);
            _lit.AttachToModel(_entry.Model);
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

            _lighting.SetDayPhase(0.35f);
            _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(0f, 1.2f, 0f), RingRadius + 6f);
            _shadowMap.DrawMeshShadow(_groundMesh, RaylibMatrix.FromScaleTranslation(0f, 0.05f, 0f, 1f, 1f, 1f));
            for (int i = 0; i < InstanceCount; i++)
            {
                float angle = (i * MathF.Tau / InstanceCount) + ((float)totalTimeSeconds * 0.1f);
                Vector3 position = new(MathF.Cos(angle) * RingRadius, 0f, MathF.Sin(angle) * RingRadius);
                _shadowMap.DrawModelShadow(_entry.Model, position, (angle + (MathF.PI * 0.5f)) * (180f / MathF.PI), new Vector3(1.15f));
            }

            _shadowMap.EndFrame();
            _lit.BeginFrame(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.05f);

            Rl.BeginMode3D(camera);
            Rl.DrawGrid(24, 2f);

            ModelAnimation animation = _entry.Animations[0];
            for (int i = 0; i < InstanceCount; i++)
            {
                float angle = (i * MathF.Tau / InstanceCount) + ((float)totalTimeSeconds * 0.1f);
                float phase = ((float)totalTimeSeconds * 0.55f + (i / (float)InstanceCount)) % 1f;
                int frame = _playbacks[i].ResolveFrame(phase);
                Rl.UpdateModelAnimation(_entry.Model, animation, frame);
                Vector3 position = new(MathF.Cos(angle) * RingRadius, 0f, MathF.Sin(angle) * RingRadius);
                byte lift = (byte)(190 + (55 * MathF.Sin(i)));
                _lit.ApplyDrawUniforms(new Vector4(0.92f, lift / 255f, 1f, 1f), roughness: 0.55f, metallic: 0.1f);
                ref Material modelMaterial = ref _entry.Model.materials[0];
                _lit.BindShadowToMaterial(ref modelMaterial, _shadowMap);
                _lit.BindIblToMaterial(ref modelMaterial);
                Rl.DrawModelEx(
                    _entry.Model,
                    position,
                    Vector3.UnitY,
                    (angle + (MathF.PI * 0.5f)) * (180f / MathF.PI),
                    new Vector3(1.15f),
                    new Color(235, lift, 255, 255));
            }

            _lit.DrawMesh(
                _groundMesh,
                RaylibMatrix.FromScaleTranslation(0f, 0.05f, 0f, 1f, 1f, 1f),
                new Vector4(0.32f, 0.34f, 0.42f, 1f),
                roughness: 0.9f,
                metallic: 0f);
            Rl.EndMode3D();
            GalleryFont.Draw($"skinned instances {InstanceCount}  clip frames {animation.frameCount}", 12, 28, 20, GalleryColors.RayWhite);
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
