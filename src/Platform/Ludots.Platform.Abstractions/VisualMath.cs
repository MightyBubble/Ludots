using System.Runtime.CompilerServices;
using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public static class VisualMath
    {
        public const float TwoPi = MathF.PI * 2f;
        private const float DegToRad = MathF.PI / 180f;
        public const float RadToDeg = 180f / MathF.PI;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RadToDegValue(float radians)
        {
            return radians * RadToDeg;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 TransformVisualLocal(Vector3 origin, Quaternion rotation, Vector3 scale, in Vector3 local)
        {
            return origin + Vector3.Transform(local * NormalizeScale(scale), NormalizeOrIdentity(rotation));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryExtractFacingRadFromVisualYRotation(Quaternion rotation, out float facingRad)
        {
            Quaternion normalized = NormalizeOrIdentity(rotation);
            Vector3 forward = Vector3.Transform(Vector3.UnitX, normalized);
            float planarLengthSq = (forward.X * forward.X) + (forward.Z * forward.Z);
            if (!float.IsFinite(planarLengthSq) || planarLengthSq <= 0.000001f)
            {
                facingRad = 0f;
                return false;
            }

            facingRad = MathF.Atan2(forward.Z, forward.X);
            return float.IsFinite(facingRad);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            float lengthSquared = value.LengthSquared();
            if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
            {
                return Quaternion.Identity;
            }

            return MathF.Abs(lengthSquared - 1f) <= 0.0001f
                ? value
                : Quaternion.Normalize(value);
        }

        public static Vector3 NormalizeScale(Vector3 value)
        {
            return value == Vector3.Zero ? Vector3.One : value;
        }

        public static Vector3 FacingRadToVisualForward(float facingRad)
        {
            return new Vector3(MathF.Cos(facingRad), 0f, MathF.Sin(facingRad));
        }

        public static Vector3 FacingRadToVisualRight(float facingRad)
        {
            return new Vector3(-MathF.Sin(facingRad), 0f, MathF.Cos(facingRad));
        }

        public static Vector3 TransformVisualLocal2D(Vector3 origin, float facingRad, in Vector3 local)
        {
            Vector3 forward = FacingRadToVisualForward(facingRad);
            Vector3 right = FacingRadToVisualRight(facingRad);
            return origin + (forward * local.X) + (Vector3.UnitY * local.Y) + (right * local.Z);
        }
    }
}
