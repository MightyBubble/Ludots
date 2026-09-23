using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ludots.Platform.Abstractions
{
    public static class ProjectionSnapshotMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 WorldToScreen(in ProjectionSnapshot snapshot, in Vector3 worldPosition)
        {
            Matrix4x4 matrix = snapshot.ViewProjection;
            float clipX = (worldPosition.X * matrix.M11) + (worldPosition.Y * matrix.M21) + (worldPosition.Z * matrix.M31) + matrix.M41;
            float clipY = (worldPosition.X * matrix.M12) + (worldPosition.Y * matrix.M22) + (worldPosition.Z * matrix.M32) + matrix.M42;
            float clipW = (worldPosition.X * matrix.M14) + (worldPosition.Y * matrix.M24) + (worldPosition.Z * matrix.M34) + matrix.M44;
            if (clipW <= 0.001f)
            {
                return new Vector2(float.NaN, float.NaN);
            }

            float invW = 1f / clipW;
            float ndcX = clipX * invW;
            float ndcY = clipY * invW;
            if (ndcX < -1f || ndcX > 1f || ndcY < -1f || ndcY > 1f)
            {
                return new Vector2(float.NaN, float.NaN);
            }

            return new Vector2(
                (ndcX + 1f) * 0.5f * snapshot.Resolution.X,
                (1f - ndcY) * 0.5f * snapshot.Resolution.Y);
        }
    }
}
