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
        private readonly JsonSerializerOptions _options = CreateOptions();

        public VirtualCameraDefinitionLoader(ConfigPipeline pipeline, VirtualCameraRegistry registry)
        {
            _pipeline = pipeline ?? throw new System.ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Camera/virtual_cameras.json", ConfigMergePolicy.ArrayById, "id");
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
                        throw new System.InvalidOperationException("VirtualCameraDefinition.id is required.");
                    }

                    var panMode = ResolvePanMode(config);
                    var rotateMode = ResolveRotateMode(config);
                    var enableZoom = ResolveEnableZoom(config);

                    ValidateConfig(config, panMode, rotateMode, enableZoom);

                    _registry.Register(new VirtualCameraDefinition
                    {
                        Id = config.Id,
                        DisplayName = string.IsNullOrWhiteSpace(config.DisplayName) ? config.Id : config.DisplayName,
                        Priority = config.Priority,
                        ControlMode = config.ControlMode,
                        PlatformDriverId = config.PlatformDriverId,
                        RigKind = config.RigKind,
                        TargetSource = config.TargetSource,
                        FixedTargetCm = config.FixedTargetCm == null
                            ? Vector2.Zero
                            : new Vector2(config.FixedTargetCm.X, config.FixedTargetCm.Y),
                        TargetHeightMode = config.TargetHeightMode,
                        TargetHeightLayerIndex = config.TargetHeightLayerIndex,
                        TargetHeightOffsetCm = config.TargetHeightOffsetCm,
                        Yaw = config.Yaw,
                        Pitch = config.Pitch,
                        DistanceCm = config.DistanceCm,
                        FovYDeg = config.FovYDeg,
                        RigPivotOffsetCm = config.RigPivotOffsetCm == null
                            ? Vector3.Zero
                            : new Vector3(config.RigPivotOffsetCm.X, config.RigPivotOffsetCm.Y, config.RigPivotOffsetCm.Z),
                        RigCameraOffsetCm = config.RigCameraOffsetCm == null
                            ? Vector3.Zero
                            : new Vector3(config.RigCameraOffsetCm.X, config.RigCameraOffsetCm.Y, config.RigCameraOffsetCm.Z),
                        MinDistanceCm = config.MinDistanceCm ?? 0f,
                        MaxDistanceCm = config.MaxDistanceCm ?? 0f,
                        MinPitchDeg = config.MinPitchDeg,
                        MaxPitchDeg = config.MaxPitchDeg,
                        PanMode = panMode,
                        EdgePanMarginPx = config.EdgePanMarginPx,
                        EdgePanSpeedCmPerSec = config.EdgePanSpeedCmPerSec,
                        EdgePanRequiresPointerInsideViewport = config.EdgePanRequiresPointerInsideViewport,
                        PanCmPerSecond = config.PanCmPerSecond,
                        EnableGrabDrag = config.EnableGrabDrag,
                        ConfineTargetToWorldBounds = config.ConfineTargetToWorldBounds,
                        ConfinePaddingCm = config.ConfinePaddingCm,
                        RotateMode = rotateMode,
                        RotateDegPerPixel = config.RotateDegPerPixel,
                        RotateRequiresHold = config.RotateRequiresHold ?? true,
                        RotateDegPerSecond = config.RotateDegPerSecond,
                        EnableZoom = enableZoom,
                        ZoomCmPerWheel = config.ZoomCmPerWheel,
                        ZoomFactorPerWheel = config.ZoomFactorPerWheel,
                        FollowMode = config.FollowMode,
                        FollowTargetKind = config.FollowTargetKind,
                        FollowCollectionKey = config.FollowCollectionKey,
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
                catch (System.Exception ex)
                {
                    throw new System.InvalidOperationException(
                        $"Failed to load virtual camera definition from '{entry.RelativePath}' entry {i}.",
                        ex);
                }
            }
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = StrictJsonOptions.CreateCamelCase();
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            return options;
        }

        private static CameraPanMode ResolvePanMode(VirtualCameraDefinitionConfig config)
        {
            return config.PanMode ?? CameraPanMode.None;
        }

        private static CameraRotateMode ResolveRotateMode(VirtualCameraDefinitionConfig config)
        {
            return config.RotateMode ?? CameraRotateMode.None;
        }

        private static bool ResolveEnableZoom(VirtualCameraDefinitionConfig config)
        {
            return config.EnableZoom ?? false;
        }

        private static void ValidateConfig(
            VirtualCameraDefinitionConfig config,
            CameraPanMode panMode,
            CameraRotateMode rotateMode,
            bool enableZoom)
        {
            if (config.TargetHeightLayerIndex < 0)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' targetHeightLayerIndex must be >= 0.");
            }

            ValidateFinite(config.Id, nameof(config.Yaw), config.Yaw);
            ValidateFinite(config.Id, nameof(config.Pitch), config.Pitch);
            ValidateDistance(config);
            ValidateFov(config);
            ValidateOptionalVector3(config.Id, nameof(config.RigPivotOffsetCm), config.RigPivotOffsetCm);
            ValidateOptionalVector3(config.Id, nameof(config.RigCameraOffsetCm), config.RigCameraOffsetCm);
            ValidateFinite(config.Id, nameof(config.DefaultBlendDuration), config.DefaultBlendDuration);
            if (config.DefaultBlendDuration < 0f)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' defaultBlendDuration must be >= 0.");
            }

            if (!float.IsFinite(config.TargetHeightOffsetCm))
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' targetHeightOffsetCm must be finite.");
            }

            ValidateTarget(config);
            ValidatePlatformDriver(config);
            ValidateInputBehavior(config, panMode, rotateMode, enableZoom);
            ValidateDefinedEnum(config.Id, nameof(config.ControlMode), config.ControlMode);
            ValidateDefinedEnum(config.Id, nameof(config.RigKind), config.RigKind);
            ValidateDefinedEnum(config.Id, nameof(config.TargetSource), config.TargetSource);
            ValidateDefinedEnum(config.Id, nameof(config.TargetHeightMode), config.TargetHeightMode);
            ValidateDefinedEnum(config.Id, nameof(config.FollowMode), config.FollowMode);
            ValidateDefinedEnum(config.Id, nameof(config.FollowTargetKind), config.FollowTargetKind);
            ValidateDefinedEnum(config.Id, nameof(config.BlendCurve), config.BlendCurve);
        }

        private static void ValidateDistance(VirtualCameraDefinitionConfig config)
        {
            ValidateFinite(config.Id, nameof(config.DistanceCm), config.DistanceCm);
            if (config.RigKind == CameraRigKind.FirstPerson)
            {
                if (config.DistanceCm < 0f)
                {
                    throw new System.InvalidOperationException(
                        $"Virtual camera '{config.Id}' distanceCm must be >= 0 for first-person rigs.");
                }

                return;
            }

            if (config.DistanceCm <= 0f)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' distanceCm must be > 0.");
            }
        }

        private static void ValidateFov(VirtualCameraDefinitionConfig config)
        {
            ValidateFinite(config.Id, nameof(config.FovYDeg), config.FovYDeg);
            if (config.FovYDeg <= 0f || config.FovYDeg >= 179f)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' fovYDeg must be > 0 and < 179.");
            }
        }

        private static void ValidateTarget(VirtualCameraDefinitionConfig config)
        {
            if (config.TargetSource != VirtualCameraTargetSource.Fixed)
            {
                return;
            }

            if (config.FixedTargetCm == null)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' uses targetSource Fixed and must declare fixedTargetCm.");
            }

            ValidateFinite(config.Id, "fixedTargetCm.x", config.FixedTargetCm.X);
            ValidateFinite(config.Id, "fixedTargetCm.y", config.FixedTargetCm.Y);
        }

        private static void ValidatePlatformDriver(VirtualCameraDefinitionConfig config)
        {
            if (config.ControlMode != VirtualCameraControlMode.PlatformManaged)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(config.PlatformDriverId))
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' uses PlatformManaged controlMode and must declare platformDriverId.");
            }
        }

        private static void ValidateInputBehavior(
            VirtualCameraDefinitionConfig config,
            CameraPanMode panMode,
            CameraRotateMode rotateMode,
            bool enableZoom)
        {
            ValidateDefinedEnum(config.Id, nameof(config.PanMode), panMode);
            ValidateDefinedEnum(config.Id, nameof(config.RotateMode), rotateMode);

            if (!config.AllowUserInput)
            {
                if (config.EnableZoom == true)
                {
                    throw new System.InvalidOperationException(
                        $"Virtual camera '{config.Id}' disables user input and must not enable zoom behavior.");
                }

                if (config.PanMode.HasValue && panMode != CameraPanMode.None)
                {
                    throw new System.InvalidOperationException(
                        $"Virtual camera '{config.Id}' disables user input and must not enable pan behavior.");
                }

                if (config.RotateMode.HasValue && rotateMode != CameraRotateMode.None)
                {
                    throw new System.InvalidOperationException(
                        $"Virtual camera '{config.Id}' disables user input and must not enable rotate behavior.");
                }

                if (config.EnableGrabDrag)
                {
                    throw new System.InvalidOperationException(
                        $"Virtual camera '{config.Id}' disables user input and must not enable grab-drag behavior.");
                }

                return;
            }

            if (!config.EnableZoom.HasValue)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' allows user input and must explicitly declare enableZoom.");
            }

            if (!config.PanMode.HasValue)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' allows user input and must explicitly declare panMode.");
            }

            if (!config.RotateMode.HasValue)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' allows user input and must explicitly declare rotateMode.");
            }

            ValidatePanBehavior(config, panMode);
            ValidateRotateBehavior(config, rotateMode);
            ValidateZoomBehavior(config, enableZoom);
        }

        private static void ValidatePanBehavior(VirtualCameraDefinitionConfig config, CameraPanMode panMode)
        {
            if (panMode is CameraPanMode.Keyboard or CameraPanMode.KeyboardAndEdge)
            {
                ValidateFinitePositive(config.Id, nameof(config.PanCmPerSecond), config.PanCmPerSecond);
            }

            if (panMode is CameraPanMode.EdgePan or CameraPanMode.KeyboardAndEdge)
            {
                ValidateFinite(config.Id, nameof(config.EdgePanMarginPx), config.EdgePanMarginPx);
                if (config.EdgePanMarginPx <= 0f)
                {
                    throw new System.InvalidOperationException(
                        $"Virtual camera '{config.Id}' edgePanMarginPx must be > 0.");
                }

                ValidateFinitePositive(config.Id, nameof(config.EdgePanSpeedCmPerSec), config.EdgePanSpeedCmPerSec);
            }
        }

        private static void ValidateRotateBehavior(VirtualCameraDefinitionConfig config, CameraRotateMode rotateMode)
        {
            if (rotateMode is CameraRotateMode.DragRotate or CameraRotateMode.Both)
            {
                if (!config.RotateRequiresHold.HasValue)
                {
                    throw new System.InvalidOperationException(
                        $"Virtual camera '{config.Id}' enables drag rotate and must declare rotateRequiresHold.");
                }

                ValidateFinitePositive(config.Id, nameof(config.RotateDegPerPixel), config.RotateDegPerPixel);
                ValidatePitchBounds(config);
            }
            else if (config.RotateRequiresHold.HasValue)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' declares rotateRequiresHold without drag rotate.");
            }

            if (rotateMode is CameraRotateMode.KeyRotate or CameraRotateMode.Both)
            {
                ValidateFinitePositive(config.Id, nameof(config.RotateDegPerSecond), config.RotateDegPerSecond);
            }
        }

        private static void ValidatePitchBounds(VirtualCameraDefinitionConfig config)
        {
            ValidateFinite(config.Id, nameof(config.MinPitchDeg), config.MinPitchDeg);
            ValidateFinite(config.Id, nameof(config.MaxPitchDeg), config.MaxPitchDeg);
            if (config.MaxPitchDeg < config.MinPitchDeg)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' maxPitchDeg must be >= minPitchDeg.");
            }
        }

        private static void ValidateZoomBehavior(VirtualCameraDefinitionConfig config, bool enableZoom)
        {
            if (!enableZoom)
            {
                return;
            }

            if (!config.MinDistanceCm.HasValue || !config.MaxDistanceCm.HasValue)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' enables zoom and must declare minDistanceCm and maxDistanceCm.");
            }

            ValidateFinite(config.Id, nameof(config.MinDistanceCm), config.MinDistanceCm.Value);
            ValidateFinite(config.Id, nameof(config.MaxDistanceCm), config.MaxDistanceCm.Value);
            if (config.MinDistanceCm.Value < 0f || config.MaxDistanceCm.Value <= 0f)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' zoom distance bounds must be finite and non-negative, with maxDistanceCm > 0.");
            }

            if (config.MaxDistanceCm.Value < config.MinDistanceCm.Value)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{config.Id}' maxDistanceCm must be >= minDistanceCm.");
            }

            ValidateFinitePositive(config.Id, nameof(config.ZoomCmPerWheel), config.ZoomCmPerWheel);
        }

        private static void ValidateFinite(string cameraId, string propertyName, float value)
        {
            if (!float.IsFinite(value))
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{cameraId}' {propertyName} must be finite.");
            }
        }

        private static void ValidateFinitePositive(string cameraId, string propertyName, float value)
        {
            ValidateFinite(cameraId, propertyName, value);
            if (value <= 0f)
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{cameraId}' {propertyName} must be > 0.");
            }
        }

        private static void ValidateOptionalVector3(string cameraId, string propertyName, Vector3Config? value)
        {
            if (value == null)
            {
                return;
            }

            ValidateFinite(cameraId, $"{propertyName}.x", value.X);
            ValidateFinite(cameraId, $"{propertyName}.y", value.Y);
            ValidateFinite(cameraId, $"{propertyName}.z", value.Z);
        }

        private static void ValidateDefinedEnum<TEnum>(string cameraId, string propertyName, TEnum value)
            where TEnum : struct, System.Enum
        {
            if (!System.Enum.IsDefined(value))
            {
                throw new System.InvalidOperationException(
                    $"Virtual camera '{cameraId}' {propertyName} declares unsupported value '{value}'.");
            }
        }

        private sealed class VirtualCameraDefinitionConfig
        {
            public string Id { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public int Priority { get; set; }
            public VirtualCameraControlMode ControlMode { get; set; } = VirtualCameraControlMode.BuiltIn;
            public string PlatformDriverId { get; set; } = string.Empty;
            public CameraRigKind RigKind { get; set; } = CameraRigKind.Orbit;
            public VirtualCameraTargetSource TargetSource { get; set; } = VirtualCameraTargetSource.CurrentState;
            public Vector2Config? FixedTargetCm { get; set; }
            public VirtualCameraTargetHeightMode TargetHeightMode { get; set; } = VirtualCameraTargetHeightMode.Flat;
            public int TargetHeightLayerIndex { get; set; }
            public float TargetHeightOffsetCm { get; set; }
            public float Yaw { get; set; } = 180f;
            public float Pitch { get; set; } = 45f;
            public float DistanceCm { get; set; } = 3000f;
            public float FovYDeg { get; set; } = 60f;
            public Vector3Config? RigPivotOffsetCm { get; set; }
            public Vector3Config? RigCameraOffsetCm { get; set; }
            public float? MinDistanceCm { get; set; }
            public float? MaxDistanceCm { get; set; }
            public float MinPitchDeg { get; set; }
            public float MaxPitchDeg { get; set; }
            public CameraPanMode? PanMode { get; set; }
            public float EdgePanMarginPx { get; set; } = 15f;
            public float EdgePanSpeedCmPerSec { get; set; } = 6000f;
            public bool EdgePanRequiresPointerInsideViewport { get; set; } = true;
            public float PanCmPerSecond { get; set; } = 6000f;
            public bool EnableGrabDrag { get; set; }
            public bool ConfineTargetToWorldBounds { get; set; }
            public float ConfinePaddingCm { get; set; }
            public CameraRotateMode? RotateMode { get; set; }
            public float RotateDegPerPixel { get; set; } = 0.28f;
            public bool? RotateRequiresHold { get; set; }
            public float RotateDegPerSecond { get; set; } = 90f;
            public bool? EnableZoom { get; set; }
            public float ZoomCmPerWheel { get; set; } = 2000f;
            public float ZoomFactorPerWheel { get; set; }
            public CameraFollowMode FollowMode { get; set; } = CameraFollowMode.None;
            public CameraFollowTargetKind FollowTargetKind { get; set; } = CameraFollowTargetKind.None;
            public string FollowCollectionKey { get; set; } = string.Empty;
            public string FollowActionId { get; set; } = "CameraLock";
            public string MoveActionId { get; set; } = "Move";
            public string ZoomActionId { get; set; } = VirtualCameraDefinition.DefaultZoomActionId;
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

        private sealed class Vector3Config
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
        }
    }
}
