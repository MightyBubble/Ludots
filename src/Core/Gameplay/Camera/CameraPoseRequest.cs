using System.Numerics;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class CameraPoseRequest
    {
        public Vector2? TargetCm { get; set; }
        public float? Yaw { get; set; }
        public float? Pitch { get; set; }
        public float? DistanceCm { get; set; }
        public float? FovYDeg { get; set; }
    }
}
