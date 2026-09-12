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
        public int TintRgb;
    }

    /// <summary>模拟层每帧写出的最终变换（presenter 打包源）。</summary>
    public struct CrowdTransform
    {
        public Vector3 Position;
        public float Yaw;
        public float Angle;
    }

    /// <summary>animator behavior：剪辑 + 相位推进。</summary>
    public struct CrowdAnimator
    {
        public int ClipId;
        public float Phase;
        public float Rate;
    }

    /// <summary>
    /// 模拟层驱动的 200K GPU 蒙皮军团（无寻路、无避障、无 GAS——纯 entity + presenter + animator）：
    /// Arch World 里每个实体带 CrowdHome/CrowdTransform/CrowdAnimator，模拟系统推进游走与动画相位，
    /// presenter 按稳定实体序打包实例表整表上传（UploadInstances），渲染走 gpu_crowd 同一条 GPU 管线
    /// （compute 预蒙皮 + GPU 剔除 + indirect draw + imposter + 投射体阴影 + PBR/IBL）。
    /// GPU 侧确定性游走关闭（Initialize(gpuWander:false)）——世界位置由模拟层唯一决定。
    /// HUD 分列 sim / present / render 三段耗时。
    /// </summary>
    [EngineSceneComponent("gpu_crowd_sim")]
    public sealed unsafe class GpuCrowdSimScene : IEngineSceneComponent, IEngineSceneComponentAssets
    {
        private const int TargetInstances = 200_000;
        private const float CrowdMaxRadius = 450f;
        private const float CrowdSceneRadius = CrowdMaxRadius + 60f;
        private const float MannequinScale = 1.15f;
        private const int PhaseBucketsPerClip = 8;
        private const float ClipPlaybackFps = 60f;
        private static readonly string[] ClipNames = { "Walking_A", "Running_A", "Idle_A", "Idle_B" };
        private static readonly float[] ClipWeights = { 0.45f, 0.25f, 0.20f, 0.10f };
        private const float GoldenRatioFract = 0.61803398875f;
        private const float GoldenAngle = 2.399963229728653f;
        private const float ShowcaseDayPhase01 = 0.38f;
        private const float LodMediumDist = 20f;
        private const float LodLowDist = 60f;
        private const float LodImposterDist = 120f;
        private const float InstanceRadius = 2.5f;
        private const float ShadowCasterRadius = 240f;
        private const int ShadowTextureUnit = 7;
        private const int ShadowMapSize = 4096;
        private const float PbrRoughness = 0.55f;
        private const float PbrMetallic = 0.10f;

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private RaylibSkyboxRenderer _skybox = null!;
        private RaylibSkyIbl _skyIbl = null!;
        private GpuCrowdIndirectRenderer? _crowd;
        private RaylibGpuSkinnedSsboPipeline? _posePipeline;
        private World _world = null!;
        private QueryDescription _simQuery;
        private readonly float[] _instanceData = new float[TargetInstances * 16];
        private readonly int[] _clipIndices = new int[ClipNames.Length];
        private readonly int[] _clipFrames = new int[ClipNames.Length];
        private Shader _skinShader;
        private uint _skinningProgramId;
        private int _locMvp;
        private readonly GpuCrowdPbrUniforms _uniforms = new();
        private EngineSceneAsset _highModel = null!;
        private EngineSceneAsset _mediumModel = null!;
        private EngineSceneAsset _lowModel = null!;
        private bool _disposed;
        private double _lastSimMs;
        private double _lastPresentMs;
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
            _meshes.Register("gpu_crowd.high", MeshAssetDescriptor.Model(9001, _highModel.Source));
            _meshes.Register("gpu_crowd.medium", MeshAssetDescriptor.Model(9002, _mediumModel.Source));
            _meshes.Register("gpu_crowd.low", MeshAssetDescriptor.Model(9003, _lowModel.Source));
            _meshes.Register("gpu_crowd.ground", MeshAssetDescriptor.Primitive(9004, PrimitiveMeshKind.Cube));

            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: ShowcaseDayPhase01);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Instanced,
                vfs: GalleryAssetPaths.Instance,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register,
                gpuSkinnedCapacity: null);
            _shadowMap = new RaylibDirectionalShadowMap(new RaylibShadowConfig(
                MapSize: ShadowMapSize,
                ReceiverBiasWorld: RaylibDirectionalShadowMap.DefaultReceiverBiasWorld,
                SceneRadiusMeters: CrowdSceneRadius,
                ReceiverTexelWorld: 0.35f).Validate());
            _skybox = new RaylibSkyboxRenderer();
            _skyIbl = new RaylibSkyIbl();
            _skyIbl.PrewarmLut();

            (Mesh highMesh, uint highAlbedo) = LoadModelMeshWithAlbedo(_highModel.ResolvedPath!);
            (Mesh mediumMesh, uint mediumAlbedo) = LoadModelMeshWithAlbedo(_mediumModel.ResolvedPath!);
            (Mesh lowMeshRaw, uint lowAlbedo) = LoadModelMeshWithAlbedo(_lowModel.ResolvedPath!);
            Mesh highWelded = GpuCrowdShowcaseScene.EnsureIndexedMesh(highMesh);
            Mesh mediumWelded = GpuCrowdShowcaseScene.EnsureIndexedMesh(mediumMesh);
            Mesh lowWelded = GpuCrowdShowcaseScene.EnsureIndexedMesh(lowMeshRaw);

            SpawnWorld();
            PresentPack(0f);

            _crowd = new GpuCrowdIndirectRenderer(TargetInstances, ClipNames.Length * PhaseBucketsPerClip, 256);
            _crowd.Initialize(
                highWelded, mediumWelded, lowWelded, _instanceData,
                LodMediumDist, LodLowDist, LodImposterDist, InstanceRadius,
                highAlbedo > 1 ? highAlbedo : 0u,
                mediumAlbedo > 1 ? mediumAlbedo : 0u,
                lowWelded.texcoords != null && lowAlbedo > 1 ? lowAlbedo : 0u,
                dynamicInstances: true,
                gpuWander: false);

            _posePipeline = new RaylibGpuSkinnedSsboPipeline(
                maxPoseRows: ClipNames.Length * PhaseBucketsPerClip,
                poseStride: 256,
                maxInstances: 1);
            Model poseModel = Rl.LoadModel(_highModel.ResolvedPath!);
            ModelAnimation* anims = Rl.LoadModelAnimations(_highModel.ResolvedPath!, out int animCount);
            for (int c = 0; c < animCount; c++)
            {
                byte* namePtr = anims[c].name;
                string name = System.Text.Encoding.ASCII.GetString(namePtr, 32).TrimEnd('\0');
                int slot = System.Array.IndexOf(ClipNames, name);
                if (slot >= 0)
                {
                    _clipIndices[slot] = c;
                    _clipFrames[slot] = anims[c].frameCount;
                }
            }

            for (int slot = 0; slot < ClipNames.Length; slot++)
            {
                if (_clipFrames[slot] == 0)
                {
                    throw new InvalidOperationException($"gpu_crowd_sim 需要 clip '{ClipNames[slot]}'（模型动画缺失）。");
                }
            }

            _posePipeline.RegisterModel(9001, poseModel, anims, animCount);

            string shaderDir = AppContext.BaseDirectory;
            _skinShader = RaylibShaderLoader.Load(shaderDir, "gpu_crowd_preskin.vs", "gpu_crowd_pbr.fs", "gpu_crowd_pbr_sim");
            _skinningProgramId = _skinShader.id;
            _uniforms.Resolve(_skinShader, _crowd.ImposterShader);
            _uniforms.ApplyStatic(
                _skinShader, _crowd.ImposterShader,
                _crowd.ImposterSize.X, _crowd.ImposterSize.Y, MannequinScale, shadowTexelWorld: 0.35f);
        }

        /// <summary>生成模拟层实体：向日葵驻留点 + 确定性游走参数 + 随机剪辑（与纯渲染版同一分布）。</summary>
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
                int clipId = 0;
                float acc = 0f;
                for (int c = 0; c < ClipWeights.Length; c++)
                {
                    acc += ClipWeights[c];
                    if (clipRoll < acc)
                    {
                        clipId = c;
                        break;
                    }
                }

                float phase01 = (i * GoldenRatioFract) % 1f;
                bool isRed = radius < CrowdMaxRadius * 0.58f;
                Vector4 tint = isRed
                    ? new Vector4(0.85f, 0.18f, 0.12f, 1f)
                    : new Vector4(0.12f, 0.25f, 0.85f, 1f);
                int r = (int)MathF.Round(Math.Clamp(tint.X, 0, 1) * 255);
                int g = (int)MathF.Round(Math.Clamp(tint.Y, 0, 1) * 255);
                int b = (int)MathF.Round(Math.Clamp(tint.Z, 0, 1) * 255);

                float angle = h.X * MathF.Tau;
                _world.Create(
                    new CrowdHome
                    {
                        Home = new Vector3(MathF.Cos(theta) * radius, 0f, MathF.Sin(theta) * radius),
                        Phase = angle,
                        Omega = 0.25f + h.Y * 0.55f,
                        Radius = 1.2f + Fract(h.X * 7.13f) * 2.4f,
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
                        ClipId = clipId,
                        Phase = phase01,
                        Rate = ClipPlaybackFps / MathF.Max(_clipFrames[clipId], 1),
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

        /// <summary>模拟系统：游走积分 + animator 相位推进（单 query 单遍）。</summary>
        private void Simulate(float dt)
        {
            var job = new CrowdSimJob { Dt = dt };
            _world.InlineQuery<CrowdSimJob, CrowdHome, CrowdTransform, CrowdAnimator>(in _simQuery, ref job);
        }

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

        /// <summary>presenter：按稳定实体序把模拟层状态打包成 GPU 实例表并整表上传。</summary>
        private void PresentPack(float _)
        {
            int cursor = 0;
            float k = MannequinScale;
            foreach (ref var chunk in _world.Query(in _simQuery))
            {
                Span<CrowdHome> homes = chunk.GetSpan<CrowdHome>();
                Span<CrowdTransform> transforms = chunk.GetSpan<CrowdTransform>();
                Span<CrowdAnimator> animators = chunk.GetSpan<CrowdAnimator>();
                for (int i = 0; i < homes.Length; i++)
                {
                    float c = MathF.Cos(transforms[i].Yaw) * k;
                    float s = MathF.Sin(transforms[i].Yaw) * k;
                    int dst = cursor * 16;
                    // GL 列打包（与纯渲染版 FromSystemNumerics 列约定一致）：S·RotY·T 的三列 + 平移
                    _instanceData[dst + 0] = c; _instanceData[dst + 1] = 0f; _instanceData[dst + 2] = -s; _instanceData[dst + 3] = transforms[i].Position.X;
                    _instanceData[dst + 4] = 0f; _instanceData[dst + 5] = k; _instanceData[dst + 6] = 0f; _instanceData[dst + 7] = transforms[i].Position.Y;
                    _instanceData[dst + 8] = s; _instanceData[dst + 9] = 0f; _instanceData[dst + 10] = c; _instanceData[dst + 11] = transforms[i].Position.Z;
                    int poseRow = animators[i].ClipId * PhaseBucketsPerClip + (int)(animators[i].Phase * PhaseBucketsPerClip) % PhaseBucketsPerClip;
                    _instanceData[dst + 12] = poseRow;
                    _instanceData[dst + 13] = homes[i].TintRgb;
                    _instanceData[dst + 14] = 1f;
                    _instanceData[dst + 15] = cursor;
                    cursor++;
                }
            }

            if (cursor != TargetInstances)
            {
                throw new InvalidOperationException($"模拟层实体数 {cursor} != 目标 {TargetInstances}（presenter 顺序合同破坏）。");
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
                9004,
                900000,
                new Vector3(0f, -0.17f, 0f),
                new Vector3(1400f, 0.3f, 1400f),
                new Vector4(0.74f, 0.75f, 0.66f, 1f)));

            try
            {
                if (_posePipeline != null && _crowd != null)
                {
                    // ── 模拟层：sim → presenter 整表上传（HUD 分段计时）──
                    long simStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    Simulate(deltaSeconds);
                    _lastSimMs = (System.Diagnostics.Stopwatch.GetTimestamp() - simStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;

                    long presentStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    PresentPack(t);
                    _crowd.UploadInstances(_instanceData);
                    _lastPresentMs = (System.Diagnostics.Stopwatch.GetTimestamp() - presentStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;

                    // ── GPU 渲染管线（与纯渲染版同一合同）──
                    if (_posePipeline.TryGetModelBinding(9001, out var binding))
                    {
                        int playFrame = (int)(totalTimeSeconds * ClipPlaybackFps);
                        for (int clip = 0; clip < ClipNames.Length; clip++)
                        {
                            int frames = _clipFrames[clip];
                            for (int bucket = 0; bucket < PhaseBucketsPerClip; bucket++)
                            {
                                int phaseFrame = (int)(((bucket + 0.5f) / PhaseBucketsPerClip) * frames);
                                int frame = (playFrame + phaseFrame) % frames;
                                _posePipeline.WriteRowMeta(clip * PhaseBucketsPerClip + bucket, binding, _clipIndices[clip], frame);
                            }
                        }
                    }

                    _posePipeline.DispatchPoseEvaluation(ClipNames.Length * PhaseBucketsPerClip, 0);
                    _crowd.DispatchPreskin(_posePipeline.PoseMatrixBuffer);
                    _crowd.CaptureImposterAtlas();

                    _shadowMap.BeginFrame(_lighting.SunDirectionToward, new Vector3(0f, 1f, 0f), CrowdSceneRadius);
                    _primitives.DrawShadow(_snapshot, _shadowMap, _meshes, camera);
                    _crowd.CullShadowCasters(camera.position, ShadowCasterRadius, TargetInstances, t);
                    _crowd.DrawShadowIndirect(t);
                    _shadowMap.EndFrame();
                }

                RaylibRenderEnvironmentConfig skyConfig = GallerySunSky.CreateConfig(_lighting, sizeMeters: 3000f);
                Rl.ClearBackground(skyConfig.Skybox.ClearColor);
                Rl.BeginMode3D(camera);
                _skybox.Draw(camera, totalTimeSeconds, skyConfig);
                _primitives.ApplyFrameLighting(_lighting, camera.position, _shadowMap, shadowTexelWorld: 0.35f);

                if (_posePipeline != null && _crowd != null)
                {
                    _skyIbl.Ensure(_lighting);
                    _crowd.BindIbl(_skyIbl.EnvCubemap.id, _skyIbl.BrdfLut.id);
                    _crowd.BindShadowTexture(_shadowMap.DepthTexture.id, ShadowTextureUnit);
                    _uniforms.ApplyFrame(_skinShader, _crowd.ImposterShader, _lighting, _shadowMap, camera, t);

                    RaylibMatrix viewProj = RaylibNativeResources.ComputeDrawMvp();
                    _crowd.CullAndDraw(viewProj, camera.position, TargetInstances, _skinningProgramId, _locMvp, t);
                }

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

        private void DrawHud()
        {
            int y = 10;
            Rl.DrawText($"GPU Crowd SIM: {TargetInstances:N0} entities (Arch ECS -> presenter -> GPU pipeline)", 10, y, 20, new Color(120, 255, 160, 255)); y += 26;
            GpuCrowdIndirectRenderer? crowd = _crowd;
            if (crowd != null)
            {
                long visible = crowd.LastVisibleTotal;
                long culled = TargetInstances - visible;
                Rl.DrawText($"Culled: {culled:N0}   H/M/L/Imp: {crowd.LastHighCount:N0} / {crowd.LastMediumCount:N0} / {crowd.LastLowCount:N0} / {crowd.LastImposterCount:N0}", 10, y, 18, new Color(200, 200, 200, 255)); y += 22;
                Rl.DrawText($"Draw calls: {crowd.LastDrawCalls} indirect + 1 shadow   Shadow casters: {crowd.LastShadowCasterCount:N0}   PBR+IBL", 10, y, 18, new Color(200, 200, 200, 255)); y += 22;
            }

            Rl.DrawText($"SIM: {_lastSimMs:F2} ms   PRESENT(pack+upload): {_lastPresentMs:F2} ms   CPU frame: {_lastFrameMs:F2} ms", 10, y, 18, new Color(150, 255, 150, 255)); y += 22;
            Rl.DrawText($"FPS: {_fpsEma:F0}   无寻路无避障——纯 entity(CrowdHome/Transform/Animator) + presenter + GPU 渲染", 10, y, 16, new Color(160, 160, 160, 255));
        }

        private static (Mesh Mesh, uint AlbedoTexture) LoadModelMeshWithAlbedo(string path)
        {
            Model model = Rl.LoadModel(path);
            if (model.meshCount <= 0 || model.meshes == null || model.materialCount <= 0)
            {
                throw new InvalidDataException($"Failed to load model mesh from '{path}'.");
            }

            uint albedo = model.materials[0].maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO].texture.id;
            return (model.meshes[0], albedo);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _crowd?.Dispose();
            _shadowMap?.Dispose();
            _primitives?.Dispose();
            _world?.Dispose();
            _disposed = true;
        }
    }
}
