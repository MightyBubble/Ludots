using System.Numerics;
using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Optional presentation-only culling focus override.
    /// It changes visibility calculation without moving the real gameplay/render camera.
    /// </summary>
    public sealed class CameraCullingFocusOverride
    {
        public bool Enabled { get; set; }
        public string SourceId { get; set; } = string.Empty;
        public Vector2 TargetCm { get; set; }
        public float DistanceCm { get; set; }
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public float FovYDeg { get; set; }

        public CameraStateSnapshot Apply(in CameraStateSnapshot cameraState)
        {
            if (!Enabled)
            {
                return cameraState;
            }

            CameraStateSnapshot overridden = cameraState;
            overridden.TargetCm = TargetCm;
            overridden.DistanceCm = DistanceCm;
            overridden.Yaw = Yaw;
            overridden.Pitch = Pitch;
            overridden.FovYDeg = FovYDeg;
            overridden.IsFollowing = false;
            return overridden;
        }
    }
}
