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
    /// GPU 剔除 + Indirect Draw 纯渲染 showcase：200K 红蓝蒙皮军团。
    /// CPU 每帧只做 5 件事（清零 12B 计数 + 写相机 uniform + 1 次 cull dispatch + 1 次 pose dispatch + 3 次 indirect draw）。
    /// 实例数据加载时一次上传驻 GPU；动画相位烘焙进实例 SSBO 的 poseRow 字段；
    /// GPU compute 剔除（视锥 + 距离 LOD）→ 原子紧凑化 → indirect 命令缓冲 → 3 次 glMultiDrawElementsIndirect。
    /// 阴影：全量最粗 LOD 经 SSBO 深度着色器单次 instanced draw 写入 4096² 打包深度图；
    /// 人群与地表共用同一 PCF 接收合同（shadow_sampling.glsl.inc）。
    /// </summary>
    [EngineSceneComponent("gpu_crowd")]
    public sealed unsafe class GpuCrowdShowcaseScene : IEngineSceneComponent, IEngineSceneComponentAssets
    {
        private const int TargetInstances = 200_000;
        private const float CrowdMaxRadius = 450f;
        private const float CrowdSceneRadius = CrowdMaxRadius + 60f;
        private const float MannequinScale = 1.15f;
        private const int PhaseBuckets = 16;
        private const float WalkClipFps = 60f;
        private const float GoldenRatioFract = 0.61803398875f;
        private const float GoldenAngle = 2.399963229728653f;
        private const float ShowcaseDayPhase01 = 0.38f;
        private const float LodMediumDist = 20f;
        private const float LodLowDist = 60f;
        private const float LodImposterDist = 120f;
        private const float InstanceRadius = 2.5f;
        private const float PbrRoughness = 0.55f;
        private const float PbrMetallic = 0.10f;
        private const float ShadowCasterRadius = 240f;
        private const int ShadowTextureUnit = 7;
        private const int ShadowMapSize = 4096;

        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();
        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private RaylibDirectionalShadowMap _shadowMap = null!;
        private RaylibSkyboxRenderer _skybox = null!;
        private GpuCrowdIndirectRenderer? _crowd;
        private RaylibGpuSkinnedSsboPipeline? _posePipeline;
        private Shader _skinShader;
        private uint _skinningProgramId;
        private int _locMvp;
        private RaylibSkyIbl _skyIbl = null!;
        private int _locLightDir = -1;
        private int _locLightColor = -1;
        private int _locLightIntensity = -1;
        private int _locAmbient = -1;
        private int _locViewPos = -1;
        private int _locFogColor = -1;
        private int _locFogParams = -1;
        private int _locSkyZenith = -1;
        private int _locSkyGround = -1;
        private int _locShadowMap = -1;
        private int _locImpCamPos = -1;
        private int _locImpCamRight = -1;
        private int _locImpCamUp = -1;
        private int _locImpViewPos = -1;
        private int _locImpLightDir = -1;
        private int _locImpLightColor = -1;
        private int _locImpLightIntensity = -1;
        private int _locImpAmbient = -1;
        private int _locImpFogColor = -1;
        private int _locImpFogParams = -1;
        private int _locImpSkyZenith = -1;
        private int _locImpSkyGround = -1;
        private int _locImpShadowMap = -1;
        private int _locImpLightSpaceMatrix = -1;
        private int _locImpShadowEnabled = -1;
        private int _locImpShadowTexelWorld = -1;
        private int _locImpShadowBias = -1;
        private int _locImpShadowMapTexel = -1;
        private int _locImpRoughness = -1;
        private int _locImpMetallic = -1;
        private int _locImpImposterSize = -1;
        private int _locLightSpaceMatrix = -1;
        private int _locShadowEnabled = -1;
        private int _locShadowTexelWorld = -1;
        private int _locShadowBias = -1;
        private int _locShadowMapTexel = -1;
        private EngineSceneAsset _highModel = null!;
        private EngineSceneAsset _mediumModel = null!;
        private EngineSceneAsset _lowModel = null!;
        private bool _disposed;
        private double _lastFrameMs;
        private double _maxFrameMs;
        private double _fpsEma;
        private int _frameCount;

        private int _walkClipIndex = -1;
        private int _walkClipFrames;

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

            // 同步加载三级 LOD 模型；非索引网格（lod2 无 indices accessor）焊接索引化——
            // 索引化让后变换顶点缓存生效，顶点着色器调用数随复用率下降（730 面：2190 顶点 → ~400）。
            (Mesh highMesh, uint highAlbedo) = LoadModelMeshWithAlbedo(_highModel.ResolvedPath!);
            (Mesh mediumMesh, uint mediumAlbedo) = LoadModelMeshWithAlbedo(_mediumModel.ResolvedPath!);
            (Mesh lowMeshRaw, uint lowAlbedo) = LoadModelMeshWithAlbedo(_lowModel.ResolvedPath!);
            Mesh highWelded = EnsureIndexedMesh(highMesh);
            Mesh mediumWelded = EnsureIndexedMesh(mediumMesh);
            // 焊接丢 UV → 焊接低模必然无 albedo（uHasAlbedoMap=0 走平色）
            Mesh lowWelded = EnsureIndexedMesh(lowMeshRaw);

            // 构建实例数据（向日葵布局：等密度填充圆盘；加载时一次，此后驻 GPU 不再更新）
            float[] instanceData = new float[TargetInstances * 16];
            for (int i = 0; i < TargetInstances; i++)
            {
                float radius = CrowdMaxRadius * MathF.Sqrt((i + 0.5f) / TargetInstances);
                float theta = i * GoldenAngle;
                float px = MathF.Cos(theta) * radius;
                float pz = MathF.Sin(theta) * radius;

                // 朝向（面朝切线方向）
                float yaw = -theta - MathF.PI / 2f;
                Quaternion rot = Quaternion.CreateFromYawPitchRoll(yaw, 0, 0);
                Matrix4x4 m = Matrix4x4.CreateScale(MannequinScale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(px, 0, pz);

                // 红蓝军团：内圈红军、外圈蓝军
                bool isRed = radius < CrowdMaxRadius * 0.58f;
                Vector4 tint = isRed
                    ? new Vector4(0.85f, 0.18f, 0.12f, 1f)
                    : new Vector4(0.12f, 0.25f, 0.85f, 1f);

                // poseRow = 相位桶（动画相位烘焙进 SSBO，黄金比例分散与空间解相关）
                float phase = (i * GoldenRatioFract) % 1f;
                int poseRow = (int)(phase * PhaseBuckets) % PhaseBuckets;

                // 打包 4×vec4（FromSystemNumerics 列约定：GL 列 c = 字段 (m_c, m_{4+c}, m_{8+c}, m_{12+c})）
                int dst = i * 16;
                instanceData[dst + 0] = m.M11; instanceData[dst + 1] = m.M21; instanceData[dst + 2] = m.M31; instanceData[dst + 3] = m.M41;
                instanceData[dst + 4] = m.M12; instanceData[dst + 5] = m.M22; instanceData[dst + 6] = m.M32; instanceData[dst + 7] = m.M42;
                instanceData[dst + 8] = m.M13; instanceData[dst + 9] = m.M23; instanceData[dst + 10] = m.M33; instanceData[dst + 11] = m.M43;
                // poseRow + RGB24 tint + alpha + 0（与 PackInstanceTint 合同一致）
                int r = (int)MathF.Round(Math.Clamp(tint.X, 0, 1) * 255);
                int g = (int)MathF.Round(Math.Clamp(tint.Y, 0, 1) * 255);
                int b = (int)MathF.Round(Math.Clamp(tint.Z, 0, 1) * 255);
                instanceData[dst + 12] = poseRow;
                instanceData[dst + 13] = r | (g << 8) | (b << 16);
                instanceData[dst + 14] = 1f;
                instanceData[dst + 15] = 0f;
            }

            _crowd = new GpuCrowdIndirectRenderer(TargetInstances, PhaseBuckets, 256);
            uint lowAlbedoUsed = lowWelded.texcoords != null ? lowAlbedo : 0u;
            _crowd.Initialize(
                highWelded, mediumWelded, lowWelded, instanceData,
                LodMediumDist, LodLowDist, LodImposterDist, InstanceRadius,
                highAlbedo > 1 ? highAlbedo : 0u,
                mediumAlbedo > 1 ? mediumAlbedo : 0u,
                lowWelded.texcoords != null && lowAlbedo > 1 ? lowAlbedo : 0u);
            _skyIbl = new RaylibSkyIbl();
            _skyIbl.PrewarmLut();

            // 姿势 compute 管线（复用现有 SSBO 管线，只做关键帧/逆绑定姿势驻留 + 行描述 + dispatch）
            _posePipeline = new RaylibGpuSkinnedSsboPipeline(
                maxPoseRows: PhaseBuckets,
                poseStride: 256,
                maxInstances: 1);
            // 加载 high LOD 模型获取动画数据（raylib Model 驻留 CPU 侧）
            Model poseModel = Rl.LoadModel(_highModel.ResolvedPath!);
            ModelAnimation* anims = Rl.LoadModelAnimations(_highModel.ResolvedPath!, out int animCount);
            // 找 Walking clip
            int walkClip = -1;
            for (int c = 0; c < animCount; c++)
            {
                byte* namePtr = anims[c].name;
                string name = System.Text.Encoding.ASCII.GetString(namePtr, 32).TrimEnd('\0');
                if (name.Contains("Walking") && !name.Contains("retarget"))
                {
                    walkClip = c;
                    break;
                }
            }

            if (walkClip < 0) walkClip = 0;
            _posePipeline.RegisterModel(9001, poseModel, anims, animCount);
            _walkClipIndex = walkClip;
            _walkClipFrames = anims[walkClip].frameCount;

            // 人群主 pass 着色器：预蒙皮顶点（compute 烘焙）+ 方向光 + 阴影 PCF 接收
            string shaderDir = AppContext.BaseDirectory;
            _skinShader = RaylibShaderLoader.Load(shaderDir, "gpu_crowd_preskin.vs", "gpu_crowd_pbr.fs", "gpu_crowd_pbr");
            _skinningProgramId = _skinShader.id;
            _locMvp = Rl.GetShaderLocation(_skinShader, "mvp");
            _locLightDir = Rl.GetShaderLocation(_skinShader, "uLightDir");
            _locLightColor = Rl.GetShaderLocation(_skinShader, "uLightColor");
            _locLightIntensity = Rl.GetShaderLocation(_skinShader, "uLightIntensity");
            _locAmbient = Rl.GetShaderLocation(_skinShader, "uAmbient");
            _locViewPos = Rl.GetShaderLocation(_skinShader, "uViewPos");
            _locFogColor = Rl.GetShaderLocation(_skinShader, "uFogColor");
            _locFogParams = Rl.GetShaderLocation(_skinShader, "uFogParams");
            _locSkyZenith = Rl.GetShaderLocation(_skinShader, "uSkyZenith");
            _locSkyGround = Rl.GetShaderLocation(_skinShader, "uSkyGround");
            _locShadowMap = Rl.GetShaderLocation(_skinShader, "uShadowMap");
            _locLightSpaceMatrix = Rl.GetShaderLocation(_skinShader, "uLightSpaceMatrix");
            _locShadowEnabled = Rl.GetShaderLocation(_skinShader, "uShadowEnabled");
            _locShadowTexelWorld = Rl.GetShaderLocation(_skinShader, "uShadowTexelWorld");
            _locShadowBias = Rl.GetShaderLocation(_skinShader, "uShadowBias");
            _locShadowMapTexel = Rl.GetShaderLocation(_skinShader, "uShadowMapTexel");
            if (_locMvp < 0 || _locLightDir < 0 ||
                _locLightColor < 0 || _locAmbient < 0 || _locShadowMap < 0 || _locLightSpaceMatrix < 0 ||
                _locShadowEnabled < 0 || _locShadowTexelWorld < 0 || _locShadowBias < 0 || _locShadowMapTexel < 0)
            {
                throw new InvalidOperationException("gpu_crowd_pbr 缺少必需 uniform（阴影接收合同不完整）。");
            }

            // imposter billboard 着色器的 PBR/相机基 uniform（渲染器只管 mvp/图集）
            Shader imp = _crowd.ImposterShader;
            _locImpCamPos = Rl.GetShaderLocation(imp, "uCamPos");
            _locImpCamRight = Rl.GetShaderLocation(imp, "uCamRight");
            _locImpCamUp = Rl.GetShaderLocation(imp, "uCamUp");
            _locImpImposterSize = Rl.GetShaderLocation(imp, "uImposterSize");
            _locImpViewPos = Rl.GetShaderLocation(imp, "uViewPos");
            _locImpLightDir = Rl.GetShaderLocation(imp, "uLightDir");
            _locImpLightColor = Rl.GetShaderLocation(imp, "uLightColor");
            _locImpLightIntensity = Rl.GetShaderLocation(imp, "uLightIntensity");
            _locImpAmbient = Rl.GetShaderLocation(imp, "uAmbient");
            _locImpFogColor = Rl.GetShaderLocation(imp, "uFogColor");
            _locImpFogParams = Rl.GetShaderLocation(imp, "uFogParams");
            _locImpSkyZenith = Rl.GetShaderLocation(imp, "uSkyZenith");
            _locImpSkyGround = Rl.GetShaderLocation(imp, "uSkyGround");
            _locImpShadowMap = Rl.GetShaderLocation(imp, "uShadowMap");
            _locImpLightSpaceMatrix = Rl.GetShaderLocation(imp, "uLightSpaceMatrix");
            _locImpShadowEnabled = Rl.GetShaderLocation(imp, "uShadowEnabled");
            _locImpShadowTexelWorld = Rl.GetShaderLocation(imp, "uShadowTexelWorld");
            _locImpShadowBias = Rl.GetShaderLocation(imp, "uShadowBias");
            _locImpShadowMapTexel = Rl.GetShaderLocation(imp, "uShadowMapTexel");
            _locImpRoughness = Rl.GetShaderLocation(imp, "uRoughness");
            _locImpMetallic = Rl.GetShaderLocation(imp, "uMetallic");
            if (_locImpCamPos < 0 || _locImpCamRight < 0 || _locImpCamUp < 0 || _locImpImposterSize < 0 ||
                _locImpViewPos < 0 || _locImpLightDir < 0 || _locImpLightColor < 0 || _locImpLightIntensity < 0 ||
                _locImpAmbient < 0 || _locImpFogColor < 0 || _locImpFogParams < 0 ||
                _locImpSkyZenith < 0 || _locImpSkyGround < 0 || _locImpShadowMap < 0 || _locImpLightSpaceMatrix < 0 ||
                _locImpShadowEnabled < 0 || _locImpShadowTexelWorld < 0 || _locImpShadowBias < 0 ||
                _locImpRoughness < 0 || _locImpMetallic < 0)
            {
                throw new InvalidOperationException("gpu_imposter 缺少必需 uniform（PBR/阴影/相机基合同不完整）。");
            }

            // 一次性设置不变的 uniform（PBR 标量 + 采样单元约定：albedo=1 / atlas=4 / env=5 / lut=6 / shadow=7）
            float roughness = PbrRoughness, metallic = PbrMetallic, envSpecular = 1f;
            Rl.SetShaderValue(_skinShader, Rl.GetShaderLocation(_skinShader, "uRoughness"), &roughness, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_skinShader, Rl.GetShaderLocation(_skinShader, "uMetallic"), &metallic, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_skinShader, Rl.GetShaderLocation(_skinShader, "uEnvSpecular"), &envSpecular, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            int albedoUnit = (int)GpuCrowdIndirectRenderer.AlbedoTextureUnit;
            Rl.SetShaderValue(_skinShader, Rl.GetShaderLocation(_skinShader, "uAlbedoMap"), &albedoUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);
            int envUnit = (int)GpuCrowdIndirectRenderer.EnvCubemapUnit;
            Rl.SetShaderValue(_skinShader, Rl.GetShaderLocation(_skinShader, "uPrefilteredEnv"), &envUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);
            int lutUnit = (int)GpuCrowdIndirectRenderer.BrdfLutUnit;
            Rl.SetShaderValue(_skinShader, Rl.GetShaderLocation(_skinShader, "uBrdfLut"), &lutUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);
            int shadowUnit = ShadowTextureUnit;
            Rl.SetShaderValue(_skinShader, _locShadowMap, &shadowUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);

            Rl.SetShaderValue(imp, _locImpRoughness, &roughness, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(imp, _locImpMetallic, &metallic, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(imp, _locImpShadowMap, &shadowUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);
            Vector2 imposterSize = _crowd.ImposterSize * MannequinScale;
            Rl.SetShaderValue(imp, _locImpImposterSize, &imposterSize, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC2);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            camera.target.Y = 2f;
            float t = (float)totalTimeSeconds;
            // 缓慢环绕 + 远近呼吸（近看高模细节，远看军团全景 + 剔除收益）
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
                // GPU 姿势 compute → 预蒙皮烘焙（行帧号随时间推进 = 行走动画；桶相位错开 = 满场步调错落）
                if (_posePipeline != null && _crowd != null)
                {
                    if (_posePipeline.TryGetModelBinding(9001, out var binding))
                    {
                        int playFrame = (int)(totalTimeSeconds * WalkClipFps);
                        for (int bucket = 0; bucket < PhaseBuckets; bucket++)
                        {
                            int phaseFrame = (int)(((bucket + 0.5f) / PhaseBuckets) * _walkClipFrames);
                            int frame = (playFrame + phaseFrame) % _walkClipFrames;
                            _posePipeline.WriteRowMeta(bucket, binding, _walkClipIndex, frame);
                        }
                    }

                    _posePipeline.DispatchPoseEvaluation(PhaseBuckets, 0);
                    _crowd.DispatchPreskin(_posePipeline.PoseMatrixBuffer);
                    // imposter 图集逐帧重烘（跟随姿势行推进 = billboard 动画；16 相位 × 8 视角）
                    _crowd.CaptureImposterAtlas();

                    // 阴影 capture：地表 + 投射体半径内的人群（GPU 剔除 → 紧凑化 → indirect 深度绘制）
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

                // 人群主 pass：PBR（直射 GGX + split-sum IBL + 雾 + 阴影 PCF）uniform → cull → 4 × indirect draw
                if (_posePipeline != null && _crowd != null)
                {
                    _skyIbl.Ensure(_lighting);
                    _crowd.BindIbl(_skyIbl.EnvCubemap.id, _skyIbl.BrdfLut.id);
                    _crowd.BindShadowTexture(_shadowMap.DepthTexture.id, ShadowTextureUnit);

                    Vector3 camPos = camera.position;
                    Vector3 lightDir = _lighting.SunDirectionToward;
                    Vector3 lightColor = _lighting.LightColor;
                    float lightIntensity = _lighting.LightIntensity;
                    Vector4 ambient = _lighting.AmbientRgba;
                    Vector3 fogColor = _lighting.FogColor;
                    Vector4 fogParams = _lighting.FogParams;
                    Vector3 skyZenith = _lighting.SkyZenithColor;
                    Vector3 skyGround = _lighting.SkyGroundColor;
                    float shadowEnabled = 1f;
                    float shadowTexelWorld = 0.35f;
                    float shadowBias = _shadowMap.ReceiverBiasWorld / _shadowMap.DepthRange;
                    float shadowMapTexel = 1f / _shadowMap.MapSize;

                    Shader imp = _crowd.ImposterShader;
                    Rl.SetShaderValue(_skinShader, _locLightDir, &lightDir, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(_skinShader, _locLightColor, &lightColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(_skinShader, _locLightIntensity, &lightIntensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.SetShaderValue(_skinShader, _locAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
                    Rl.SetShaderValue(_skinShader, _locViewPos, &camPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(_skinShader, _locFogColor, &fogColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(_skinShader, _locFogParams, &fogParams, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
                    Rl.SetShaderValue(_skinShader, _locSkyZenith, &skyZenith, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(_skinShader, _locSkyGround, &skyGround, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(_skinShader, _locShadowEnabled, &shadowEnabled, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.SetShaderValue(_skinShader, _locShadowTexelWorld, &shadowTexelWorld, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.SetShaderValue(_skinShader, _locShadowBias, &shadowBias, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.SetShaderValue(_skinShader, _locShadowMapTexel, &shadowMapTexel, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.SetShaderValueMatrix(_skinShader, _locLightSpaceMatrix, _shadowMap.LightViewProjection);

                    Rl.SetShaderValue(imp, _locImpLightDir, &lightDir, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(imp, _locImpLightColor, &lightColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(imp, _locImpLightIntensity, &lightIntensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.SetShaderValue(imp, _locImpAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
                    Rl.SetShaderValue(imp, _locImpViewPos, &camPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(imp, _locImpFogColor, &fogColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(imp, _locImpFogParams, &fogParams, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
                    Rl.SetShaderValue(imp, _locImpSkyZenith, &skyZenith, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(imp, _locImpSkyGround, &skyGround, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(imp, _locImpShadowEnabled, &shadowEnabled, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.SetShaderValue(imp, _locImpShadowTexelWorld, &shadowTexelWorld, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.SetShaderValue(imp, _locImpShadowBias, &shadowBias, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    if (_locImpShadowMapTexel >= 0)
                    {
                        Rl.SetShaderValue(imp, _locImpShadowMapTexel, &shadowMapTexel, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    }
                    Rl.SetShaderValueMatrix(imp, _locImpLightSpaceMatrix, _shadowMap.LightViewProjection);

                    // billboard 相机基（quad 朝向相机；视角桶按 相机→实例 方位角选）
                    Vector3 camForward = Vector3.Normalize(camera.target - camera.position);
                    Vector3 camRight = Vector3.Normalize(Vector3.Cross(camForward, Vector3.UnitY));
                    Vector3 camUp = Vector3.Cross(camRight, camForward);
                    Rl.SetShaderValue(imp, _locImpCamPos, &camPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(imp, _locImpCamRight, &camRight, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
                    Rl.SetShaderValue(imp, _locImpCamUp, &camUp, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);

                    Rl.SetShaderValue(_skinShader, Rl.GetShaderLocation(_skinShader, "uTime"), &t, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    Rl.SetShaderValue(imp, Rl.GetShaderLocation(imp, "uTime"), &t, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
                    RaylibMatrix viewProj = RaylibNativeResources.ComputeDrawMvp();
                    _crowd.CullAndDraw(viewProj, camera.position, TargetInstances, _skinningProgramId, _locMvp, t);
                }

                // 地面盘（接收人群阴影）
                _primitives.Draw(_snapshot, camera, _meshes);
                Rl.EndMode3D();
                // HUD
                double frameMs = (System.Diagnostics.Stopwatch.GetTimestamp() - frameStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                _lastFrameMs = frameMs;
                _maxFrameMs = Math.Max(_maxFrameMs, frameMs);
                _frameCount++;
                float fps = deltaSeconds > 1e-4f ? 1f / deltaSeconds : 0f;
                _fpsEma = _fpsEma <= 0 ? fps : (_fpsEma * 0.92 + fps * 0.08);
                DrawHud(camera);
            }
            finally
            {
                Rl.EndMode3D();
            }
        }

        private void DrawHud(Camera3D camera)
        {
            int y = 10;
            Rl.DrawText($"GPU Crowd: {TargetInstances:N0} GPU-skinned instances (200K army)", 10, y, 20, new Color(255, 255, 100, 255)); y += 26;
            GpuCrowdIndirectRenderer? crowd = _crowd;
            if (crowd != null)
            {
                long visible = crowd.LastVisibleTotal;
                long culled = TargetInstances - visible;
                Rl.DrawText($"Culled: {culled:N0}   H/M/L/Imp: {crowd.LastHighCount:N0} / {crowd.LastMediumCount:N0} / {crowd.LastLowCount:N0} / {crowd.LastImposterCount:N0}", 10, y, 18, new Color(200, 200, 200, 255)); y += 22;
                Rl.DrawText($"Draw calls: {crowd.LastDrawCalls} indirect (crowd) + 1 indirect shadow + ground/sky   PBR+IBL", 10, y, 18, new Color(200, 200, 200, 255)); y += 22;
                Rl.DrawText($"Shadow casters (GPU culled): {crowd.LastShadowCasterCount:N0} / {TargetInstances:N0}   CPU frame: {_lastFrameMs:F2} ms", 10, y, 18, new Color(150, 255, 150, 255)); y += 22;
            }

            Rl.DrawText($"FPS: {_fpsEma:F0}   LOD: <{LodMediumDist:F0}m / <{LodLowDist:F0}m / <{LodImposterDist:F0}m / imposter   shadows: {ShadowMapSize}^2 PCF + IBL", 10, y, 16, new Color(160, 160, 160, 255));
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

        /// <summary>非索引网格焊接索引化：位置+骨骼属性一致的顶点合并（法线/UV 差异不参与——
        /// 蒙皮人群着色只用 tint，焊接后按三角面烘平法线，远距离不可辨）。
        /// 索引网格原样返回。焊容量按顶点数建字典，加载期一次性成本。</summary>
        private static Mesh EnsureIndexedMesh(Mesh mesh)
        {
            if (mesh.indices != null)
            {
                return mesh;
            }

            int vertexCount = mesh.vertexCount;
            if (vertexCount <= 0 || mesh.vertices == null)
            {
                throw new InvalidDataException("Weld requires a mesh with vertices.");
            }

            float* srcVerts = mesh.vertices;
            byte* srcBoneIds = mesh.boneIds;
            float* srcBoneWeights = mesh.boneWeights;

            Dictionary<string, int> weldMap = new(vertexCount);
            List<float> verts = new(vertexCount * 3);
            List<byte> boneIds = srcBoneIds != null ? new(vertexCount * 4) : new(0);
            List<float> boneWeights = srcBoneWeights != null ? new(vertexCount * 4) : new(0);
            List<ushort> indices = new(vertexCount);

            for (int v = 0; v < vertexCount; v++)
            {
                // 位置 1/1024、骨骼 id/权重（1/1024）进 key；共享位置但骨骼不同会裂开，必须参与
                int qx = (int)MathF.Round(srcVerts[v * 3 + 0] * 1024f);
                int qy = (int)MathF.Round(srcVerts[v * 3 + 1] * 1024f);
                int qz = (int)MathF.Round(srcVerts[v * 3 + 2] * 1024f);
                string key = $"{qx},{qy},{qz}";
                if (srcBoneIds != null)
                {
                    key += $",{srcBoneIds[v * 4 + 0]},{srcBoneIds[v * 4 + 1]},{srcBoneIds[v * 4 + 2]},{srcBoneIds[v * 4 + 3]}";
                }

                if (srcBoneWeights != null)
                {
                    key += $",{(int)MathF.Round(srcBoneWeights[v * 4 + 0] * 1024f)},{(int)MathF.Round(srcBoneWeights[v * 4 + 1] * 1024f)},{(int)MathF.Round(srcBoneWeights[v * 4 + 2] * 1024f)},{(int)MathF.Round(srcBoneWeights[v * 4 + 3] * 1024f)}";
                }

                if (!weldMap.TryGetValue(key, out int weldedIndex))
                {
                    weldedIndex = verts.Count / 3;
                    weldMap[key] = weldedIndex;
                    verts.Add(srcVerts[v * 3 + 0]);
                    verts.Add(srcVerts[v * 3 + 1]);
                    verts.Add(srcVerts[v * 3 + 2]);
                    if (srcBoneIds != null)
                    {
                        boneIds.Add(srcBoneIds[v * 4 + 0]);
                        boneIds.Add(srcBoneIds[v * 4 + 1]);
                        boneIds.Add(srcBoneIds[v * 4 + 2]);
                        boneIds.Add(srcBoneIds[v * 4 + 3]);
                    }

                    if (srcBoneWeights != null)
                    {
                        boneWeights.Add(srcBoneWeights[v * 4 + 0]);
                        boneWeights.Add(srcBoneWeights[v * 4 + 1]);
                        boneWeights.Add(srcBoneWeights[v * 4 + 2]);
                        boneWeights.Add(srcBoneWeights[v * 4 + 3]);
                    }
                }

                indices.Add((ushort)weldedIndex);
            }

            // 按三角面烘平法线（焊接丢掉了角点法线；远 LOD 平面着色足够）
            int weldedCount = verts.Count / 3;
            Span<float> normalsSpan = new float[weldedCount * 3];
            for (int tri = 0; tri < indices.Count; tri += 3)
            {
                int a = indices[tri] * 3, b = indices[tri + 1] * 3, c = indices[tri + 2] * 3;
                float ax = verts[a], ay = verts[a + 1], az = verts[a + 2];
                float e1x = verts[b] - ax, e1y = verts[b + 1] - ay, e1z = verts[b + 2] - az;
                float e2x = verts[c] - ax, e2y = verts[c + 1] - ay, e2z = verts[c + 2] - az;
                float nx = e1y * e2z - e1z * e2y;
                float ny = e1z * e2x - e1x * e2z;
                float nz = e1x * e2y - e1y * e2x;
                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 1e-12f)
                {
                    nx /= len; ny /= len; nz /= len;
                }

                for (int corner = 0; corner < 3; corner++)
                {
                    int dst = indices[tri + corner] * 3;
                    normalsSpan[dst] = nx;
                    normalsSpan[dst + 1] = ny;
                    normalsSpan[dst + 2] = nz;
                }
            }

            List<float> normals = new(normalsSpan.Length);
            normals.AddRange(normalsSpan);

            Mesh welded = default;
            welded.vertexCount = weldedCount;
            welded.triangleCount = indices.Count / 3;
            welded.vertices = AllocFloats(verts);
            welded.normals = AllocFloats(normals);
            if (boneIds.Count > 0) welded.boneIds = AllocBytes(boneIds);
            if (boneWeights.Count > 0) welded.boneWeights = AllocFloats(boneWeights);
            welded.indices = AllocUshorts(indices);
            Rl.UploadMesh(ref welded, false);
            Console.WriteLine($"[gpu-crowd-weld] {vertexCount} verts -> {welded.vertexCount} verts ({welded.triangleCount} tris, bones={(boneIds.Count > 0 ? "yes" : "NO")})");
            return welded;
        }

        private static float* AllocFloats(List<float> values)
        {
            IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(values.Count * sizeof(float));
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values);
            fixed (float* src = span)
            {
                System.Buffer.MemoryCopy(src, (void*)block, values.Count * sizeof(float), values.Count * sizeof(float));
            }

            return (float*)block;
        }

        private static byte* AllocBytes(List<byte> values)
        {
            IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(values.Count);
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values);
            fixed (byte* src = span)
            {
                System.Buffer.MemoryCopy(src, (void*)block, values.Count, values.Count);
            }

            return (byte*)block;
        }

        private static ushort* AllocUshorts(List<ushort> values)
        {
            IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(values.Count * sizeof(ushort));
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values);
            fixed (ushort* src = span)
            {
                System.Buffer.MemoryCopy(src, (void*)block, values.Count * sizeof(ushort), values.Count * sizeof(ushort));
            }

            return (ushort*)block;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _crowd?.Dispose();
            _shadowMap?.Dispose();
            _primitives?.Dispose();
            _disposed = true;
        }
    }
}
