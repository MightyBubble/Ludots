using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Loads virtual camera definitions from ConfigPipeline (Camera/virtual_cameras.json)
    /// into VirtualCameraRegistry.
    /// </summary>
    public sealed class VirtualCameraDefinitionLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly VirtualCameraRegistry _registry;
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public VirtualCameraDefinitionLoader(ConfigPipeline pipeline, VirtualCameraRegistry registry)
        {
            _pipeline = pipeline ?? throw new System.ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Camera/virtual_cameras.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            if (merged == null || merged.Count == 0)
            {
                return;
            }

            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                if (node == null)
                {
                    continue;
                }

                try
                {
                    var config = JsonSerializer.Deserialize<VirtualCameraDefinitionConfig>(node.ToJsonString(), _options);
                    if (config == null || string.IsNullOrWhiteSpace(config.Id))
                    {
                        continue;
                    }

                    _registry.Register(new VirtualCameraDefinition
                    {
                        Id = config.Id,
                        DisplayName = string.IsNullOrWhiteSpace(config.DisplayName) ? config.Id : config.DisplayName,
                        Priority = config.Priority,
                        RigKind = config.RigKind,
                        TargetSource = config.TargetSource,
                        FixedTargetCm = config.FixedTargetCm == null
                            ? Vector2.Zero
                            : new Vector2(config.FixedTargetCm.X, config.FixedTargetCm.Y),
                        Yaw = config.Yaw,
                        Pitch = config.Pitch,
                        DistanceCm = config.DistanceCm,
                        FovYDeg = config.FovYDeg,
                        MinDistanceCm = config.MinDistanceCm,
                        MaxDistanceCm = config.MaxDistanceCm,
                        MinPitchDeg = config.MinPitchDeg,
                        MaxPitchDeg = config.MaxPitchDeg,
                        PanMode = config.PanMode,
                        EdgePanMarginPx = config.EdgePanMarginPx,
                        EdgePanSpeedCmPerSec = config.EdgePanSpeedCmPerSec,
                        PanCmPerSecond = config.PanCmPerSecond,
                        EnableGrabDrag = config.EnableGrabDrag,
                        RotateMode = config.RotateMode,
                        RotateDegPerPixel = config.RotateDegPerPixel,
                        RotateDegPerSecond = config.RotateDegPerSecond,
                        EnableZoom = config.EnableZoom,
                        ZoomCmPerWheel = config.ZoomCmPerWheel,
                        FollowMode = config.FollowMode,
                        FollowActionId = config.FollowActionId,
                        MoveActionId = config.MoveActionId,
                        ZoomActionId = config.ZoomActionId,
                        PointerPosActionId = config.PointerPosActionId,
                        PointerDeltaActionId = config.PointerDeltaActionId,
                        LookActionId = config.LookActionId,
                        RotateHoldActionId = config.RotateHoldActionId,
                        RotateLeftActionId = config.RotateLeftActionId,
                        RotateRightActionId = config.RotateRightActionId,
                        GrabDragHoldActionId = config.GrabDragHoldActionId,
                        SnapToFollowTargetWhenAvailable = config.SnapToFollowTargetWhenAvailable,
                        DefaultBlendDuration = config.DefaultBlendDuration,
                        BlendCurve = config.BlendCurve,
                        AllowUserInput = config.AllowUserInput
                    });
                }
                catch (System.Exception)
                {
                    // Skip invalid entries
                }
            }
        }

        private sealed class VirtualCameraDefinitionConfig
        {
            public string Id { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public int Priority { get; set; }
            public CameraRigKind RigKind { get; set; } = CameraRigKind.Orbit;
            public VirtualCameraTargetSource TargetSource { get; set; } = VirtualCameraTargetSource.CurrentState;
            public Vector2Config? FixedTargetCm { get; set; }
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

        private sealed class Vector2Config
        {
            public float X { get; set; }
            public float Y { get; set; }
        }
    }
}
