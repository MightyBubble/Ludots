using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Presenters
{
    public struct PresenterInstanceTransformOverride
    {
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public bool HasOverride;

        public static PresenterInstanceTransformOverride Identity => new()
        {
            LocalPosition = Vector3.Zero,
            LocalRotation = Quaternion.Identity,
            LocalScale = Vector3.One,
            HasOverride = false,
        };

        public static Quaternion RotationFromEulerDegreesXyz(Vector3 degrees)
        {
            const float degToRad = MathF.PI / 180f;
            return Quaternion.CreateFromYawPitchRoll(
                degrees.Y * degToRad,
                degrees.X * degToRad,
                degrees.Z * degToRad);
        }
    }
}
