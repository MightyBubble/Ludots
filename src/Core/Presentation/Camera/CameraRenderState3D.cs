using System.Numerics;

namespace Ludots.Core.Presentation.Camera
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
    }
}

