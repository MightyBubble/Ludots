using System.Numerics;

namespace Ludots.Core.Presentation.Camera
{
    public readonly struct CameraRenderState3D
    {
        public Vector3 PositionCm { get; }
        public Vector3 TargetCm { get; }
        public Vector3 Up { get; }
        public float FovYDeg { get; }

        public CameraRenderState3D(Vector3 positionCm, Vector3 targetCm, Vector3 up, float fovYDeg)
        {
            PositionCm = positionCm;
            TargetCm = targetCm;
            Up = up;
            FovYDeg = fovYDeg;
        }
    }
}

