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
