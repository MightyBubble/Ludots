using System.Collections.Generic;
using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Raylib.SceneKit;

namespace Ludots.Content.EngineGallery.Scenes
{
    /// <summary>
    /// GPU 剔除 + Indirect Draw 纯渲染基线 showcase：200K 红蓝蒙皮军团，零模拟层。
    /// 静态批次在 Load 时按 presenter 车道合同发射一次（GallerySkinnedBatchItem），由
    /// RaylibGpuCrowdLane 正式消费（StaticBatch + GpuWander 模式：实例驻 GPU，确定性游走在
    /// shader 端推进）——预蒙皮 + GPU 剔除 + indirect draw + imposter + 投射体阴影 + PBR/IBL。
    /// 场景零 GL 调用；本场景是模拟层场景（gpu_crowd_sim）的性能对照基线。
    /// </summary>
    [EngineSceneComponent("gpu_crowd")]
    public sealed class GpuCrowdShowcaseScene : IEngineSceneComponent, IEngineSceneComponentAssets
    {
        private const int TargetInstances = 200_000;
        private const float CrowdMaxRadius = 450f;
        private const float CrowdSceneRadius = CrowdMaxRadius + 60f;
        private const float MannequinScale = 1.15f;
        private const float GoldenRatioFract = 0.61803398875f;
        private const float GoldenAngle = 2.399963229728653f;
        private const float ShowcaseDayPhase01 = 0.38f;
        private const int GroundAssetId = 9004;
        private const int FamilyMeshAssetId = 9001;
        private static readonly string[] ClipNames = { "Walking_A", "Running_A", "Idle_A", "Idle_B" };
        private static readonly float[] ClipWeights = { 0.45f, 0.25f, 0.20f, 0.10f };

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private readonly GallerySkinnedBatch _staticBatch = new(TargetInstances);
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private RaylibSkyboxRenderer _skybox = null!;
        private RaylibSkyIbl _skyIbl = null!;
        private RaylibGpuCrowdLane _lane = null!;
        private EngineSceneAsset _highModel = null!;
        private EngineSceneAsset _mediumModel = null!;
        private EngineSceneAsset _lowModel = null!;
        private bool _disposed;
        private double _lastFrameMs;
        private double _fpsEma;

        public void SetAssets(IReadOnlyDictionary<string, EngineSceneAsset> assets)
        {
            if (!assets.TryGetValue("gpu_crowd.mannequin_high", out _highModel) ||
                !assets.TryGetValue("gpu_crowd.mannequin_medium", out _mediumModel) ||
                !assets.TryGetValue("gpu_crowd.mannequin_low", out _lowModel))
            {
                throw new InvalidDataException("gpu_crowd requires mannequin_high/medium/low assets.");
            }
        }

        public void Load()
        {
            _meshes.Register("gpu_crowd.ground", MeshAssetDescriptor.Primitive(GroundAssetId, PrimitiveMeshKind.Cube));
            _meshes.Register("gpu_crowd.high", MeshAssetDescriptor.Model(FamilyMeshAssetId, _highModel.Source));
            _meshes.Register("gpu_crowd.medium", MeshAssetDescriptor.Model(9002, _mediumModel.Source));
            _meshes.Register("gpu_crowd.low", MeshAssetDescriptor.Model(9003, _lowModel.Source));

            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: ShowcaseDayPhase01);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Instanced,
                vfs: GalleryAssetPaths.Instance,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register,
                gpuSkinnedCapacity: null);
            _shadowMap = new RaylibDirectionalShadowMap(new RaylibShadowConfig(
                MapSize: 4096,
                ReceiverBiasWorld: RaylibDirectionalShadowMap.DefaultReceiverBiasWorld,
                SceneRadiusMeters: CrowdSceneRadius,
                ReceiverTexelWorld: 0.35f).Validate());
            _skybox = new RaylibSkyboxRenderer();
            _skyIbl = new RaylibSkyIbl();
            _skyIbl.PrewarmLut();

