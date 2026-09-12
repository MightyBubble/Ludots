using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Content.EngineGallery.Scenes;

/// <summary>
/// gpu_crowd / gpu_crowd_sim 共用的 PBR uniform 绑定：主 pass（gpu_crowd_preskin.vs + gpu_crowd_pbr.fs）
/// 与 imposter billboard 的光照/IBL/雾/阴影/相机基 uniform 解析与逐帧下发。
/// 采样单元约定见 GpuCrowdIndirectRenderer（albedo=1 / atlas=4 / env=5 / lut=6 / shadow=7）。
/// </summary>
public sealed unsafe class GpuCrowdPbrUniforms
{
    private const int ShadowTextureUnit = 7;
    private const float PbrRoughness = 0.55f;
    private const float PbrMetallic = 0.10f;

    private int _locLightDir = -1;
    private int _locLightColor = -1;
    private int _locLightIntensity = -1;
    private int _locAmbient = -1;
    private int _locViewPos = -1;
    private int _locFogColor = -1;
    private int _locFogParams = -1;
    private int _locSkyZenith = -1;
    private int _locSkyGround = -1;
    private int _locShadowEnabled = -1;
    private int _locShadowTexelWorld = -1;
    private int _locShadowBias = -1;
    private int _locShadowMapTexel = -1;
    private int _locLightSpaceMatrix = -1;
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
    private int _locImpEnvSpecular = -1;
    private int _locImpRoughness = -1;
    private int _locImpMetallic = -1;
    private int _locImpImposterSize = -1;
    private float _shadowTexelWorld;

    public void Resolve(Shader main, Shader imp)
    {
        _locLightDir = Rl.GetShaderLocation(main, "uLightDir");
        _locLightColor = Rl.GetShaderLocation(main, "uLightColor");
        _locLightIntensity = Rl.GetShaderLocation(main, "uLightIntensity");
        _locAmbient = Rl.GetShaderLocation(main, "uAmbient");
        _locViewPos = Rl.GetShaderLocation(main, "uViewPos");
        _locFogColor = Rl.GetShaderLocation(main, "uFogColor");
        _locFogParams = Rl.GetShaderLocation(main, "uFogParams");
        _locSkyZenith = Rl.GetShaderLocation(main, "uSkyZenith");
        _locSkyGround = Rl.GetShaderLocation(main, "uSkyGround");
        _locShadowEnabled = Rl.GetShaderLocation(main, "uShadowEnabled");
        _locShadowTexelWorld = Rl.GetShaderLocation(main, "uShadowTexelWorld");
        _locShadowBias = Rl.GetShaderLocation(main, "uShadowBias");
        _locShadowMapTexel = Rl.GetShaderLocation(main, "uShadowMapTexel");
        _locLightSpaceMatrix = Rl.GetShaderLocation(main, "uLightSpaceMatrix");
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
        _locImpEnvSpecular = Rl.GetShaderLocation(imp, "uEnvSpecular");
        _locImpRoughness = Rl.GetShaderLocation(imp, "uRoughness");
        _locImpMetallic = Rl.GetShaderLocation(imp, "uMetallic");

        int[][] required =
        {
            new[] { _locLightDir, _locLightColor, _locLightIntensity, _locAmbient, _locViewPos, _locFogColor, _locFogParams,
                    _locSkyZenith, _locSkyGround, _locShadowEnabled, _locShadowTexelWorld, _locShadowBias, _locShadowMapTexel, _locLightSpaceMatrix },
            new[] { _locImpCamPos, _locImpCamRight, _locImpCamUp, _locImpImposterSize, _locImpViewPos, _locImpLightDir,
                    _locImpLightColor, _locImpLightIntensity, _locImpAmbient, _locImpFogColor, _locImpFogParams,
                    _locImpSkyZenith, _locImpSkyGround, _locImpShadowMap, _locImpLightSpaceMatrix, _locImpShadowEnabled,
                    _locImpShadowTexelWorld, _locImpShadowBias, _locImpRoughness, _locImpMetallic },
        };
        foreach (int[] group in required)
        {
            foreach (int loc in group)
            {
                if (loc < 0)
                {
                    throw new InvalidOperationException("gpu_crowd PBR/imposter uniform 合同不完整（编译器可能优化了未用 uniform）。");
                }
            }
        }

        if (_locImpEnvSpecular < 0)
        {
            _locImpEnvSpecular = -1; // 远景降采样档允许省略
        }
    }

