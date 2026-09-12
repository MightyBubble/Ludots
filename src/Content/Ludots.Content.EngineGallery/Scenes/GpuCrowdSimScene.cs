using System.Numerics;
using Arch;
using Arch.Core;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Raylib.SceneKit;

namespace Ludots.Content.EngineGallery.Scenes
{
    /// <summary>游走参数（驻留点 + 确定性轨迹参数）——模拟层实体组件。</summary>
    public struct CrowdHome
    {
        public Vector3 Home;
        public float Phase;
        public float Omega;
        public float Radius;
        public int ClipSlot;
        public int TintRgb;
    }

    /// <summary>模拟层每帧写出的最终变换（presenter 发射源）。</summary>
    public struct CrowdTransform
    {
        public Vector3 Position;
        public float Yaw;
        public float Angle;
    }

    /// <summary>animator behavior：剪辑槽 + 相位推进。</summary>
    public struct CrowdAnimator
    {
        public float Phase;
        public float Rate;
    }

    /// <summary>
    /// 模拟层驱动的 200K GPU 蒙皮军团（无寻路、无避障、无 GAS——纯 entity + presenter 车道 + animator behavior）：
    /// Arch World 里每个实体带 CrowdHome/CrowdTransform/CrowdAnimator，模拟系统单遍推进游走积分与动画相位；
    /// 发射侧把实体状态写成 SkinnedVisualBatchItem（与游戏宿主 presenter emit 同一 ISkinnedVisualBatchSnapshot 合同，
    /// 画廊侧由 GallerySkinnedBatch 承载），由 RaylibGpuCrowdLane 正式消费——预蒙皮 + GPU 剔除 + indirect draw
    /// + imposter + 投射体阴影 + PBR/IBL。场景零 GL 调用；HUD 分列 sim / emit / pack / pose 耗时。
    /// </summary>
    [EngineSceneComponent("gpu_crowd_sim")]
    public sealed class GpuCrowdSimScene : IEngineSceneComponent, IEngineSceneComponentAssets
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
        private readonly GallerySkinnedBatch _skinnedBatch = new(TargetInstances);
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private RaylibSkyboxRenderer _skybox = null!;
        private RaylibSkyIbl _skyIbl = null!;
        private RaylibGpuCrowdLane _lane = null!;
        private World _world = null!;
        private QueryDescription _simQuery;
        private EngineSceneAsset _highModel = null!;
        private EngineSceneAsset _mediumModel = null!;
        private EngineSceneAsset _lowModel = null!;
        private bool _disposed;
        private double _lastSimMs;
        private double _lastEmitMs;
        private double _lastFrameMs;
        private double _fpsEma;

