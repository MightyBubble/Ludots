using System;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 外部文件模型（glTF/GLB/OBJ 经 LoadModel 装载）的逐材质 PBR 绘制通道：
    /// model_file_lit 着色器（GGX + split-sum 天空 IBL + 深度阴影，词汇与 model_lit 一致），
    /// 在此之上逐网格采样 glTF 装载器放进材质槽的贴图——albedo/法线/ORM/自发光。
    /// 槽位合同：EMISSION 槽承载 glTF 自发光贴图（阴影深度纹理因此走 glTF 不使用的
    /// HEIGHT 槽，区别于 model_lit 的 EMISSION 槽阴影约定）；METALNESS 与 ROUGHNESS
    /// 两个 shader locs 同指 uOrmMap——raylib 5.5 把 metallicRoughness 贴图放 ROUGHNESS 槽。
    /// uViewMode/uScalarOverride 供资产验收拆通道查看与"贴图 PBR vs 缺省标量 PBR"消融。
    /// </summary>
    public sealed unsafe class RaylibFileModelLit : IDisposable
    {
        private const int ShadowMapSlot = (int)Rl.MaterialMapIndex.MATERIAL_MAP_HEIGHT;
        private const int OrmSlotA = (int)Rl.MaterialMapIndex.MATERIAL_MAP_METALNESS;
        private const int OrmSlotB = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ROUGHNESS;

        public const float DefaultRoughness = 0.8f;
        public const float DefaultMetallic = 0f;

        public enum ViewMode
        {
            Final = 0,
            Albedo = 1,
            Normals = 2,
            Metallic = 3,
            Roughness = 4,
        }

        /// <summary>单材质的贴图通道与 PBR 因子读数（HUD/验收结论的数据源）。</summary>
        public readonly struct MaterialInspection
        {
            public readonly bool HasAlbedo;
            public readonly bool HasNormal;
            public readonly bool HasOrm;
            public readonly bool HasEmissive;
            public readonly float RoughnessFactor;
            public readonly float MetallicFactor;

            public MaterialInspection(bool hasAlbedo, bool hasNormal, bool hasOrm, bool hasEmissive, float roughnessFactor, float metallicFactor)
            {
                HasAlbedo = hasAlbedo;
                HasNormal = hasNormal;
                HasOrm = hasOrm;
                HasEmissive = hasEmissive;
                RoughnessFactor = roughnessFactor;
                MetallicFactor = metallicFactor;
            }
        }

        private readonly Shader _shader;
        private readonly RaylibFrameLightingLocations _lightingLocations;
        private readonly int _locTint;
        private readonly int _locRoughness;
        private readonly int _locMetallic;
        private readonly int _locHasNormal;
        private readonly int _locHasOrm;
        private readonly int _locHasEmissive;
        private readonly int _locScalarOverride;
        private readonly int _locViewMode;
        private readonly int _locAlphaCutoff;
        private readonly int _locSkyZenith;
        private readonly int _locSkyGround;
        private readonly int _locEnvSpecular;
        private readonly int _locShadowMap;
        private readonly int _locLightSpaceMatrix;
        private readonly int _locShadowEnabled;
        private readonly int _locShadowTexelWorld;
        private readonly int _locShadowBias;
        private readonly int _locShadowMapTexel;
        private RaylibSkyIbl? _skyIbl;
        private RaylibFrameLighting? _lighting;
        private RaylibDirectionalShadowMap? _shadow;
        private bool _disposed;

        /// <summary>验收消融开关：true 时忽略贴图通道，用 <see cref="OverrideRoughness"/>/<see cref="OverrideMetallic"/> 的缺省标量 PBR。</summary>
        public bool ScalarOverride { get; set; }

        public float OverrideRoughness { get; set; } = DefaultRoughness;

        public float OverrideMetallic { get; set; } = DefaultMetallic;

        public ViewMode Mode { get; set; } = ViewMode.Final;

        /// <summary>alpha 低于该值剔除；0 关闭剔除（glTF alphaMode 信息不进 raylib Model，验收运行时可调）。</summary>
        public float AlphaCutoff { get; set; } = 0.1f;

        public float EnvSpecular { get; set; } = 1f;

        public RaylibFileModelLit()
        {
            string baseDir = AppContext.BaseDirectory;
            _shader = RaylibShaderLoader.Load(baseDir, "model_file_lit.vs", "model_file_lit.fs", "model_file_lit");

            _lightingLocations = RaylibFrameLightingLocations.ResolveOrThrow(_shader, "model_file_lit");
            _locTint = RequireLocation("tint");
            _locRoughness = RequireLocation("uRoughness");
            _locMetallic = RequireLocation("uMetallic");
            _locHasNormal = RequireLocation("uHasNormal");
            _locHasOrm = RequireLocation("uHasOrm");
            _locHasEmissive = RequireLocation("uHasEmissive");
            _locScalarOverride = RequireLocation("uScalarOverride");
            _locViewMode = RequireLocation("uViewMode");
            _locAlphaCutoff = RequireLocation("uAlphaCutoff");
            _locSkyZenith = RequireLocation("uSkyZenith");
            _locSkyGround = RequireLocation("uSkyGround");
            _locEnvSpecular = RequireLocation("uEnvSpecular");
            _locShadowMap = RaylibShaderBindingGuard.RequireUniform(_shader, "uShadowMap", "model_file_lit");
            _locLightSpaceMatrix = RaylibShaderBindingGuard.RequireUniform(_shader, "uLightSpaceMatrix", "model_file_lit");
            _locShadowEnabled = RaylibShaderBindingGuard.RequireUniform(_shader, "uShadowEnabled", "model_file_lit");
            _locShadowTexelWorld = RaylibShaderBindingGuard.RequireUniform(_shader, "uShadowTexelWorld", "model_file_lit");
            _locShadowBias = RaylibShaderBindingGuard.RequireUniform(_shader, "uShadowBias", "model_file_lit");
            _locShadowMapTexel = RaylibShaderBindingGuard.RequireUniform(_shader, "uShadowMapTexel", "model_file_lit");
            int locMvp = RaylibShaderBindingGuard.RequireUniform(_shader, "mvp", "model_file_lit");
            int locMatModel = RaylibShaderBindingGuard.RequireUniform(_shader, "matModel", "model_file_lit");
            int locVertexPosition = RaylibShaderBindingGuard.RequireAttribute(_shader, "vertexPosition", "model_file_lit");
            int locVertexTexCoord = RaylibShaderBindingGuard.RequireAttribute(_shader, "vertexTexCoord", "model_file_lit");
            int locVertexNormal = RaylibShaderBindingGuard.RequireAttribute(_shader, "vertexNormal", "model_file_lit");
            int locMapAlbedo = RaylibShaderBindingGuard.RequireUniform(_shader, "texture0", "model_file_lit");
            int locColDiffuse = RaylibShaderBindingGuard.RequireUniform(_shader, "colDiffuse", "model_file_lit");
            int locNormalMap = RaylibShaderBindingGuard.RequireUniform(_shader, "uNormalMap", "model_file_lit");
            int locOrmMap = RaylibShaderBindingGuard.RequireUniform(_shader, "uOrmMap", "model_file_lit");
            int locEmissiveMap = RaylibShaderBindingGuard.RequireUniform(_shader, "uEmissiveMap", "model_file_lit");

            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locVertexNormal;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locMatModel;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = locColDiffuse;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_NORMAL] = locNormalMap;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_METALNESS] = locOrmMap;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ROUGHNESS] = locOrmMap;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_EMISSION] = locEmissiveMap;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_HEIGHT] = _locShadowMap;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_CUBEMAP] = RequireLocation("uPrefilteredEnv");
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_BRDF] = RequireLocation("uBrdfLut");

            // 构造期已持 GL 上下文（LoadShader），BRDF LUT 烘焙移出首帧 Draw。
            _skyIbl = new RaylibSkyIbl();
            _skyIbl.PrewarmLut();
        }

        /// <summary>帧级状态：光照总线 + 相机视点 + 阴影源（null 关闭阴影接收）。</summary>
        public void BeginFrame(RaylibFrameLighting lighting, Vector3 viewPos, RaylibDirectionalShadowMap? shadow = null, float shadowTexelWorld = 0.04f)
        {
            ThrowIfDisposed();
            _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            _shadow = shadow;

            lighting.Apply(_shader, in _lightingLocations);
            lighting.ApplyViewPosition(_shader, in _lightingLocations, viewPos);

            Vector3 zenith = lighting.SkyZenithColor;
            Vector3 ground = lighting.SkyGroundColor;
            Rl.SetShaderValue(_shader, _locSkyZenith, &zenith, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_shader, _locSkyGround, &ground, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);

            _skyIbl!.Ensure(lighting);
            float envSpecular = EnvSpecular;
            Rl.SetShaderValue(_shader, _locEnvSpecular, &envSpecular, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

            float scalarOverride = ScalarOverride ? 1f : 0f;
            Rl.SetShaderValue(_shader, _locScalarOverride, &scalarOverride, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            int viewMode = (int)Mode;
            Rl.SetShaderValue(_shader, _locViewMode, &viewMode, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            float alphaCutoff = AlphaCutoff;
            Rl.SetShaderValue(_shader, _locAlphaCutoff, &alphaCutoff, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

            float shadowEnabled = shadow != null ? 1f : 0f;
            float shadowBias = shadow != null ? shadow.ReceiverBiasWorld / shadow.DepthRange : 0f;
            float shadowMapTexel = shadow != null ? 1f / shadow.MapSize : 0f;
            Rl.SetShaderValue(_shader, _locShadowEnabled, &shadowEnabled, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locShadowTexelWorld, &shadowTexelWorld, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locShadowBias, &shadowBias, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locShadowMapTexel, &shadowMapTexel, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            if (shadow != null)
            {
                Rl.SetShaderValueMatrix(_shader, _locLightSpaceMatrix, shadow.LightViewProjection);
            }
        }

        /// <summary>把着色器挂到模型全部材质（DrawModel 之前必须调用一次）。</summary>
        public void AttachToModel(Model model)
        {
            ThrowIfDisposed();
            for (int i = 0; i < model.materialCount; i++)
            {
                model.materials[i].shader = _shader;
            }
        }

        /// <summary>装载前把注入槽（IBL/阴影）指向本通道管理的纹理；UnloadModel 前必须调用，防误删外部纹理。</summary>
        public void DetachInjectedTextures(Model model)
        {
            for (int i = 0; i < model.materialCount; i++)
            {
                model.materials[i].maps[ShadowMapSlot].texture = default;
                model.materials[i].maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP].texture = default;
                model.materials[i].maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_BRDF].texture = default;
            }
        }

        /// <summary>逐网格绘制：逐材质写贴图通道 flag 与 PBR 因子，注入 IBL/阴影纹理后 DrawMesh。</summary>
        public void DrawModel(Model model, RaylibMatrix transform)
        {
            ThrowIfDisposed();
            if (_lighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibFileModelLit)} requires {nameof(BeginFrame)} before draws (lighting + shadow state).");
            }

            if (model.meshCount > 0 && model.meshMaterial == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibFileModelLit)} model has {model.meshCount} meshes but no meshMaterial mapping.");
            }

            Material* materials = model.materials;
            for (int i = 0; i < model.meshCount; i++)
            {
                int materialIndex = model.meshMaterial[i];
                if (materialIndex < 0 || materialIndex >= model.materialCount)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibFileModelLit)} mesh[{i}] materialIndex={materialIndex} out of range (materialCount={model.materialCount}).");
                }

                Material* material = &materials[materialIndex];
                MaterialInspection inspection = Inspect(material);
                DrawMaterial(material, in inspection, model.meshes[i], transform);
            }
        }

        /// <summary>全模型材质读数（HUD/验收结论数据源）。</summary>
        public static MaterialInspection[] Inspect(Model model)
        {
            var result = new MaterialInspection[model.materialCount];
            for (int i = 0; i < model.materialCount; i++)
            {
                result[i] = Inspect(&model.materials[i]);
            }

            return result;
        }

        private static MaterialInspection Inspect(Material* material)
        {
            MaterialMap* maps = material->maps;
            bool hasAlbedo = maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO].texture.id > 0;
            bool hasNormal = maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_NORMAL].texture.id > 0;
            bool hasOrm = maps[OrmSlotA].texture.id > 0 || maps[OrmSlotB].texture.id > 0;
            bool hasEmissive = maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_EMISSION].texture.id > 0;
            float roughnessFactor = maps[OrmSlotB].value;
            float metallicFactor = maps[OrmSlotA].value;
            return new MaterialInspection(hasAlbedo, hasNormal, hasOrm, hasEmissive, roughnessFactor, metallicFactor);
        }

        private void DrawMaterial(Material* material, in MaterialInspection inspection, Mesh mesh, RaylibMatrix transform)
        {
            float roughness;
            float metallic;
            if (ScalarOverride)
            {
                roughness = OverrideRoughness;
                metallic = OverrideMetallic;
            }
            else if (inspection.HasOrm)
            {
                // glTF：value = factor × 贴图采样；raylib 未写因子时按 spec 缺省 1.0（仅贴图）。
                roughness = inspection.RoughnessFactor > 0f ? inspection.RoughnessFactor : 1f;
                metallic = inspection.MetallicFactor > 0f ? inspection.MetallicFactor : 1f;
            }
            else
            {
                // 无 ORM 贴图且因子缺失（OBJ 或未写因子）：显示缺省 PBR，HUD 披露。
                roughness = inspection.RoughnessFactor > 0f ? inspection.RoughnessFactor : DefaultRoughness;
                metallic = inspection.MetallicFactor > 0f ? inspection.MetallicFactor : DefaultMetallic;
            }

            float hasNormal = inspection.HasNormal ? 1f : 0f;
            float hasOrm = inspection.HasOrm ? 1f : 0f;
            float hasEmissive = inspection.HasEmissive ? 1f : 0f;
            Rl.SetShaderValue(_shader, _locHasNormal, &hasNormal, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locHasOrm, &hasOrm, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locHasEmissive, &hasEmissive, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locRoughness, &roughness, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locMetallic, &metallic, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Vector4 tint = new(1f, 1f, 1f, 1f);
            Rl.SetShaderValue(_shader, _locTint, &tint, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);

            // IBL 重烘后纹理 id 会变、阴影每帧更新，注入槽逐材质重挂；glTF 自有槽不触碰。
            Rl.SetMaterialTexture(ref *material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP, _skyIbl!.EnvCubemap);
            Rl.SetMaterialTexture(ref *material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_BRDF, _skyIbl.BrdfLut);
            if (_shadow != null)
            {
                Rl.SetMaterialTexture(ref *material, ShadowMapSlot, _shadow.DepthTexture);
            }

            Rl.DrawMesh(mesh, *material, transform);
        }

        private int RequireLocation(string name)
        {
            int loc = Rl.GetShaderLocation(_shader, name);
            if (loc < 0)
            {
                throw new InvalidOperationException($"model_file_lit shader uniform '{name}' not found.");
            }

            return loc;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _skyIbl?.Dispose();
            _skyIbl = null;
            Rl.UnloadShader(_shader);
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibFileModelLit));
            }
        }
    }
}
