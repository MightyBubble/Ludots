using System;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 单物体带光照绘制通道：model_lit 着色器（Cook-Torrance GGX + split-sum 天空 IBL——
    /// RaylibSkyIbl CPU 烘焙预滤波环境立方图与 BRDF LUT）。补齐"不走合批管线就没有明暗"
    /// 的底座缺口；光照 uniform 词汇与 instancing 管线一致。
    /// 阴影由 RaylibDirectionalShadowMap 深度 shadow map 承担（native 5.5）。
    /// </summary>
    public sealed unsafe class RaylibLitModel : IDisposable
    {
        private static readonly int ShadowMapSlot = (int)Rl.MaterialMapIndex.MATERIAL_MAP_NORMAL;
        private static readonly int ShadowLocIndex = (int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_NORMAL;

        private readonly Shader _shader;
        private Material _material;
        private readonly RaylibFrameLightingLocations _lightingLocations;
        private readonly int _locTint;
        private readonly int _locRoughness;
        private readonly int _locMetallic;
        private readonly int _locSkyZenith;
        private readonly int _locSkyGround;
        private readonly int _locEnvSpecular;
        private readonly int _locShadowMap;
        private readonly int _locLightSpaceMatrix;
        private readonly int _locShadowEnabled;
        private readonly int _locShadowTexelWorld;
        private RaylibSkyIbl? _skyIbl;
        private RaylibFrameLighting? _lighting;
        private bool _disposed;

        public RaylibLitModel()
        {
            string baseDir = AppContext.BaseDirectory;
            _shader = Rl.LoadShader(
                Path.Combine(baseDir, "model_lit.vs"),
                Path.Combine(baseDir, "model_lit.fs"));
            if (_shader.id == 0)
            {
                throw new InvalidOperationException("Failed to load model_lit shader (shader.id == 0).");
            }

            _lightingLocations = RaylibFrameLightingLocations.ResolveOrThrow(_shader, "model_lit");
            _locTint = RequireLocation("tint");
            _locRoughness = RequireLocation("uRoughness");
            _locMetallic = RequireLocation("uMetallic");
            _locSkyZenith = RequireLocation("uSkyZenith");
            _locSkyGround = RequireLocation("uSkyGround");
            _locEnvSpecular = RequireLocation("uEnvSpecular");
            _locShadowMap = RequireLocation("uShadowMap");
            _locLightSpaceMatrix = RequireLocation("uLightSpaceMatrix");
            _locShadowEnabled = RequireLocation("uShadowEnabled");
            _locShadowTexelWorld = RequireLocation("uShadowTexelWorld");
            _shader.locs[ShadowLocIndex] = _locShadowMap;

            // split-sum IBL 采样器：cubemap 走 MATERIAL_MAP_CUBEMAP 槽位（native 5.5
            // DrawMesh 对该槽以 GL_TEXTURE_CUBE_MAP 绑定并回填 uniform=槽位号），LUT 走 BRDF 槽。
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_CUBEMAP] = RequireLocation("uPrefilteredEnv");
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_BRDF] = RequireLocation("uBrdfLut");

            _material = Rl.LoadMaterialDefault();
            _material.shader = _shader;

            // 构造期已持 GL 上下文（LoadShader），把与光照无关的 BRDF LUT 烘焙移出首帧 Draw。
            _skyIbl = new RaylibSkyIbl();
            _skyIbl.PrewarmLut();
        }

        public Shader Shader => _shader;

        /// <summary>帧级状态：光照总线 + 相机视点 + 阴影源（null 关闭阴影接收）。</summary>
        public void BeginFrame(RaylibFrameLighting lighting, Vector3 viewPos, RaylibDirectionalShadowMap? shadow = null, float shadowTexelWorld = 0.04f)
        {
            ThrowIfDisposed();
            _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));

            lighting.Apply(_shader, in _lightingLocations);
            lighting.ApplyViewPosition(_shader, in _lightingLocations, viewPos);

            Vector3 zenith = lighting.SkyZenithColor;
            Vector3 ground = lighting.SkyGroundColor;
            Rl.SetShaderValue(_shader, _locSkyZenith, &zenith, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_shader, _locSkyGround, &ground, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);

            _skyIbl.Ensure(lighting);
            float envSpecular = 1f;
            Rl.SetShaderValue(_shader, _locEnvSpecular, &envSpecular, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetMaterialTexture(ref _material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP, _skyIbl.EnvCubemap);
            Rl.SetMaterialTexture(ref _material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_BRDF, _skyIbl.BrdfLut);

            float shadowEnabled = shadow != null ? 1f : 0f;
            Rl.SetShaderValue(_shader, _locShadowEnabled, &shadowEnabled, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locShadowTexelWorld, &shadowTexelWorld, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            if (shadow != null)
            {
                Rl.SetMaterialTexture(ref _material, ShadowMapSlot, shadow.DepthTexture);
                Rl.SetShaderValueMatrix(_shader, _locLightSpaceMatrix, shadow.LightViewProjection);
            }
        }

        /// <summary>挂到模型材质接收阴影（DrawModelEx 路径每帧调用）。</summary>
        public void BindShadowToMaterial(ref Material material, RaylibDirectionalShadowMap shadow)
        {
            Rl.SetMaterialTexture(ref material, ShadowMapSlot, shadow.DepthTexture);
        }

        /// <summary>把 IBL 采样纹理挂到模型材质（DrawModelEx 路径每帧调用；重烘后纹理 id 会变）。</summary>
        public void BindIblToMaterial(ref Material material)
        {
            ThrowIfDisposed();
            if (_skyIbl == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibLitModel)} requires {nameof(BeginFrame)} before binding IBL maps.");
            }

            Rl.SetMaterialTexture(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP, _skyIbl.EnvCubemap);
            Rl.SetMaterialTexture(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_BRDF, _skyIbl.BrdfLut);
        }

        public void DrawMesh(Mesh mesh, RaylibMatrix transform, Vector4 tint, float roughness = 0.85f, float metallic = 0f)
        {
            ThrowIfDisposed();
            EnsureFrame();
            ApplyDrawUniforms(tint, roughness, metallic);
            Rl.DrawMesh(mesh, _material, transform);
        }

        /// <summary>把着色器挂到模型的全部材质上；随后 DrawModelEx 携带光照。</summary>
        public void AttachToModel(Model model)
        {
            ThrowIfDisposed();
            for (int i = 0; i < model.materialCount; i++)
            {
                model.materials[i].shader = _shader;
            }
        }

        /// <summary>AttachToModel + DrawModelEx 路径绘制前手动设置逐物件 uniform。</summary>
        public void ApplyDrawUniforms(Vector4 tint, float roughness = 0.85f, float metallic = 0f)
        {
            ThrowIfDisposed();
            EnsureFrame();
            Rl.SetShaderValue(_shader, _locTint, &tint, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            Rl.SetShaderValue(_shader, _locRoughness, &roughness, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locMetallic, &metallic, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        private void EnsureFrame()
        {
            if (_lighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibLitModel)} requires {nameof(BeginFrame)} before draws (lighting + shadow state).");
            }
        }

        private int RequireLocation(string name)
        {
            int loc = Rl.GetShaderLocation(_shader, name);
            if (loc < 0)
            {
                throw new InvalidOperationException($"model_lit shader uniform '{name}' not found.");
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
            _material.shader = default;
            // UnloadMaterial 会删除材质槽上的全部纹理；IBL 纹理归 RaylibSkyIbl 所有，先清槽防双删。
            _material.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP].texture = default;
            _material.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_BRDF].texture = default;
            Rl.UnloadMaterial(_material);
            Rl.UnloadShader(_shader);
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibLitModel));
            }
        }
    }
}