        public void SetAssets(IReadOnlyDictionary<string, EngineSceneAsset> assets)
        {
            if (!assets.TryGetValue("gpu_crowd.mannequin_high", out _highModel) ||
                !assets.TryGetValue("gpu_crowd.mannequin_medium", out _mediumModel) ||
                !assets.TryGetValue("gpu_crowd.mannequin_low", out _lowModel))
            {
                throw new InvalidDataException("gpu_crowd_sim requires gpu_crowd.mannequin_high/medium/low assets.");
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

            SpawnWorld();

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
                StaticBatch: false,
                GpuWander: false));
            _lane.Initialize();
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
                        throw new InvalidOperationException($"gpu_crowd_sim 需要 clip '{ClipNames[slot]}'（模型动画缺失）。");
                    }
                }

                return slots;
            }
            finally
            {
                Rl.UnloadModelAnimations(anims, animCount);
            }
        }

        /// <summary>生成模拟层实体：向日葵驻留点 + 确定性游走参数 + 随机剪辑槽。</summary>
        private void SpawnWorld()
        {
            _world = World.Create();
            var random = new Random(20260912);
            for (int i = 0; i < TargetInstances; i++)
            {
                float radius = CrowdMaxRadius * MathF.Sqrt((i + 0.5f) / TargetInstances);
                float theta = i * GoldenAngle;
                Vector2 h = CrowdHash(i);
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

                float angle = h.X * MathF.Tau;
                bool isRed = radius < CrowdMaxRadius * 0.58f;
                Vector3 tint = isRed ? new Vector3(0.85f, 0.18f, 0.12f) : new Vector3(0.12f, 0.25f, 0.85f);
                int r = (int)MathF.Round(tint.X * 255);
                int g = (int)MathF.Round(tint.Y * 255);
                int b = (int)MathF.Round(tint.Z * 255);

                _world.Create(
                    new CrowdHome
                    {
                        Home = new Vector3(MathF.Cos(theta) * radius, 0f, MathF.Sin(theta) * radius),
                        Phase = angle,
                        Omega = 0.25f + h.Y * 0.55f,
                        Radius = 1.2f + Fract(h.X * 7.13f) * 2.4f,
                        ClipSlot = clipSlot,
                        TintRgb = r | (g << 8) | (b << 16),
                    },
                    new CrowdTransform
                    {
                        Position = default,
                        Yaw = angle + MathF.PI / 2f,
                        Angle = angle,
                    },
                    new CrowdAnimator
                    {
                        Phase = (i * GoldenRatioFract) % 1f,
                        Rate = 60f / 62f,
                    });
            }

            _simQuery = new QueryDescription().WithAll<CrowdHome, CrowdTransform, CrowdAnimator>();
        }

        private static Vector2 CrowdHash(int id)
        {
            float h1 = Fract(MathF.Sin(id * 12.9898f) * 43758.5453f);
            float h2 = Fract(MathF.Sin(id * 78.233f) * 24634.6345f);
            return new Vector2(h1, h2);
        }

        private static float Fract(float v) => v - MathF.Floor(v);

        private struct CrowdSimJob : IForEach<CrowdHome, CrowdTransform, CrowdAnimator>
        {
            public float Dt;

            public void Update(ref CrowdHome home, ref CrowdTransform transform, ref CrowdAnimator animator)
            {
                transform.Angle += home.Omega * Dt;
                float a = home.Phase + transform.Angle;
                transform.Position = new Vector3(
                    home.Home.X + MathF.Cos(a) * home.Radius,
                    home.Home.Y,
                    home.Home.Z + MathF.Sin(a) * home.Radius);
                transform.Yaw = a + MathF.PI / 2f;
                animator.Phase = Fract(animator.Phase + animator.Rate * Dt);
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
                // ── 模拟层：sim（游走积分 + animator 相位）→ presenter 发射（正式批次合同）──
                long simStart = System.Diagnostics.Stopwatch.GetTimestamp();
                var job = new CrowdSimJob { Dt = deltaSeconds };
                _world.InlineQuery<CrowdSimJob, CrowdHome, CrowdTransform, CrowdAnimator>(in _simQuery, ref job);
                _lastSimMs = (System.Diagnostics.Stopwatch.GetTimestamp() - simStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;

                long emitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                EmitBatch();
                _lastEmitMs = (System.Diagnostics.Stopwatch.GetTimestamp() - emitStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                _lane.PrepareFrame(_skinnedBatch, totalTimeSeconds);

                // ── 阴影拍：地表 + 车道投射体（GPU 剔除 + indirect 深度）──
                _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(0f, 1f, 0f), CrowdSceneRadius);
                _primitives.DrawShadow(_snapshot, _shadowMap, _meshes, camera);
                _lane.DrawShadowCasters(camera.position, t);
                _shadowMap.EndFrame();

                // ── 主拍：天空/地表照旧，人群经车道 GPU 管线 ──
                RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_lighting, sizeMeters: 3000f);
                Rl.ClearBackground(skyConfig.Skybox.ClearColor);
                Rl.BeginMode3D(camera);
                _skybox.Draw(camera, totalTimeSeconds, skyConfig);
                _primitives.ApplyFrameLighting(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.35f);
                _lane.DrawMain(camera, _lighting, _shadowMap, _skyIbl, t);
                _primitives.Draw(_snapshot, camera, _meshes);
                Rl.EndMode3D();

                double frameMs = (System.Diagnostics.Stopwatch.GetTimestamp() - frameStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                _lastFrameMs = frameMs;
                float fps = deltaSeconds > 1e-4f ? 1f / deltaSeconds : 0f;
                _fpsEma = _fpsEma <= 0 ? fps : (_fpsEma * 0.92 + fps * 0.08);
                DrawHud();
            }
            finally
            {
                Rl.EndMode3D();
            }
        }

        /// <summary>presenter 发射：实体状态 → SkinnedVisualBatchItem（与游戏宿主 presenter emit 同合同）。</summary>
        private void EmitBatch()
        {
            _skinnedBatch.BeginFrame();
            foreach (ref var chunk in _world.Query(in _simQuery))
            {
                Span<CrowdHome> homes = chunk.GetSpan<CrowdHome>();
                Span<CrowdTransform> transforms = chunk.GetSpan<CrowdTransform>();
                Span<CrowdAnimator> animators = chunk.GetSpan<CrowdAnimator>();
                for (int i = 0; i < homes.Length; i++)
                {
                    var animatorState = AnimatorPackedState.Create(controllerId: 1);
                    animatorState.SetPrimaryStateIndex(homes[i].ClipSlot);
                    animatorState.SetNormalizedTime01(animators[i].Phase);
                    animatorState.SetFlags(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping);

                    _skinnedBatch.Add(new SkinnedVisualBatchItem
                    {
                        MeshAssetId = FamilyMeshAssetId,
                        StableId = 10000 + i,
                        Position = transforms[i].Position,
                        Rotation = Quaternion.CreateFromYawPitchRoll(transforms[i].Yaw, 0f, 0f),
                        Scale = new Vector3(MannequinScale),
                        Color = DecodeTint(homes[i].TintRgb),
                        RenderPath = VisualRenderPath.GpuSkinnedInstance,
                        AssetKind = AssetKind.SkinnedMesh,
                        Visibility = VisualVisibility.Visible,
                        Animator = animatorState,
                    });
                }
            }
        }

        private static Vector4 DecodeTint(int packed)
        {
            return new Vector4(
                (packed & 0xFF) / 255f,
                ((packed >> 8) & 0xFF) / 255f,
                ((packed >> 16) & 0xFF) / 255f,
                1f);
        }

        private void DrawHud()
        {
            int y = 10;
            Rl.DrawText($"GPU Crowd SIM: {TargetInstances:N0} entities -> presenter lane -> GPU pipeline", 10, y, 20, new Color(120, 255, 160, 255)); y += 26;
            Rl.DrawText($"Culled: {TargetInstances - _lane.LastVisible:N0}   H/M/L/Imp: {_lane.LastHighCount:N0} / {_lane.LastMediumCount:N0} / {_lane.LastLowCount:N0} / {_lane.LastImposterCount:N0}", 10, y, 18, new Color(200, 200, 200, 255)); y += 22;
            Rl.DrawText($"Draw calls: {_lane.LastDrawCalls} indirect + 1 shadow   Shadow casters: {_lane.LastShadowCasterCount:N0}   PBR+IBL", 10, y, 18, new Color(200, 200, 200, 255)); y += 22;
            Rl.DrawText($"SIM: {_lastSimMs:F2} ms   EMIT: {_lastEmitMs:F2} ms   LANE pack: {_lane.LastPackMs:F2} ms   pose: {_lane.LastPoseMs:F2} ms   CPU frame: {_lastFrameMs:F2} ms", 10, y, 18, new Color(150, 255, 150, 255)); y += 22;
            Rl.DrawText($"FPS: {_fpsEma:F0}   无寻路无避障——Arch entity + presenter batch + GPU crowd lane", 10, y, 16, new Color(160, 160, 160, 255));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _lane?.Dispose();
            _shadowMap?.Dispose();
            _primitives?.Dispose();
            _world?.Dispose();
            _disposed = true;
        }
    }
}
