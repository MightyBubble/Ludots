using System.Numerics;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class VirtualCameraDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Priority { get; set; }
        public CameraRigKind RigKind { get; set; } = CameraRigKind.Orbit;
        public VirtualCameraTargetSource TargetSource { get; set; } = VirtualCameraTargetSource.CurrentState;
        public Vector2 FixedTargetCm { get; set; } = Vector2.Zero;
        public float Yaw { get; set; } = 180f;
        public float Pitch { get; set; } = 45f;
        public float DistanceCm { get; set; } = 3000f;
        public float FovYDeg { get; set; } = 60f;
        public float MinDistanceCm { get; set; }
        public float MaxDistanceCm { get; set; }
        public float MinPitchDeg { get; set; }
        public float MaxPitchDeg { get; set; }
        public CameraPanMode PanMode { get; set; } = CameraPanMode.Keyboard;
        public float EdgePanMarginPx { get; set; } = 15f;
        public float EdgePanSpeedCmPerSec { get; set; } = 6000f;
        public float PanCmPerSecond { get; set; } = 6000f;
        public bool EnableGrabDrag { get; set; }
        public CameraRotateMode RotateMode { get; set; } = CameraRotateMode.Both;
        public float RotateDegPerPixel { get; set; } = 0.28f;
        public float RotateDegPerSecond { get; set; } = 90f;
        public bool EnableZoom { get; set; } = true;
        public float ZoomCmPerWheel { get; set; } = 2000f;
        public CameraFollowMode FollowMode { get; set; } = CameraFollowMode.None;
        public CameraFollowTargetKind FollowTargetKind { get; set; } = CameraFollowTargetKind.None;
        public string FollowActionId { get; set; } = "CameraLock";
        public string MoveActionId { get; set; } = "Move";
        public string ZoomActionId { get; set; } = "Zoom";
        public string PointerPosActionId { get; set; } = "PointerPos";
        public string PointerDeltaActionId { get; set; } = "PointerDelta";
        public string LookActionId { get; set; } = "Look";
        public string RotateHoldActionId { get; set; } = "OrbitRotateHold";
        public string RotateLeftActionId { get; set; } = "RotateLeft";
        public string RotateRightActionId { get; set; } = "RotateRight";
        public string GrabDragHoldActionId { get; set; } = "OrbitRotateHold";
        public bool SnapToFollowTargetWhenAvailable { get; set; } = true;
        public float DefaultBlendDuration { get; set; } = 0.25f;
        public CameraBlendCurve BlendCurve { get; set; } = CameraBlendCurve.SmoothStep;
        public bool AllowUserInput { get; set; }
    }
}
