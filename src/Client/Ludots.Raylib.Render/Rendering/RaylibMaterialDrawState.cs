using System;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    internal static class RaylibMaterialDrawState
    {
        public static MaterialBlendMode ResolveBlendMode(
            IRenderMaterialAssets? materials,
            int materialId,
            MaterialBlendMode defaultWhenMissing,
            string callerName)
        {
            if (materialId <= 0)
            {
                return defaultWhenMissing;
            }

            if (materials == null)
            {
                throw new InvalidOperationException(
                    $"{callerName} received materialId={materialId} but no {nameof(IRenderMaterialAssets)} was provided.");
            }

            if (!materials.TryGet(materialId, out MaterialAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"{callerName} cannot resolve blend mode for unknown materialId={materialId}.");
            }

            return MaterialBlendModeResolver.Resolve(descriptor.Flags);
        }

        /// <summary>shaderKey 分派仅实例化合批车道支持；其余车道遇非默认 key 必须 fail-loud，不静默降级。</summary>
        public static void RequireLaneShaderKey(
            IRenderMaterialAssets? materials,
            int materialId,
            string callerName)
        {
            if (materialId <= 0)
            {
                return;
            }

            if (materials == null)
            {
                throw new InvalidOperationException(
                    $"{callerName} received materialId={materialId} but no {nameof(IRenderMaterialAssets)} was provided.");
            }

            if (!materials.TryResolve(materialId, out ResolvedMaterialAsset resolved))
            {
                throw new InvalidOperationException(
                    $"{callerName} cannot resolve shaderKey for unknown materialId={materialId}.");
            }

            RequireLaneShaderKey(in resolved, materialId, callerName);
        }

        public static void RequireLaneShaderKey(in ResolvedMaterialAsset resolved, int materialId, string callerName)
        {
            if (!string.Equals(resolved.ShaderKey, MaterialAssetDescriptor.DefaultShaderKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{callerName} does not support material shaderKey '{resolved.ShaderKey}' (materialId={materialId}); shaderKey dispatch is only available on instanced lanes.");
            }
        }

        public static bool TryBeginAuthorBlendMode(MaterialBlendMode blendMode, string callerName)
        {
            switch (blendMode)
            {
                case MaterialBlendMode.Opaque:
                case MaterialBlendMode.Cutout:
                    return false;
                case MaterialBlendMode.AlphaBlend:
                    Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
                    return true;
                case MaterialBlendMode.Additive:
                    Rl.BeginBlendMode(BlendMode.BLEND_ADDITIVE);
                    return true;
                default:
                    throw new InvalidOperationException(
                        $"{callerName} does not recognize material blend mode '{blendMode}'.");
            }
        }
    }
}
