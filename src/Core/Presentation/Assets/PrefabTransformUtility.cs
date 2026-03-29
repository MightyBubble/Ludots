using System;
using System.Numerics;

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
            Quaternion normalizedParentRotation = NormalizeOrIdentity(parentRotation);
            Quaternion normalizedLocalRotation = NormalizeOrIdentity(part.LocalRotation);

            childPosition = parentPosition + Vector3.Transform(part.LocalPosition * parentScale, normalizedParentRotation);
            childRotation = Quaternion.Normalize(Quaternion.Concatenate(normalizedLocalRotation, normalizedParentRotation));
            childScale = parentScale * part.LocalScale;
        }

        public static Quaternion NormalizeOrIdentity(in Quaternion rotation)
        {
            return rotation.LengthSquared() > 0.000001f
                ? Quaternion.Normalize(rotation)
                : Quaternion.Identity;
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
