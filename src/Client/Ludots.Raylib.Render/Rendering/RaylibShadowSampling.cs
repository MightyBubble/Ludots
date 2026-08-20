using System;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    internal readonly struct RaylibShadowSamplingLocations
    {
        public readonly int ShadowMap;
        public readonly int LightSpaceMatrix;
        public readonly int ShadowEnabled;
        public readonly int ShadowTexelWorld;
        public readonly int ShadowBias;

        private RaylibShadowSamplingLocations(
            int shadowMap,
            int lightSpaceMatrix,
            int shadowEnabled,
            int shadowTexelWorld,
            int shadowBias)
        {
            ShadowMap = shadowMap;
            LightSpaceMatrix = lightSpaceMatrix;
            ShadowEnabled = shadowEnabled;
            ShadowTexelWorld = shadowTexelWorld;
            ShadowBias = shadowBias;
        }

        public static unsafe RaylibShadowSamplingLocations ResolveOrThrow(
            Shader shader,
            string shaderLabel,
            Rl.ShaderLocationIndex shaderTextureSlot)
        {
            int shadowMap = RaylibShaderBindingGuard.RequireUniform(shader, "uShadowMap", shaderLabel);
            int lightSpaceMatrix = RaylibShaderBindingGuard.RequireUniform(shader, "uLightSpaceMatrix", shaderLabel);
            int shadowEnabled = RaylibShaderBindingGuard.RequireUniform(shader, "uShadowEnabled", shaderLabel);
            int shadowTexelWorld = RaylibShaderBindingGuard.RequireUniform(shader, "uShadowTexelWorld", shaderLabel);
            int shadowBias = RaylibShaderBindingGuard.RequireUniform(shader, "uShadowBias", shaderLabel);
            shader.locs[(int)shaderTextureSlot] = shadowMap;
            return new RaylibShadowSamplingLocations(shadowMap, lightSpaceMatrix, shadowEnabled, shadowTexelWorld, shadowBias);
        }

        public unsafe void ApplyUniforms(Shader shader, RaylibDirectionalShadowMap? shadow, float shadowTexelWorld)
        {
            if (shadowTexelWorld <= 0f || !float.IsFinite(shadowTexelWorld))
            {
                throw new ArgumentOutOfRangeException(nameof(shadowTexelWorld), shadowTexelWorld, "Shadow texel world size must be positive and finite.");
            }

            float enabled = shadow != null ? 1f : 0f;
            float bias = shadow != null
                ? RaylibDirectionalShadowMap.DefaultReceiverBiasWorld / shadow.DepthRange
                : 0f;
            Rl.SetShaderValue(shader, ShadowEnabled, &enabled, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(shader, ShadowTexelWorld, &shadowTexelWorld, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(shader, ShadowBias, &bias, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            if (shadow != null)
            {
                Rl.SetShaderValueMatrix(shader, LightSpaceMatrix, shadow.LightViewProjection);
            }
        }
    }

    internal static class RaylibShadowSampling
    {
        public const Rl.MaterialMapIndex MaterialSlot = Rl.MaterialMapIndex.MATERIAL_MAP_EMISSION;
        public const Rl.ShaderLocationIndex ShaderTextureSlot = Rl.ShaderLocationIndex.SHADER_LOC_MAP_EMISSION;

        public static unsafe void BindTexture(ref Material material, RaylibDirectionalShadowMap? shadow)
        {
            if (material.maps == null)
            {
                return;
            }

            if (shadow == null)
            {
                material.maps[(int)MaterialSlot].texture = default;
                return;
            }

            Rl.SetMaterialTexture(ref material, (int)MaterialSlot, shadow.DepthTexture);
        }

        public static unsafe void ClearTexture(ref Material material)
        {
            if (material.maps == null)
            {
                return;
            }

            material.maps[(int)MaterialSlot].texture = default;
        }
    }
}