    /// <summary>一次性 uniform：PBR 标量 + 采样单元 + billboard 尺寸。Resolve 之后调用一次。</summary>
    public void ApplyStatic(Shader main, Shader imp, float imposterHalfWidth, float imposterHeight, float instanceScale, float shadowTexelWorld)
    {
        _shadowTexelWorld = shadowTexelWorld;
        float roughness = PbrRoughness, metallic = PbrMetallic, envSpecular = 1f;
        Rl.SetShaderValue(main, Rl.GetShaderLocation(main, "uRoughness"), &roughness, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Rl.SetShaderValue(main, Rl.GetShaderLocation(main, "uMetallic"), &metallic, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Rl.SetShaderValue(main, Rl.GetShaderLocation(main, "uEnvSpecular"), &envSpecular, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        int albedoUnit = (int)GpuCrowdIndirectRenderer.AlbedoTextureUnit;
        Rl.SetShaderValue(main, Rl.GetShaderLocation(main, "uAlbedoMap"), &albedoUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);
        int envUnit = (int)GpuCrowdIndirectRenderer.EnvCubemapUnit;
        Rl.SetShaderValue(main, Rl.GetShaderLocation(main, "uPrefilteredEnv"), &envUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);
        int lutUnit = (int)GpuCrowdIndirectRenderer.BrdfLutUnit;
        Rl.SetShaderValue(main, Rl.GetShaderLocation(main, "uBrdfLut"), &lutUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);
        int shadowUnit = ShadowTextureUnit;
        Rl.SetShaderValue(main, Rl.GetShaderLocation(main, "uShadowMap"), &shadowUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);

        Rl.SetShaderValue(imp, _locImpRoughness, &roughness, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Rl.SetShaderValue(imp, _locImpMetallic, &metallic, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        if (_locImpEnvSpecular >= 0)
        {
            Rl.SetShaderValue(imp, _locImpEnvSpecular, &envSpecular, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        Rl.SetShaderValue(imp, _locImpShadowMap, &shadowUnit, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_SAMPLER2D);
        Vector2 imposterSize = new Vector2(imposterHalfWidth, imposterHeight) * instanceScale;
        Rl.SetShaderValue(imp, _locImpImposterSize, &imposterSize, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC2);
    }

    /// <summary>逐帧 uniform：光照/雾/天空/阴影矩阵 + billboard 相机基 + uTime。</summary>
    public void ApplyFrame(Shader main, Shader imp, RaylibFrameLighting lighting, RaylibDirectionalShadowMap shadowMap, Camera3D camera, float t)
    {
        Vector3 camPos = camera.position;
        Vector3 lightDir = lighting.SunDirectionToward;
        Vector3 lightColor = lighting.LightColor;
        float lightIntensity = lighting.LightIntensity;
        Vector4 ambient = lighting.AmbientRgba;
        Vector3 fogColor = lighting.FogColor;
        Vector4 fogParams = lighting.FogParams;
        Vector3 skyZenith = lighting.SkyZenithColor;
        Vector3 skyGround = lighting.SkyGroundColor;
        float shadowEnabled = 1f;
        float shadowBias = shadowMap.ReceiverBiasWorld / shadowMap.DepthRange;
        float shadowMapTexel = 1f / shadowMap.MapSize;
        float shadowTexelWorld = _shadowTexelWorld;

        Rl.SetShaderValue(main, _locLightDir, &lightDir, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        Rl.SetShaderValue(main, _locLightColor, &lightColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        Rl.SetShaderValue(main, _locLightIntensity, &lightIntensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Rl.SetShaderValue(main, _locAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        Rl.SetShaderValue(main, _locViewPos, &camPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        Rl.SetShaderValue(main, _locFogColor, &fogColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        Rl.SetShaderValue(main, _locFogParams, &fogParams, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        Rl.SetShaderValue(main, _locSkyZenith, &skyZenith, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        Rl.SetShaderValue(main, _locSkyGround, &skyGround, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        Rl.SetShaderValue(main, _locShadowEnabled, &shadowEnabled, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Rl.SetShaderValue(main, _locShadowTexelWorld, &shadowTexelWorld, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Rl.SetShaderValue(main, _locShadowBias, &shadowBias, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Rl.SetShaderValue(main, _locShadowMapTexel, &shadowMapTexel, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Rl.SetShaderValueMatrix(main, _locLightSpaceMatrix, shadowMap.LightViewProjection);
        Rl.SetShaderValue(main, Rl.GetShaderLocation(main, "uTime"), &t, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

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
        Rl.SetShaderValueMatrix(imp, _locImpLightSpaceMatrix, shadowMap.LightViewProjection);
        Rl.SetShaderValue(imp, Rl.GetShaderLocation(imp, "uTime"), &t, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

        // billboard 相机基（quad 朝向相机；视角桶按 相机→实例 方位角选）
        Vector3 camForward = Vector3.Normalize(camera.target - camera.position);
        Vector3 camRight = Vector3.Normalize(Vector3.Cross(camForward, Vector3.UnitY));
        Vector3 camUp = Vector3.Cross(camRight, camForward);
        Rl.SetShaderValue(imp, _locImpCamPos, &camPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        Rl.SetShaderValue(imp, _locImpCamRight, &camRight, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        Rl.SetShaderValue(imp, _locImpCamUp, &camUp, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
    }
}
