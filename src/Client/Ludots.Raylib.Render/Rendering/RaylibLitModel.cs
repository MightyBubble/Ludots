using System;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 单物体带光照绘制通道：model_lit 着色器（Cook-Torrance GGX + 解析式天空半球 IBL）。
    /// 补齐"不走合批管线就没有明暗"的底座缺口；光照 uniform 词汇与 instancing 管线一致。
    /// 阴影由 RaylibPlanarShadows 平面投影承担（方向光 → 地面）。
    /// </summary>
    public sealed unsafe class RaylibLitModel : IDisposable
    {
        private static readonly int ShadowSamplerSlot = (int)Rl.MaterialMapIndex.MATERIAL_MAP_NORMAL;

        private readonly Shader _shader;
        private Material _material;
        private readonly RaylibFrameLightingLocations _lightingLocations;
        private readonly int _locTint;
        private readonly int _locRoughness;
        private readonly int _locMetallic;
        private readonly int _locSkyZenith;
        private readonly int _locSkyGround;
        private readonly int _locShadowMap;
        private readonly int _locLightSpaceMatrix;
        private readonly int _locShadowEnabled;
        private readonly int _locShadowTexelWorld;
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
            _locShadowMap = RequireLocation("uShadowMap");
            _locLightSpaceMatrix = RequireLocation("uLightSpaceMatrix");
            _locShadowEnabled = RequireLocation("uShadowEnabled");
            _locShadowTexelWorld = RequireLocation("uShadowTexelWorld");
            _shader.locs[ShadowSamplerSlot] = _locShadowMap;

            _material = Rl.LoadMaterialDefault();
            _material.shader = _shader;
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

            float shadowEnabled = shadow != null ? 1f : 0f;
            Rl.SetShaderValue(_shader, _locShadowEnabled, &shadowEnabled, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locShadowTexelWorld, &shadowTexelWorld, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            if (shadow != null)
            {
                Rl.SetMaterialTexture(ref _material, ShadowSamplerSlot, shadow.DepthTexture);
                Rl.SetShaderValueMatrix(_shader, _locLightSpaceMatrix, shadow.LightViewProjection);
            }
        }

        /// <summary>挂到模型材质接收阴影（DrawModelEx 路径每帧调用）。</summary>
        public void BindShadowToMaterial(ref Material material, RaylibDirectionalShadowMap shadow)
        {
            Rl.SetMaterialTexture(ref material, ShadowSamplerSlot, shadow.DepthTexture);
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

            _material.shader = default;
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
