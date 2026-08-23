using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public readonly struct CameraRenderState3D
    {
        public Vector3 Position { get; }
        public Vector3 Target { get; }
        public Vector3 Up { get; }
        public float FovYDeg { get; }

        public CameraRenderState3D(Vector3 position, Vector3 target, Vector3 up, float fovYDeg)
        {
            Position = position;
            Target = target;
            Up = up;
            FovYDeg = fovYDeg;
        }

        public CameraClipPlanes ResolveClipPlanes()
        {
            float distanceMeters = System.Numerics.Vector3.Distance(Position, Target);
            float farMeters = CameraClipPlanes.DefaultFarPlaneMeters;
            if (float.IsFinite(distanceMeters) && distanceMeters > 0f)
            {
                farMeters = MathF.Max(farMeters, distanceMeters * CameraClipPlanes.FarPlaneDistanceMultiplier);
            }

            return new CameraClipPlanes(CameraClipPlanes.DefaultNearPlaneMeters, farMeters);
        }
    }
}

