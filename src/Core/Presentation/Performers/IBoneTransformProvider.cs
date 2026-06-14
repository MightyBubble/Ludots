using System.Numerics;

namespace Ludots.Core.Presentation.Performers
{
    public interface IBoneTransformProvider
    {
        bool TryGetBoneWorldTransform(
            int stableId,
            int boneId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale);
    }
}
