using System.Numerics;
using System.Text;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 真 GPU 蒙皮人群：4096 具 mannequin 环形行军——CPU 侧每帧只重算环位/朝向并打包
    /// AnimatorPackedState（locomotion 相位），骨骼蒙皮走 RaylibPrimitiveRenderer 的
    /// GpuSkinnedInstance 车道：bucket 按 (meshAsset, 环带色, clip, frame) 分桶，
    /// 每 bucket 一次 UpdateModelAnimationBones + skinning_instanced 实例化绘制，
    /// 不存在 CPU 变换假蒙皮回退。
    /// 相位离散化取舍：行走 clip 62 帧（raylib ~60fps 采样），取 16 个相位桶——行军队列里
    /// 比 16 桶更细的相位差肉眼不可辨；环带色量化为 7 档（相邻两环同档，分层渐变保留），
    /// 7 色 ×16 相位 = 112 逻辑桶，mannequin 6 网格 → 672 次 DrawMeshInstanced，
    /// 桶数每 +1 就多 6 次 uniform 上传/draw 与一次骨骼姿态计算，是本车道主要帧耗来源。
    /// </summary>
    [EngineSceneComponent("crowd_anim")]
    public sealed unsafe class CrowdAnimScene : IEngineSceneComponent
    {
        private const int TargetInstances = 4096;
        private const int RingCount = 14;
        private const int ColorBandCount = 7;
        private const int MannequinAssetId = 9001;
        private const int GroundAssetId = 9002;
        private const int DesiredPhaseBuckets = 16;
        private const float MannequinUniformScale = 1.2f;
        private const float ShowcaseDayPhase01 = 0.38f;
        private const float GoldenRatioFract = 0.61803398875f;
        private const string WalkClipNeedle = "Walking";
        private const string WalkClipReject = "retarget";

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private readonly GallerySkinnedBatch _skinnedBatch = new();
        private readonly RaylibSkyboxRenderer _skybox = new();
        private readonly Vector4[] _ringParams = new Vector4[TargetInstances];
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private int _walkClipIndex = -1;
        private int _walkClipFrameCount;
        private int _phaseBucketCount;
        private bool _disposed;

        public void Load()
        {
            _meshes.Register(
                "gallery.crowd.mannequin",
                MeshAssetDescriptor.Model(MannequinAssetId, "Models/mannequin_large_walk.glb"));
            _meshes.Register(
                "gallery.crowd.ground",
                MeshAssetDescriptor.Primitive(GroundAssetId, PrimitiveMeshKind.Cube));
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: ShowcaseDayPhase01);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Instanced,
                vfs: GalleryAssetPaths.Instance,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register);
            _shadowMap = new RaylibDirectionalShadowMap();

            // 只探测 clip 元数据（名字/帧数）供相位分桶；绘制模型由渲染器内置
            // RaylibGpuSkinnedModelCache 独立装载，避免场景侧重复驻留 GPU 网格。
            if (!GalleryAssetPaths.Instance.TryResolveFullPath("Models/mannequin_large_walk.glb", out string modelPath) ||
                !File.Exists(modelPath))
            {
                throw new InvalidOperationException("crowd_anim cannot resolve Models/mannequin_large_walk.glb for clip probing.");
            }

            (_walkClipIndex, _walkClipFrameCount) = ProbeWalkClip(modelPath);
            _phaseBucketCount = Math.Min(DesiredPhaseBuckets, _walkClipFrameCount);

            var random = new Random(20260818);
            for (int i = 0; i < TargetInstances; i++)
            {
                int ring = i % RingCount;
                float radius = 8f + (ring * 2.2f);
                float baseAngle = (i / (float)RingCount) * MathF.Tau + (random.NextSingle() * 0.24f);
                float speed = 0.16f - (ring * 0.006f);
                float baseWalkPhase = (i * GoldenRatioFract) % 1f;
                _ringParams[i] = new Vector4(baseAngle, radius, speed, baseWalkPhase);
            }
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            camera.target.Y = 1.4f;
            camera.position = camera.target + new Vector3(0.62f, 0.46f, 0.62f);
            GalleryCamera.EnforceDistance(ref camera, 32f);
            float t = (float)totalTimeSeconds;
            // raylib 以 ~60fps 采样 glTF clip（62 帧 / 1.042s），按帧数反推原始节奏播放。
            float walkCyclesPerSecond = _walkClipFrameCount / 60f;

            _lighting.SetDayPhase(ShowcaseDayPhase01);

            _snapshot.BeginFrame();
            _skinnedBatch.BeginFrame();
            _snapshot.Add(GalleryItems.Mesh(
                GroundAssetId,
                900000,
                new Vector3(0f, -0.17f, 0f),
                new Vector3(86f, 0.3f, 86f),
                new Vector4(0.74f, 0.75f, 0.66f, 1f)));
            for (int i = 0; i < TargetInstances; i++)
            {
                Vector4 p = _ringParams[i];
                float angle = p.X + (t * p.Z);
                int ring = i % RingCount;
                int phaseBucket = (int)(((p.W + (t * walkCyclesPerSecond)) % 1f) * _phaseBucketCount);
                if (phaseBucket >= _phaseBucketCount)
                {
                    phaseBucket = 0;
                }

                var animator = AnimatorPackedState.Create(controllerId: 1);
                animator.SetPrimaryStateIndex(_walkClipIndex);
                animator.SetNormalizedTime01((phaseBucket + 0.5f) / _phaseBucketCount);
                animator.SetFlags(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping);

                int colorBand = ring * ColorBandCount / RingCount;
                float depth = colorBand / (float)(ColorBandCount - 1);
                _skinnedBatch.Add(new SkinnedVisualBatchItem
                {
                    MeshAssetId = MannequinAssetId,
                    StableId = 10000 + i,
                    Position = new Vector3(MathF.Cos(angle) * p.Y, 0f, MathF.Sin(angle) * p.Y),
                    Rotation = Quaternion.CreateFromYawPitchRoll(-angle, 0f, 0f),
                    Scale = new Vector3(MannequinUniformScale),
                    Color = new Vector4(1.0f - (depth * 0.06f), 0.98f - (depth * 0.04f), 0.96f - (depth * 0.08f), 1f),
                    RenderPath = VisualRenderPath.GpuSkinnedInstance,
                    AssetKind = AssetKind.SkinnedMesh,
                    Visibility = VisualVisibility.Visible,
                    Animator = animator,
                });
            }

            _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(0f, 1f, 0f), 52f);
            _primitives.DrawShadow(_snapshot, _shadowMap, _meshes, camera);
            _primitives.DrawShadow(_skinnedBatch, _shadowMap, _meshes);
            _shadowMap.EndFrame();

            RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_lighting, sizeMeters: 1400f);
            Rl.ClearBackground(skyConfig.Skybox.ClearColor);
            Rl.BeginMode3D(camera);
            _skybox.Draw(camera, totalTimeSeconds, skyConfig);
            _primitives.ApplyFrameLighting(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.12f);

            // snapshot 形参保持非空以进入 persistent-lanes 调用形态（RaylibFrameRenderer 同款），
            // 其中蒙皮批次先于动态 lane 绘制；图元快照仅承载地面盘，人群全部走蒙皮车道。
            _primitives.Draw(
                _snapshot,
                camera,
                snapshot: _snapshot,
                skinnedBatch: _skinnedBatch,
                _meshes,
                timeSeconds: totalTimeSeconds);

            Rl.EndMode3D();

            GalleryFont.Draw(
                $"crowd {_primitives.LastGpuSkinnedInstances} gpu-skinned  rings {RingCount}/{ColorBandCount}bands  phase {_phaseBucketCount}/{_walkClipFrameCount}f  draws {_primitives.LastGpuSkinnedBatches}  gpu {_primitives.LastGpuSkinnedMeshDrawMs:F2}ms  mat {_primitives.LastGpuSkinnedMatrixBuildMs:F2}ms",
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
            _shadowMap?.Dispose();
            _skybox.Dispose();
            _shadowMap = null!;
            _disposed = true;
        }

        private static (int ClipIndex, int FrameCount) ProbeWalkClip(string modelPath)
        {
            ModelAnimation* animations = Rl.LoadModelAnimations(modelPath, out int animCount);
            try
            {
                if (animations == null || animCount <= 0)
                {
                    throw new InvalidOperationException($"crowd_anim model '{modelPath}' has animCount={animCount}; GpuSkinnedInstance forbids silent static fallback.");
                }

                for (int i = 0; i < animCount; i++)
                {
                    string name = ReadAnimationName(animations[i]);
                    if (!name.Contains(WalkClipNeedle, StringComparison.Ordinal) ||
                        name.Contains(WalkClipReject, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int frameCount = animations[i].frameCount;
                    if (frameCount <= 0)
                    {
                        throw new InvalidOperationException($"crowd_anim walk clip '{name}' has frameCount={frameCount}.");
                    }

                    return (i, frameCount);
                }

                throw new InvalidOperationException($"crowd_anim found no walk clip (name contains '{WalkClipNeedle}' without '{WalkClipReject}') in '{modelPath}'.");
            }
            finally
            {
                Rl.UnloadModelAnimations(animations, animCount);
            }
        }

        private static string ReadAnimationName(in ModelAnimation animation)
        {
            fixed (byte* name = animation.name)
            {
                int len = 0;
                while (len < 32 && name[len] != 0)
                {
                    len++;
                }

                return len == 0 ? string.Empty : Encoding.UTF8.GetString(name, len);
            }
        }
    }
}
