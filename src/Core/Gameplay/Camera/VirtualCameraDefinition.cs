using System.Numerics;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class VirtualCameraDefinition
    {
        public string Id { get; set; } = string.Empty;
        public CameraRigKind RigKind { get; set; } = CameraRigKind.Orbit;
        public VirtualCameraTargetSource TargetSource { get; set; } = VirtualCameraTargetSource.Fixed;
        public Vector2 FixedTargetCm { get; set; } = Vector2.Zero;
        public float Yaw { get; set; } = 180f;
        public float Pitch { get; set; } = 45f;
        public float DistanceCm { get; set; } = 3000f;
        public float FovYDeg { get; set; } = 60f;
        public float DefaultBlendDuration { get; set; } = 0.25f;
        public CameraBlendCurve BlendCurve { get; set; } = CameraBlendCurve.SmoothStep;
        public bool AllowUserInput { get; set; }
    }
}