            _lane = new RaylibGpuCrowdLane(new RaylibGpuCrowdLaneConfig(
                FamilyMeshAssetId: FamilyMeshAssetId,
                HighModelSource: _highModel.ResolvedPath!,
                MediumModelSource: _mediumModel.ResolvedPath!,
                LowModelSource: _lowModel.ResolvedPath!,
                ClipSlotMap: ResolveClipSlots(),
                MaxInstances: TargetInstances,
                LodMediumDist: 20f,
                LodLowDist: 60f,
                LodImposterDist: 120f,
                InstanceRadius: 2.5f,
                ShadowCasterRadius: 240f,
                StaticBatch: true,
                GpuWander: true));
            BuildStaticBatch();
            _lane.Initialize(_staticBatch);
        }

        private unsafe int[] ResolveClipSlots()
        {
            ModelAnimation* anims = Rl.LoadModelAnimations(_highModel.ResolvedPath!, out int animCount);
            try
            {
                int[] slots = new int[ClipNames.Length];
                for (int slot = 0; slot < ClipNames.Length; slot++)
                {
                    slots[slot] = -1;
                    for (int c = 0; c < animCount; c++)
                    {
                        byte* namePtr = anims[c].name;
                        string name = System.Text.Encoding.ASCII.GetString(namePtr, 32).TrimEnd('\0');
                        if (name == ClipNames[slot])
                        {
                            slots[slot] = c;
                            break;
                        }
                    }

                    if (slots[slot] < 0)
                    {
                        throw new InvalidOperationException($"gpu_crowd 需要 clip '{ClipNames[slot]}'（模型动画缺失）。");
                    }
                }

                return slots;
            }
            finally
            {
                Rl.UnloadModelAnimations(anims, animCount);
            }
        }

        /// <summary>静态批次：向日葵驻留点 + 随机剪辑/相位/阵营色，发射一次后驻 GPU（游走在 shader 端推进）。</summary>
        private void BuildStaticBatch()
        {
            var random = new Random(20260911);
            for (int i = 0; i < TargetInstances; i++)
            {
                float radius = CrowdMaxRadius * MathF.Sqrt((i + 0.5f) / TargetInstances);
                float theta = i * GoldenAngle;
                float clipRoll = random.NextSingle();
                int clipSlot = 0;
                float acc = 0f;
                for (int c = 0; c < ClipWeights.Length; c++)
                {
                    acc += ClipWeights[c];
                    if (clipRoll < acc)
                    {
                        clipSlot = c;
                        break;
                    }
                }

                float phase01 = (i * GoldenRatioFract) % 1f;
                bool isRed = radius < CrowdMaxRadius * 0.58f;
                Vector4 tint = isRed
                    ? new Vector4(0.85f, 0.18f, 0.12f, 1f)
                    : new Vector4(0.12f, 0.25f, 0.85f, 1f);

                var animatorState = AnimatorPackedState.Create(controllerId: 1);
                animatorState.SetPrimaryStateIndex(clipSlot);
                animatorState.SetNormalizedTime01(phase01);
                animatorState.SetFlags(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping);

                _staticBatch.Add(new SkinnedVisualBatchItem
                {
                    MeshAssetId = FamilyMeshAssetId,
                    StableId = 10000 + i,
                    Position = new Vector3(MathF.Cos(theta) * radius, 0f, MathF.Sin(theta) * radius),
                    Rotation = Quaternion.CreateFromYawPitchRoll(-theta - MathF.PI / 2f, 0f, 0f),
                    Scale = new Vector3(MannequinScale),
                    Color = tint,
                    RenderPath = VisualRenderPath.GpuSkinnedInstance,
                    AssetKind = AssetKind.SkinnedMesh,
                    Visibility = VisualVisibility.Visible,
                    Animator = animatorState,
                });
            }
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            camera.target.Y = 2f;
            float t = (float)totalTimeSeconds;
            float camAngle = t * 0.045f;
            float dist = 175f + 135f * MathF.Sin(t * 0.085f);
            float horiz = dist * 0.94f;
            float height = MathF.Max(dist * 0.42f, 8f);
            camera.position = camera.target + new Vector3(
                MathF.Cos(camAngle) * horiz,
                height,
                MathF.Sin(camAngle) * horiz);

            var frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _lighting.SetDayPhase(ShowcaseDayPhase01);

            _snapshot.BeginFrame();
            _snapshot.Add(GalleryItems.Mesh(
                GroundAssetId,
                900000,
                new Vector3(0f, -0.17f, 0f),
                new Vector3(1400f, 0.3f, 1400f),
                new Vector4(0.74f, 0.75f, 0.66f, 1f)));

            try
            {
                _lane.PrepareFrame(_staticBatch, totalTimeSeconds);

                _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(0f, 1f, 0f), CrowdSceneRadius);
                _primitives.DrawShadow(_snapshot, _shadowMap, _meshes, camera);
                _lane.DrawShadowCasters(camera.position, t);
                _shadowMap.EndFrame();

                RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_lighting, sizeMeters: 3000f);
                Rl.ClearBackground(skyConfig.Skybox.ClearColor);
                Rl.BeginMode3D(camera);
                _skybox.Draw(camera, totalTimeSeconds, skyConfig);
                _primitives.ApplyFrameLighting(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.35f);
                _lane.DrawMain(camera, _lighting, _shadowMap, _skyIbl, t);
                _primitives.Draw(_snapshot, camera, _meshes);
                Rl.EndMode3D();

                _lastFrameMs = (System.Diagnostics.Stopwatch.GetTimestamp() - frameStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                float fps = deltaSeconds > 1e-4f ? 1f / deltaSeconds : 0f;
                _fpsEma = _fpsEma <= 0 ? fps : (_fpsEma * 0.92 + fps * 0.08);
                DrawHud();
            }
            finally
            {
                Rl.EndMode3D();
            }
        }

        private void DrawHud()
        {
            int y = 10;
            Rl.DrawText($"GPU Crowd: {TargetInstances:N0} GPU-skinned instances (pure render baseline)", 10, y, 20, new Color(255, 255, 100, 255)); y += 26;
            Rl.DrawText($"Culled: {TargetInstances - _lane.LastVisible:N0}   H/M/L/Imp: {_lane.LastHighCount:N0} / {_lane.LastMediumCount:N0} / {_lane.LastLowCount:N0} / {_lane.LastImposterCount:N0}", 10, y, 18, new Color(200, 200, 200, 255)); y += 22;
            Rl.DrawText($"Draw calls: {_lane.LastDrawCalls} indirect + 1 shadow   Shadow casters: {_lane.LastShadowCasterCount:N0}   PBR+IBL", 10, y, 18, new Color(200, 200, 200, 255)); y += 22;
            Rl.DrawText($"pose+preskin+atlas: {_lane.LastPoseMs:F2} ms   CPU frame: {_lastFrameMs:F2} ms", 10, y, 18, new Color(150, 255, 150, 255)); y += 22;
            Rl.DrawText($"FPS: {_fpsEma:F0}   零模拟层——presenter 静态批次 + GPU crowd lane（gpu_crowd_sim 的对照基线）", 10, y, 16, new Color(160, 160, 160, 255));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _lane?.Dispose();
            _shadowMap?.Dispose();
            _primitives?.Dispose();
            _disposed = true;
        }
    }
}
