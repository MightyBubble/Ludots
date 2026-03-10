using System.Text.Json.Serialization;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Camera preset for reuse across maps. Loaded from ConfigPipeline (Camera/presets.json).
    /// Defines both camera state values and controller behavior configuration.
    /// Mods can extend or override via assets/Configs/Camera/presets.json.
    /// </summary>
    public sealed class CameraPreset
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }

        // --- State values ---
        public float DistanceCm { get; set; }
        public float Pitch { get; set; }
        public float FovYDeg { get; set; } = 60f;
        public float Yaw { get; set; } = 180f;
        public float MinDistanceCm { get; set; }
        public float MaxDistanceCm { get; set; }
        public float MinPitchDeg { get; set; }
        public float MaxPitchDeg { get; set; }

        // --- Controller behavior ---
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CameraPanMode PanMode { get; set; } = CameraPanMode.Keyboard;
        public float EdgePanMarginPx { get; set; } = 15f;
        public float EdgePanSpeedCmPerSec { get; set; } = 6000f;
        public float PanCmPerSecond { get; set; } = 6000f;

        public bool EnableGrabDrag { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CameraRotateMode RotateMode { get; set; } = CameraRotateMode.Both;
        public float RotateDegPerPixel { get; set; } = 0.28f;
        public float RotateDegPerSecond { get; set; } = 90f;

        public bool EnableZoom { get; set; } = true;
        public float ZoomCmPerWheel { get; set; } = 2000f;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CameraFollowMode FollowMode { get; set; } = CameraFollowMode.None;
        public string FollowActionId { get; set; } = "CameraLock";

        // --- Input Action IDs (override defaults if needed) ---
        public string MoveActionId { get; set; } = "Move";
        public string ZoomActionId { get; set; } = "Zoom";
        public string PointerPosActionId { get; set; } = "PointerPos";
        public string PointerDeltaActionId { get; set; } = "PointerDelta";
        public string LookActionId { get; set; } = "Look";
        public string RotateHoldActionId { get; set; } = "OrbitRotateHold";
        public string RotateLeftActionId { get; set; } = "RotateLeft";
        public string RotateRightActionId { get; set; } = "RotateRight";
        public string GrabDragHoldActionId { get; set; } = "OrbitRotateHold";
    }
}
