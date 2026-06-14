using System;
using System.Numerics;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Assets
{
    public static class PrefabTransformUtility
    {
        public static void Compose(
            in Vector3 parentPosition,
            in Quaternion parentRotation,
            in Vector3 parentScale,
            in PrefabPart part,
            out Vector3 childPosition,
            out Quaternion childRotation,
            out Vector3 childScale)
        {
            Quaternion normalizedParentRotation = WorldPlane2D.NormalizeOrIdentity(parentRotation);
            Quaternion normalizedLocalRotation = WorldPlane2D.NormalizeOrIdentity(part.LocalRotation);

            childPosition = parentPosition + Vector3.Transform(part.LocalPosition * parentScale, normalizedParentRotation);
            childRotation = WorldPlane2D.NormalizeOrIdentity(Quaternion.Concatenate(normalizedLocalRotation, normalizedParentRotation));
            childScale = parentScale * part.LocalScale;
        }

        public static int BuildChildStableId(int parentStableId, int depth, int childIndex, int meshAssetId)
        {
            int hash = HashCode.Combine(parentStableId, depth, childIndex, meshAssetId);
            if (hash == int.MinValue)
            {
                return 1;
            }

            int stableId = Math.Abs(hash);
            return stableId == 0 ? 1 : stableId;
        }
    }
}
