using System.Numerics;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Presenters
{
    public static class AssetBindingVisualScale
    {
        public static Vector3 Resolve(in AssetBindingConfig asset, Vector3 presenterWorldScale, float scaleParamMultiplier)
        {
            if (asset.AssetKind == AssetKind.Decal)
            {
                Vector3 decalScale = presenterWorldScale * asset.LocalScale * scaleParamMultiplier;
                _ = ProjectedDecalVolume.FromVisualScale(decalScale);
                return decalScale;
            }

            Vector3 resolved = presenterWorldScale == Vector3.Zero ? Vector3.One : presenterWorldScale;
            resolved *= asset.LocalScale == Vector3.Zero ? Vector3.One : asset.LocalScale;
            return resolved * scaleParamMultiplier;
        }
    }
}
