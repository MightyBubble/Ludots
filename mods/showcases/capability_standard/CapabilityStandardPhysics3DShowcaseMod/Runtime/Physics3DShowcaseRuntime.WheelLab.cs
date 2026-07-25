using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Vehicle3D;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    internal const string WheelThrottleAction = "Physics3D.VehicleThrottle";
    internal const string WheelSteeringAction = "Physics3D.VehicleSteering";
    internal const string WheelBrakeAction = "Physics3D.VehicleBrake";
    internal const string WheelNextModeAction = "Physics3D.VehicleNextMode";
    internal const string WheelResetAction = "Physics3D.VehicleReset";

    private const int WheelLabWheelCount = 4;
    private const int WheelLabMaximumModeBodies = WheelLabWheelCount;
    private const uint WheelLabGroundCategory = 1u << 20;
    private const uint WheelLabVehicleCategory = 1u << 21;
    private const uint WheelLabAssemblyId = 7301;

    private static readonly LayerMask WheelLabGroundCollisionLayer = new(
        WheelLabGroundCategory,
        WheelLabVehicleCategory);
    private static readonly LayerMask WheelLabVehicleCollisionLayer = new(
        WheelLabVehicleCategory,
        WheelLabGroundCategory);
    private static readonly LayerMask WheelLabGroundQueryLayer = new(
        WheelLabGroundCategory,
        WheelLabGroundCategory);
    private static readonly Vector4 WheelLabRoadColor = new(0.22f, 0.25f, 0.28f, 1f);
    private static readonly Vector4 WheelLabBumpColor = new(0.95f, 0.68f, 0.18f, 1f);
    private static readonly Vector4 WheelLabPitColor = new(0.46f, 0.30f, 0.24f, 1f);
    private static readonly Vector4 WheelLabBankColor = new(0.24f, 0.58f, 0.76f, 1f);
    private static readonly Vector4 WheelLabPlatformColor = new(0.68f, 0.38f, 0.96f, 1f);
    private static readonly Vector4 WheelLabRampColor = new(0.94f, 0.34f, 0.25f, 1f);
    private static readonly Vector4 WheelLabBrakeColor = new(0.22f, 0.72f, 0.42f, 1f);
    private static readonly Vector4 WheelLabChassisColor = new(0.12f, 0.62f, 0.92f, 1f);
    private static readonly Vector4 WheelLabPhysicalWheelColor = new(0.08f, 0.09f, 0.11f, 1f);
    private static readonly Vector4 WheelLabBoxWheelColor = new(0.90f, 0.50f, 0.12f, 1f);
    private static readonly Vector4 WheelLabCarrierColor = new(0.30f, 0.34f, 0.40f, 0.35f);

    private readonly Physics3DShapeId[] _wheelLabRoadShapes = new Physics3DShapeId[12];
    private readonly Physics3DBodyId[] _wheelLabModeBodies = new Physics3DBodyId[WheelLabMaximumModeBodies];
    private readonly Vehicle3DWheelId[] _wheelLabWheelIds = new Vehicle3DWheelId[WheelLabWheelCount];
    private readonly Vehicle3DWheelState[] _wheelLabWheelStates = new Vehicle3DWheelState[WheelLabWheelCount];
    private Physics3DWheelLabTrialResult[] _wheelLabTrialResults = Array.Empty<Physics3DWheelLabTrialResult>();

    private Physics3DShapeId _wheelLabBumpShape;
    private Physics3DShapeId _wheelLabMovingPlatformShape;
    private Physics3DShapeId _wheelLabStopWallShape;
    private Physics3DShapeId _wheelLabChassisShape;
    private Physics3DShapeId _wheelLabPhysicalWheelShape;
    private Physics3DShapeId _wheelLabBoxWheelShape;
    private Vehicle3DWorld? _wheelLabVehicles;
    private Vehicle3DVehicleId _wheelLabVehicle;
    private Physics3DBodyId _wheelLabChassis;
    private Vehicle3DWheelKind _wheelLabMode;
    private WheelLabCourseSection _wheelLabSection;
    private int _wheelLabModeBodyCount;
    private int _wheelLabOwnedBodyStartIndex = -1;
    private int _wheelLabChassisBodyIndex = -1;
    private int _wheelLabMovingPlatformBodyIndex = -1;
    private int _wheelLabGroundedWheelCount;
    private float _wheelLabThrottle;
    private float _wheelLabSteering;
    private float _wheelLabBrake;
    private float _wheelLabSpeedKph;
    private float _wheelLabAverageCompressionCm;
    private float _wheelLabMaximumSlipCmPerSecond;
    private Physics3DWheelLabTrialStatus _wheelLabTrialStatus;
    private Physics3DBodyState _wheelLabTrialStartState;
    private Physics3DBodyState _wheelLabTrialPlatformStartState;
    private int _wheelLabTrialTick;
    private long _wheelLabGroundedSamples;
    private float _wheelLabTrialMaximumCompressionCm;
    private float _wheelLabTrialMaximumSlipCmPerSecond;
    private bool _wheelLabTrialBrakeMeasured;
    private Vector3 _wheelLabTrialBrakePreviousPositionCm;
    private float _wheelLabTrialBrakingDistanceCm;
    private bool _wheelLabNextModeRequested;
    private bool _wheelLabResetRequested;
    private bool _wheelLabHasObservedStep;
    private bool _wheelLabCameraActive;

    internal Vehicle3DWheelKind WheelLabMode => _wheelLabMode;
    internal int WheelLabGroundedWheelCount => _wheelLabGroundedWheelCount;
    internal float WheelLabSpeedKph => _wheelLabSpeedKph;
    internal float WheelLabAverageCompressionCm => _wheelLabAverageCompressionCm;
    internal float WheelLabMaximumSlipCmPerSecond => _wheelLabMaximumSlipCmPerSecond;
    internal int WheelLabVehicleCount => _wheelLabVehicles?.ActiveVehicleCount ?? 0;
    internal int WheelLabWheelCountValue => _wheelLabVehicles?.ActiveWheelCount ?? 0;
    internal int WheelLabModeBodyCount => _wheelLabModeBodyCount;
    internal Physics3DBodyId WheelLabChassisBody => _wheelLabChassis;
    internal WheelLabCourseSection WheelLabSection => _wheelLabSection;
    internal Physics3DWheelLabTrialStatus WheelLabTrialStatus => _wheelLabTrialStatus;
    internal Physics3DBodyState WheelLabTrialStartState => _wheelLabTrialStartState;
    internal Physics3DBodyState WheelLabTrialPlatformStartState => _wheelLabTrialPlatformStartState;

    internal Physics3DBodyState GetWheelLabChassisState()
    {
        if (_scene != Physics3DShowcaseScene.WheelLab)
        {
            throw new InvalidOperationException("Wheel Lab chassis state is only available while Wheel Lab is active.");
        }

        return RequirePhysics3DChassisState();
    }

    internal void GetWheelLabMovingPlatformMotion(
        out Physics3DBodyState bodyState,
        out Physics3DPoseCm ecsPose)
    {
        if (_scene != Physics3DShowcaseScene.WheelLab || _wheelLabMovingPlatformBodyIndex < 0)
        {
            throw new InvalidOperationException("Wheel Lab platform motion is only available while Wheel Lab is active.");
        }

        bodyState = RequirePhysicsWorld().GetBodyState(_bodyIds[_wheelLabMovingPlatformBodyIndex]);
        ecsPose = RequireEcsWorld().Get<Physics3DPoseCm>(_bodyEntities[_wheelLabMovingPlatformBodyIndex]);
    }

    internal void SetWheelLabInputForTests(in Vehicle3DInput input)
    {
        if (_scene != Physics3DShowcaseScene.WheelLab)
        {
            throw new InvalidOperationException("Wheel Lab test input is only available while Wheel Lab is active.");
        }

        _wheelLabThrottle = input.Throttle;
        _wheelLabBrake = input.Brake;
        _wheelLabSteering = input.Steering;
    }

    private void RegisterWheelLabShapes(Physics3DWheelLabShowcaseConfig config)
    {
        IPhysics3DWorld world = RequirePhysicsWorld();
        float roadWidth = config.RoadWidthCm;
        float thickness = config.RoadThicknessCm;
        _wheelLabRoadShapes[0] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.PotholeStartZCm - config.RoadStartZCm));
        float potholeSlopeLength = MathF.Sqrt(
            (config.PotholeTransitionLengthCm * config.PotholeTransitionLengthCm) +
            (config.PotholeDepthCm * config.PotholeDepthCm));
        _wheelLabRoadShapes[1] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, potholeSlopeLength));
        _wheelLabRoadShapes[2] = world.RegisterBoxShape(new Vector3(
            roadWidth,
            thickness,
            config.PotholeEndZCm - config.PotholeStartZCm - (config.PotholeTransitionLengthCm * 2f)));
        _wheelLabRoadShapes[3] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, potholeSlopeLength));
        _wheelLabRoadShapes[4] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.BankStartZCm - config.PotholeEndZCm));
        _wheelLabRoadShapes[5] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.BankEndZCm - config.BankStartZCm));
        _wheelLabRoadShapes[6] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.PlatformGapStartZCm - config.BankEndZCm));
        _wheelLabRoadShapes[7] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.RampStartZCm - config.PlatformGapEndZCm));
        _wheelLabRoadShapes[8] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.RampEndZCm - config.RampStartZCm));
        _wheelLabRoadShapes[9] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.BrakeStartZCm - config.RampEndZCm));
        _wheelLabRoadShapes[10] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.BrakeEndZCm - config.BrakeStartZCm));
        _wheelLabRoadShapes[11] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.RoadEndZCm - config.BrakeEndZCm));
        _wheelLabBumpShape = world.RegisterBoxShape(new Vector3(config.BumpWidthCm, config.BumpHeightCm, config.BumpDepthCm));
        _wheelLabMovingPlatformShape = world.RegisterBoxShape(new Vector3(
            config.MovingPlatformWidthCm,
            config.MovingPlatformThicknessCm,
            config.PlatformGapEndZCm - config.PlatformGapStartZCm));
        _wheelLabStopWallShape = world.RegisterBoxShape(new Vector3(
            config.RoadWidthCm,
            config.StopWallHeightCm,
            config.StopWallThicknessCm));
        _wheelLabChassisShape = world.RegisterBoxShape(new Vector3(
            config.ChassisWidthCm,
            config.ChassisHeightCm,
            config.ChassisLengthCm));
        _wheelLabPhysicalWheelShape = world.RegisterCylinderShape(config.WheelRadiusCm, config.WheelWidthCm);
        float boxWheelSideCm = WheelLabBoxWheelSideCm(config);
        _wheelLabBoxWheelShape = world.RegisterBoxShape(new Vector3(
            config.WheelWidthCm,
            boxWheelSideCm,
            boxWheelSideCm));
    }

    private void BuildWheelLabScene()
    {
        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        _wheelLabOwnedBodyStartIndex = _bodyCount;
        BuildWheelLabCourse(config);
        CreateWheelLabChassis(config);

        _wheelLabMode = config.InitialWheelKind;
        CreateWheelLabVehicle(config, _wheelLabMode, RequirePhysicsWorld().GetBodyState(_wheelLabChassis));
        InitializeWheelLabComparison(config);
        ResetWheelLabTrialTracking();
        ResetWheelLabMovingPlatform();
        _wheelLabSection = WheelLabCourseSection.Start;
        _wheelLabHasObservedStep = false;
        _wheelLabGroundedWheelCount = 0;
        _wheelLabSpeedKph = 0f;
        _wheelLabAverageCompressionCm = 0f;
        _wheelLabMaximumSlipCmPerSecond = 0f;
        ActivateWheelLabCamera();
    }

    private void CreateWheelLabChassis(Physics3DWheelLabShowcaseConfig config)
    {
        _wheelLabChassisBodyIndex = _bodyCount;
        _wheelLabChassis = AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            _wheelLabChassisShape,
            Physics3DShapeKind.Box,
            new Vector3(config.ChassisWidthCm, config.ChassisHeightCm, config.ChassisLengthCm),
            0f,
            WheelLabSpawnPosition(config),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            WheelLabChassisColor,
            config.ChassisMass,
            collisionLayer: WheelLabVehicleCollisionLayer,
            collisionSubgroup: CreateWheelLabCollisionSubgroup());
    }

    private void InitializeWheelLabComparison(Physics3DWheelLabShowcaseConfig config)
    {
        _wheelLabTrialResults = new Physics3DWheelLabTrialResult[config.ComparisonResultCapacity];
        StoreWheelLabTrialResult(Physics3DWheelLabTrialResult.NotRun(Vehicle3DWheelKind.Physical));
        StoreWheelLabTrialResult(Physics3DWheelLabTrialResult.NotRun(Vehicle3DWheelKind.Box));
        StoreWheelLabTrialResult(Physics3DWheelLabTrialResult.NotRun(Vehicle3DWheelKind.Scanning));
    }

    private void BuildWheelLabCourse(Physics3DWheelLabShowcaseConfig config)
    {
        float potholeFloorStartZCm = config.PotholeStartZCm + config.PotholeTransitionLengthCm;
        float potholeFloorEndZCm = config.PotholeEndZCm - config.PotholeTransitionLengthCm;
        AddWheelLabRoadSegment(0, config.RoadStartZCm, config.PotholeStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);
        AddWheelLabSlopedRoadSegment(
            1,
            config.PotholeStartZCm,
            potholeFloorStartZCm,
            0f,
            -config.PotholeDepthCm,
            WheelLabPitColor);
        AddWheelLabRoadSegment(
            2,
            potholeFloorStartZCm,
            potholeFloorEndZCm,
            -config.PotholeDepthCm,
            Quaternion.Identity,
            WheelLabPitColor);
        AddWheelLabSlopedRoadSegment(
            3,
            potholeFloorEndZCm,
            config.PotholeEndZCm,
            -config.PotholeDepthCm,
            0f,
            WheelLabPitColor);
        AddWheelLabRoadSegment(4, config.PotholeEndZCm, config.BankStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);
        AddWheelLabRoadSegment(
            5,
            config.BankStartZCm,
            config.BankEndZCm,
            0f,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, DegreesToRadians(config.BankAngleDegrees)),
            WheelLabBankColor);
        AddWheelLabRoadSegment(6, config.BankEndZCm, config.PlatformGapStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);

        AddWheelLabRoadSegment(7, config.PlatformGapEndZCm, config.RampStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);
        float rampAngle = DegreesToRadians(config.RampAngleDegrees);
        float rampLength = config.RampEndZCm - config.RampStartZCm;
        float rampCenterY = (MathF.Sin(rampAngle) * rampLength * 0.5f) -
                            (MathF.Cos(rampAngle) * config.RoadThicknessCm * 0.5f);
        AddWheelLabRoadSegment(
            8,
            config.RampStartZCm,
            config.RampEndZCm,
            rampCenterY + (config.RoadThicknessCm * 0.5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, -rampAngle),
            WheelLabRampColor);
        AddWheelLabRoadSegment(9, config.RampEndZCm, config.BrakeStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);
        AddWheelLabRoadSegment(10, config.BrakeStartZCm, config.BrakeEndZCm, 0f, Quaternion.Identity, WheelLabBrakeColor);
        AddWheelLabRoadSegment(11, config.BrakeEndZCm, config.RoadEndZCm, 0f, Quaternion.Identity, WheelLabRoadColor);

        for (int i = 0; i < config.BumpCount; i++)
        {
            float bumpRampAngle = MathF.Atan2(config.BumpHeightCm, config.BumpDepthCm);
            AddOwnedBody(
                Physics3DBodyKind.Static,
                _wheelLabBumpShape,
                Physics3DShapeKind.Box,
                new Vector3(config.BumpWidthCm, config.BumpHeightCm, config.BumpDepthCm),
                0f,
                new Vector3(0f, 0f, config.FirstBumpZCm + (i * config.BumpSpacingCm)),
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, -bumpRampAngle),
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Discrete,
                WheelLabBumpColor,
                collisionLayer: WheelLabGroundCollisionLayer);
        }

        AddOwnedBody(
            Physics3DBodyKind.Static,
            _wheelLabStopWallShape,
            Physics3DShapeKind.Box,
            new Vector3(config.RoadWidthCm, config.StopWallHeightCm, config.StopWallThicknessCm),
            0f,
            new Vector3(
                0f,
                config.StopWallHeightCm * 0.5f,
                config.RoadEndZCm + (config.StopWallThicknessCm * 0.5f)),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            DynamicRed,
            collisionLayer: WheelLabGroundCollisionLayer);

        CreateWheelLabMovingPlatform(config);
    }

    private void CreateWheelLabMovingPlatform(Physics3DWheelLabShowcaseConfig config)
    {
        _wheelLabMovingPlatformBodyIndex = _bodyCount;
        AddOwnedBody(
            Physics3DBodyKind.Kinematic,
            _wheelLabMovingPlatformShape,
            Physics3DShapeKind.Box,
            new Vector3(
                config.MovingPlatformWidthCm,
                config.MovingPlatformThicknessCm,
                config.PlatformGapEndZCm - config.PlatformGapStartZCm),
            0f,
            new Vector3(0f, -config.MovingPlatformThicknessCm * 0.5f, Midpoint(config.PlatformGapStartZCm, config.PlatformGapEndZCm)),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            WheelLabPlatformColor,
            collisionLayer: WheelLabGroundCollisionLayer);
    }

    private void AddWheelLabRoadSegment(
        int shapeIndex,
        float startZCm,
        float endZCm,
        float topSurfaceYCm,
        Quaternion orientation,
        Vector4 color)
    {
        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        float lengthCm = endZCm - startZCm;
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _wheelLabRoadShapes[shapeIndex],
            Physics3DShapeKind.Box,
            new Vector3(config.RoadWidthCm, config.RoadThicknessCm, lengthCm),
            0f,
            new Vector3(0f, topSurfaceYCm - (config.RoadThicknessCm * 0.5f), Midpoint(startZCm, endZCm)),
            orientation,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            color,
            collisionLayer: WheelLabGroundCollisionLayer);
    }

    private void AddWheelLabSlopedRoadSegment(
        int shapeIndex,
        float startZCm,
        float endZCm,
        float startSurfaceYCm,
        float endSurfaceYCm,
        Vector4 color)
    {
        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        float lengthCm = endZCm - startZCm;
        float angle = MathF.Atan2(endSurfaceYCm - startSurfaceYCm, lengthCm);
        float slopeLengthCm = MathF.Sqrt((lengthCm * lengthCm) +
                                         ((endSurfaceYCm - startSurfaceYCm) *
                                          (endSurfaceYCm - startSurfaceYCm)));
        float centerSurfaceYCm = (startSurfaceYCm + endSurfaceYCm) * 0.5f;
        float centerYCm = centerSurfaceYCm - (MathF.Cos(angle) * config.RoadThicknessCm * 0.5f);
        float centerZCm = Midpoint(startZCm, endZCm) +
                          (MathF.Sin(angle) * config.RoadThicknessCm * 0.5f);
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _wheelLabRoadShapes[shapeIndex],
            Physics3DShapeKind.Box,
            new Vector3(config.RoadWidthCm, config.RoadThicknessCm, slopeLengthCm),
            0f,
            new Vector3(0f, centerYCm, centerZCm),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, -angle),
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            color,
            collisionLayer: WheelLabGroundCollisionLayer);
    }

    private void CreateWheelLabVehicle(
        Physics3DWheelLabShowcaseConfig config,
        Vehicle3DWheelKind mode,
        in Physics3DBodyState chassisState)
    {
        _wheelLabVehicles = new Vehicle3DWorld(
            RequirePhysicsWorld(),
            new Vehicle3DConfig
            {
                VehicleCapacity = config.VehicleCapacity,
                WheelCapacity = config.WheelCapacity,
                QueryBatchCapacity = config.QueryBatchCapacity,
                FixedStepHz = 30
            });

        Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[WheelLabWheelCount];
        _wheelLabModeBodyCount = 0;
        for (int wheelIndex = 0; wheelIndex < WheelLabWheelCount; wheelIndex++)
        {
            Vector3 mount = WheelLabMount(config, wheelIndex);
            float steeringScale = wheelIndex >= 2 ? 1f : 0f;
            if (mode == Vehicle3DWheelKind.Scanning)
            {
                descriptions[wheelIndex] = CreateWheelLabScanningDescription(config, mount, steeringScale);
                continue;
            }

            Vector3 suspensionOffset = -Vector3.UnitY * config.SuspensionRestLengthCm;
            Vector3 bodyPosition = chassisState.PositionCm +
                                   Vector3.Transform(mount + suspensionOffset, chassisState.Orientation);
            Physics3DShapeId wheelShape = mode == Vehicle3DWheelKind.Physical
                ? _wheelLabPhysicalWheelShape
                : _wheelLabBoxWheelShape;
            Physics3DShapeKind wheelShapeKind = mode == Vehicle3DWheelKind.Physical
                ? Physics3DShapeKind.Cylinder
                : Physics3DShapeKind.Box;
            float boxWheelSideCm = WheelLabBoxWheelSideCm(config);
            Vector3 wheelVisualSize = mode == Vehicle3DWheelKind.Physical
                ? new Vector3(config.WheelRadiusCm * 2f, config.WheelWidthCm, config.WheelRadiusCm * 2f)
                : new Vector3(config.WheelWidthCm, boxWheelSideCm, boxWheelSideCm);
            Quaternion wheelOrientation = mode == Vehicle3DWheelKind.Physical
                ? Quaternion.Concatenate(
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI * 0.5f),
                    chassisState.Orientation)
                : chassisState.Orientation;
            Physics3DBodyId wheel = AddWheelLabModeBody(
                wheelShape,
                wheelShapeKind,
                wheelVisualSize,
                bodyPosition,
                wheelOrientation,
                mode == Vehicle3DWheelKind.Physical ? WheelLabPhysicalWheelColor : WheelLabBoxWheelColor,
                config.WheelMass);
            descriptions[wheelIndex] = CreateWheelLabPhysicalDescription(
                config,
                mode,
                wheel,
                mount,
                steeringScale);
        }

        Array.Clear(_wheelLabWheelIds, 0, _wheelLabWheelIds.Length);
        _wheelLabVehicle = _wheelLabVehicles.RegisterVehicle(_wheelLabChassis, descriptions, _wheelLabWheelIds);
        _wheelLabVehicles.SetInput(
            _wheelLabVehicle,
            new Vehicle3DInput(_wheelLabThrottle, _wheelLabBrake, _wheelLabSteering));
    }

    private Physics3DBodyId AddWheelLabModeBody(
        Physics3DShapeId shape,
        Physics3DShapeKind shapeKind,
        Vector3 visualSizeCm,
        Vector3 positionCm,
        Quaternion orientation,
        Vector4 color,
        float mass)
    {
        if (_wheelLabModeBodyCount >= _wheelLabModeBodies.Length)
        {
            throw new InvalidOperationException($"Wheel Lab exceeded its mode body capacity {_wheelLabModeBodies.Length}.");
        }

        Physics3DBodyId body = AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            shape,
            shapeKind,
            visualSizeCm,
            0f,
            positionCm,
            orientation,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            color,
            mass,
            collisionLayer: WheelLabVehicleCollisionLayer,
            collisionSubgroup: CreateWheelLabCollisionSubgroup());
        _wheelLabModeBodies[_wheelLabModeBodyCount++] = body;
        return body;
    }

    private static Vehicle3DWheelDescription CreateWheelLabScanningDescription(
        Physics3DWheelLabShowcaseConfig config,
        Vector3 mount,
        float steeringScale)
        => Vehicle3DWheelDescription.Scanning(
            config.ScanningQueryKind,
            mount,
            -Vector3.UnitY,
            Vector3.UnitZ,
            config.WheelRadiusCm,
            config.SuspensionMinimumLengthCm,
            config.SuspensionRestLengthCm,
            config.SuspensionMaximumLengthCm,
            DegreesToRadians(config.MaximumSteeringAngleDegrees),
            config.SuspensionStiffness,
            config.SuspensionDamping,
            config.MaximumSuspensionForce,
            config.LongitudinalGrip,
            config.LateralGrip,
            config.MaximumDriveForce,
            config.MaximumBrakeForce,
            config.MaximumLateralForce,
            config.MaximumWheelAngularSpeedRadiansPerSecond,
            steeringScale,
            1f,
            1f,
            WheelLabGroundQueryLayer);

    private static float WheelLabBoxWheelSideCm(Physics3DWheelLabShowcaseConfig config)
        => config.WheelRadiusCm * MathF.Sqrt(2f);

    private static Vehicle3DWheelDescription CreateWheelLabPhysicalDescription(
        Physics3DWheelLabShowcaseConfig config,
        Vehicle3DWheelKind mode,
        Physics3DBodyId wheel,
        Vector3 mount,
        float steeringScale)
    {
        float forceScale = mode == Vehicle3DWheelKind.Box ? config.BoxWheelForceScale : 1f;
        return Vehicle3DWheelDescription.Physical(
            mode,
            Vehicle3DWheelQueryKind.Raycast,
            wheel,
            mount,
            -Vector3.UnitY,
            Vector3.UnitZ,
            config.WheelRadiusCm,
            config.SuspensionMinimumLengthCm,
            config.SuspensionRestLengthCm,
            config.SuspensionMaximumLengthCm,
            DegreesToRadians(config.MaximumSteeringAngleDegrees),
            config.SuspensionStiffness,
            config.SuspensionDamping,
            config.MaximumSuspensionForce,
            config.LongitudinalGrip,
            config.LateralGrip,
            config.MaximumDriveForce * forceScale,
            config.MaximumBrakeForce * forceScale,
            config.MaximumLateralForce,
            config.MaximumWheelAngularSpeedRadiansPerSecond,
            steeringScale,
            1f,
            1f,
            WheelLabGroundQueryLayer,
            CreateWheelLabJointSettings(config));
    }

    private static Vehicle3DWheelJointSettings CreateWheelLabJointSettings(Physics3DWheelLabShowcaseConfig config)
    {
        var alignment = new Physics3DSpringSettings(config.AlignmentSpringAngularFrequency, config.AlignmentSpringTwiceDampingRatio);
        var suspension = new Physics3DSpringSettings(config.JointSuspensionSpringAngularFrequency, config.JointSuspensionSpringTwiceDampingRatio);
        var limit = new Physics3DSpringSettings(config.LimitSpringAngularFrequency, config.LimitSpringTwiceDampingRatio);
        var lineServo = new Physics3DServoSettings(
            config.LineServoMaximumSpeed,
            config.LineServoBaseSpeed,
            config.LineServoMaximumForce);
        var motor = new Physics3DMotorSettings(config.AxleMotorMaximumForce, config.AxleMotorSoftness);
        return new Vehicle3DWheelJointSettings(
            alignment,
            suspension,
            limit,
            lineServo,
            motor);
    }

    private void CaptureWheelLabInput(IInputActionReader? input)
    {
        if (_scene != Physics3DShowcaseScene.WheelLab)
        {
            return;
        }

        if (_engine != null && input == null)
        {
            throw new InvalidOperationException("Wheel Lab requires authoritative input.");
        }

        if (input == null)
        {
            return;
        }

        float throttle = input.ReadAction<float>(WheelThrottleAction);
        float steering = input.ReadAction<float>(WheelSteeringAction);
        if (!float.IsFinite(throttle) || throttle < -1.0001f || throttle > 1.0001f)
        {
            throw new InvalidOperationException($"Wheel Lab throttle input '{throttle}' is outside [-1, 1].");
        }

        if (!float.IsFinite(steering) || steering < -1.0001f || steering > 1.0001f)
        {
            throw new InvalidOperationException($"Wheel Lab steering input '{steering}' is outside [-1, 1].");
        }

        _wheelLabThrottle = Math.Clamp(throttle, -1f, 1f);
        _wheelLabSteering = Math.Clamp(steering, -1f, 1f);
        _wheelLabBrake = input.IsDown(WheelBrakeAction) ? 1f : 0f;
        _wheelLabNextModeRequested |= input.PressedThisFrame(WheelNextModeAction);
        _wheelLabResetRequested |= input.PressedThisFrame(WheelResetAction);
    }

    private static void RequireWheelLabInputSchema(PlayerInputHandler input)
    {
        RequireAction(input, WheelThrottleAction);
        RequireAction(input, WheelSteeringAction);
        RequireAction(input, WheelBrakeAction);
        RequireAction(input, WheelNextModeAction);
        RequireAction(input, WheelResetAction);
    }

    private void PrepareWheelLabStep()
    {
        if (_wheelLabNextModeRequested)
        {
            _wheelLabNextModeRequested = false;
            SwitchWheelLabMode(NextWheelLabMode(_wheelLabMode));
        }

        if (_wheelLabResetRequested)
        {
            _wheelLabResetRequested = false;
            ResetWheelLabVehicle();
        }

        if (_wheelLabTrialStatus == Physics3DWheelLabTrialStatus.Ready && HasWheelLabTrialStartInput())
        {
            BeginWheelLabTrial();
        }

        BeginWheelLabBrakingIfNeeded();
        AnimateWheelLabPlatform();
        Vehicle3DWorld vehicles = RequireWheelLabVehicles();
        Vehicle3DInput input = _wheelLabTrialStatus is Physics3DWheelLabTrialStatus.Succeeded or
            Physics3DWheelLabTrialStatus.Failed
            ? default
            : new Vehicle3DInput(_wheelLabThrottle, _wheelLabBrake, _wheelLabSteering);
        vehicles.SetInput(_wheelLabVehicle, input);
        vehicles.PrepareFixedStep();
    }

    private void ObserveWheelLabStep()
    {
        Vehicle3DWorld vehicles = RequireWheelLabVehicles();
        int grounded = 0;
        float compression = 0f;
        float maximumSlip = 0f;
        for (int i = 0; i < WheelLabWheelCount; i++)
        {
            Vehicle3DWheelState state = vehicles.GetWheelState(_wheelLabWheelIds[i]);
            _wheelLabWheelStates[i] = state;
            if (state.Grounded)
            {
                grounded++;
                compression += state.CompressionCm;
            }

            maximumSlip = MathF.Max(maximumSlip, state.SlipVelocityCmPerSecond.Length());
        }

        Physics3DBodyState chassis = RequirePhysicsWorld().GetBodyState(_wheelLabChassis);
        _wheelLabGroundedWheelCount = grounded;
        _wheelLabAverageCompressionCm = grounded > 0 ? compression / grounded : 0f;
        _wheelLabMaximumSlipCmPerSecond = maximumSlip;
        _wheelLabSpeedKph = chassis.LinearVelocityCmPerSecond.Length() * 0.036f;
        _wheelLabHasObservedStep = true;
        if (_wheelLabTrialStatus == Physics3DWheelLabTrialStatus.Running)
        {
            UpdateWheelLabCourseSection(chassis.PositionCm.Z);
            ObserveWheelLabTrial(in chassis, grounded, maximumSlip);
        }
    }

    private bool HasWheelLabTrialStartInput()
    {
        float deadZone = ActiveConfig.WheelLab.TrialInputDeadZone;
        return _wheelLabThrottle > deadZone;
    }

    private void BeginWheelLabTrial()
    {
        Vehicle3DWheelKind mode = _wheelLabMode;
        DestroyWheelLabVehicleAndModeBodies();
        RestoreWheelLabAuthoredState(mode);
        _wheelLabTrialStatus = Physics3DWheelLabTrialStatus.Running;
        _wheelLabTrialStartState = RequirePhysics3DChassisState();
        StoreWheelLabTrialResult(CreateWheelLabTrialResult(
            Physics3DWheelLabTrialStatus.Running,
            Physics3DWheelLabTrialReason.None));
        _lastAction = $"{WheelLabModeName(mode)} run started from the shared course state.";
    }

    private void ObserveWheelLabTrial(
        in Physics3DBodyState chassis,
        int groundedWheelCount,
        float maximumSlipCmPerSecond)
    {
        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        _wheelLabTrialTick++;
        _wheelLabGroundedSamples += groundedWheelCount;
        _wheelLabTrialMaximumSlipCmPerSecond = MathF.Max(
            _wheelLabTrialMaximumSlipCmPerSecond,
            maximumSlipCmPerSecond);
        for (int i = 0; i < WheelLabWheelCount; i++)
        {
            _wheelLabTrialMaximumCompressionCm = MathF.Max(
                _wheelLabTrialMaximumCompressionCm,
                _wheelLabWheelStates[i].CompressionCm);
        }

        if (_wheelLabTrialBrakeMeasured)
        {
            Vector3 brakingDelta = chassis.PositionCm - _wheelLabTrialBrakePreviousPositionCm;
            _wheelLabTrialBrakingDistanceCm += MathF.Sqrt(
                (brakingDelta.X * brakingDelta.X) +
                (brakingDelta.Z * brakingDelta.Z));
            _wheelLabTrialBrakePreviousPositionCm = chassis.PositionCm;
        }

        if (chassis.PositionCm.Y < config.ResetBelowYCm)
        {
            FinalizeWheelLabTrial(
                Physics3DWheelLabTrialStatus.Failed,
                Physics3DWheelLabTrialReason.FellBelowCourse);
            return;
        }

        if (MathF.Abs(chassis.PositionCm.X - config.SpawnXCm) > config.TrialMaximumLateralOffsetCm)
        {
            FinalizeWheelLabTrial(
                Physics3DWheelLabTrialStatus.Failed,
                Physics3DWheelLabTrialReason.LeftRoute);
            return;
        }

        if (_wheelLabTrialBrakeMeasured &&
            chassis.PositionCm.Z >= config.TrialCompletionMinimumZCm &&
            chassis.PositionCm.Z <= config.BrakeEndZCm &&
            _wheelLabSpeedKph <= config.TrialStopSpeedKph)
        {
            FinalizeWheelLabTrial(
                Physics3DWheelLabTrialStatus.Succeeded,
                Physics3DWheelLabTrialReason.None);
            return;
        }

        if (chassis.PositionCm.Z > config.BrakeEndZCm)
        {
            FinalizeWheelLabTrial(
                Physics3DWheelLabTrialStatus.Failed,
                Physics3DWheelLabTrialReason.OvershotBrakingZone);
            return;
        }

        if (_wheelLabTrialTick >= config.TrialTimeLimitTicks)
        {
            FinalizeWheelLabTrial(
                Physics3DWheelLabTrialStatus.Failed,
                Physics3DWheelLabTrialReason.TimeLimit);
            return;
        }

        StoreWheelLabTrialResult(CreateWheelLabTrialResult(
            Physics3DWheelLabTrialStatus.Running,
            Physics3DWheelLabTrialReason.None));
    }

    private void BeginWheelLabBrakingIfNeeded()
    {
        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        if (_wheelLabTrialStatus != Physics3DWheelLabTrialStatus.Running ||
            _wheelLabTrialBrakeMeasured ||
            _wheelLabBrake < config.TrialBrakeInputThreshold ||
            _wheelLabSpeedKph < config.TrialMinimumBrakeStartSpeedKph)
        {
            return;
        }

        Physics3DBodyState chassis = RequirePhysics3DChassisState();
        Vector3 positionCm = chassis.PositionCm;
        if (positionCm.Z < config.BrakeStartZCm || positionCm.Z > config.BrakeEndZCm)
        {
            return;
        }

        _wheelLabTrialBrakeMeasured = true;
        _wheelLabTrialBrakePreviousPositionCm = positionCm;
        _wheelLabTrialBrakingDistanceCm = 0f;
    }

    private void AnimateWheelLabPlatform()
    {
        if (_wheelLabMovingPlatformBodyIndex < 0)
        {
            throw new InvalidOperationException("Wheel Lab moving platform body is missing.");
        }

        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        float phase = (_wheelLabTrialTick + 1f) * config.MovingPlatformRadiansPerStep;
        Vector3 nextPosition = new(
            MathF.Sin(phase) * config.MovingPlatformTravelCm,
            -config.MovingPlatformThicknessCm * 0.5f,
            Midpoint(config.PlatformGapStartZCm, config.PlatformGapEndZCm));
        Quaternion nextOrientation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            MathF.Sin(phase * 0.63f) * config.MovingPlatformMaximumYawRadians);
        SetKinematicCourseNextPose(_wheelLabMovingPlatformBodyIndex, nextPosition, nextOrientation);
    }

    private void SwitchWheelLabMode(Vehicle3DWheelKind mode)
    {
        if (_scene != Physics3DShowcaseScene.WheelLab)
        {
            throw new InvalidOperationException("Wheel mode can only change while Wheel Lab is active.");
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Wheel Lab wheel kind.");
        }

        if (mode == _wheelLabMode)
        {
            _lastAction = WheelLabModeMessage(mode);
            return;
        }

        bool invalidated = _wheelLabTrialStatus == Physics3DWheelLabTrialStatus.Running;
        if (invalidated)
        {
            FinalizeWheelLabTrial(
                Physics3DWheelLabTrialStatus.Invalidated,
                Physics3DWheelLabTrialReason.WheelTypeChanged);
        }

        DestroyWheelLabVehicleAndModeBodies();
        _wheelLabMode = mode;
        RestoreWheelLabAuthoredState(mode);
        _lastAction = invalidated
            ? $"Previous run void: wheel type changed. {WheelLabModeName(mode)} is ready at the shared start."
            : $"{WheelLabModeName(mode)} is ready at the shared start.";
    }

    private void ResetWheelLabVehicle()
    {
        bool invalidated = _wheelLabTrialStatus == Physics3DWheelLabTrialStatus.Running;
        if (invalidated)
        {
            FinalizeWheelLabTrial(
                Physics3DWheelLabTrialStatus.Invalidated,
                Physics3DWheelLabTrialReason.ManualReset);
        }

        Vehicle3DWheelKind mode = _wheelLabMode;
        DestroyWheelLabVehicleAndModeBodies();
        RestoreWheelLabAuthoredState(mode);
        _lastAction = invalidated
            ? "Previous run void: the player reset mid-course. The selected wheel type is ready at the shared start."
            : "Vehicle returned to the shared start with the selected wheel type unchanged.";
    }

    private void RestoreWheelLabAuthoredState(Vehicle3DWheelKind mode)
    {
        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        RemoveWheelLabOwnedBodies();
        BuildWheelLabCourse(config);
        CreateWheelLabChassis(config);
        Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(_wheelLabChassis);
        CreateWheelLabVehicle(ActiveConfig.WheelLab, mode, state);
        ResetWheelLabMovingPlatform();
        ResetWheelLabTrialTracking();
    }

    private void ResetWheelLabMovingPlatform()
    {
        if (_wheelLabMovingPlatformBodyIndex < 0)
        {
            throw new InvalidOperationException("Wheel Lab moving platform body is missing.");
        }

        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        Physics3DBodyState platform = RequirePhysicsWorld().GetBodyState(
            _bodyIds[_wheelLabMovingPlatformBodyIndex]);
        platform.PositionCm = new Vector3(
            0f,
            -config.MovingPlatformThicknessCm * 0.5f,
            Midpoint(config.PlatformGapStartZCm, config.PlatformGapEndZCm));
        platform.Orientation = Quaternion.Identity;
        platform.LinearVelocityCmPerSecond = Vector3.Zero;
        platform.AngularVelocityRadiansPerSecond = Vector3.Zero;
        platform.Awake = true;
        SetBodyStateAndPose(_wheelLabMovingPlatformBodyIndex, in platform);
    }

    private void ResetWheelLabTrialTracking()
    {
        _wheelLabTrialStatus = Physics3DWheelLabTrialStatus.Ready;
        _wheelLabTrialStartState = RequirePhysics3DChassisState();
        _wheelLabTrialPlatformStartState = RequirePhysicsWorld().GetBodyState(
            _bodyIds[_wheelLabMovingPlatformBodyIndex]);
        _wheelLabTrialTick = 0;
        _wheelLabGroundedSamples = 0;
        _wheelLabTrialMaximumCompressionCm = 0f;
        _wheelLabTrialMaximumSlipCmPerSecond = 0f;
        _wheelLabTrialBrakeMeasured = false;
        _wheelLabTrialBrakePreviousPositionCm = default;
        _wheelLabTrialBrakingDistanceCm = 0f;
        _wheelLabSection = WheelLabCourseSection.Start;
        _wheelLabHasObservedStep = false;
        _wheelLabGroundedWheelCount = 0;
        _wheelLabSpeedKph = 0f;
        _wheelLabAverageCompressionCm = 0f;
        _wheelLabMaximumSlipCmPerSecond = 0f;
    }

    private void FinalizeWheelLabTrial(
        Physics3DWheelLabTrialStatus status,
        Physics3DWheelLabTrialReason reason)
    {
        if (_wheelLabTrialStatus != Physics3DWheelLabTrialStatus.Running)
        {
            throw new InvalidOperationException(
                $"Wheel Lab cannot finalize trial state '{_wheelLabTrialStatus}' as '{status}'.");
        }

        if (status is not Physics3DWheelLabTrialStatus.Succeeded and
            not Physics3DWheelLabTrialStatus.Failed and
            not Physics3DWheelLabTrialStatus.Invalidated)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Wheel Lab requires a terminal trial status.");
        }

        _wheelLabTrialStatus = status;
        if (status == Physics3DWheelLabTrialStatus.Succeeded)
        {
            _wheelLabSection = WheelLabCourseSection.Finish;
        }

        Physics3DWheelLabTrialResult result = CreateWheelLabTrialResult(status, reason);
        StoreWheelLabTrialResult(result);
        _lastAction = status switch
        {
            Physics3DWheelLabTrialStatus.Succeeded =>
                $"{WheelLabModeName(_wheelLabMode)} passed the shared route in {_wheelLabTrialTick} ticks.",
            Physics3DWheelLabTrialStatus.Failed =>
                $"{WheelLabModeName(_wheelLabMode)} failed the shared route: {WheelLabTrialReasonName(reason)}. Press R to retry.",
            Physics3DWheelLabTrialStatus.Invalidated =>
                $"{WheelLabModeName(_wheelLabMode)} run void: {WheelLabTrialReasonName(reason)}.",
            _ => throw new InvalidOperationException($"Unsupported Wheel Lab terminal status '{status}'.")
        };
    }

    private Physics3DWheelLabTrialResult CreateWheelLabTrialResult(
        Physics3DWheelLabTrialStatus status,
        Physics3DWheelLabTrialReason reason)
    {
        float groundedRatio = _wheelLabTrialTick > 0
            ? (float)_wheelLabGroundedSamples / (_wheelLabTrialTick * (float)WheelLabWheelCount)
            : 0f;
        return new Physics3DWheelLabTrialResult(
            _wheelLabMode,
            status,
            reason,
            _wheelLabTrialTick,
            _wheelLabTrialMaximumCompressionCm,
            _wheelLabTrialMaximumSlipCmPerSecond,
            groundedRatio,
            _wheelLabTrialBrakingDistanceCm,
            _wheelLabTrialBrakeMeasured);
    }

    private void StoreWheelLabTrialResult(in Physics3DWheelLabTrialResult result)
    {
        int slot = WheelLabResultSlot(result.WheelKind);
        if ((uint)slot >= (uint)_wheelLabTrialResults.Length)
        {
            throw new InvalidOperationException(
                $"Wheel Lab comparison result capacity {_wheelLabTrialResults.Length} cannot store slot {slot} for '{result.WheelKind}'.");
        }

        _wheelLabTrialResults[slot] = result;
    }

    private void DestroyWheelLabVehicleAndModeBodies()
    {
        ReleaseRegisteredWheelLabVehicle();
        for (int i = _wheelLabModeBodyCount - 1; i >= 0; i--)
        {
            RemoveLastWheelLabOwnedBody(_wheelLabModeBodies[i]);
            _wheelLabModeBodies[i] = default;
        }

        _wheelLabModeBodyCount = 0;
    }

    private void RemoveWheelLabOwnedBodies()
    {
        if (_wheelLabOwnedBodyStartIndex < 0 || _wheelLabOwnedBodyStartIndex > _bodyCount)
        {
            throw new InvalidOperationException(
                $"Wheel Lab owned-body start {_wheelLabOwnedBodyStartIndex} is invalid for body count {_bodyCount}.");
        }

        while (_bodyCount > _wheelLabOwnedBodyStartIndex)
        {
            RemoveLastWheelLabOwnedBody(_bodyIds[_bodyCount - 1]);
        }

        _wheelLabChassis = default;
        _wheelLabChassisBodyIndex = -1;
        _wheelLabMovingPlatformBodyIndex = -1;
    }

    private void RemoveLastWheelLabOwnedBody(Physics3DBodyId expectedBody)
    {
        int index = _bodyCount - 1;
        if (index < 0 || _bodyIds[index] != expectedBody)
        {
            throw new InvalidOperationException(
                $"Wheel Lab mode body removal expected '{expectedBody}' at owned body tail {index}.");
        }

        if (!RequirePhysicsWorld().ContainsBody(expectedBody))
        {
            throw new InvalidOperationException($"Wheel Lab mode body '{expectedBody}' disappeared before mode switch.");
        }

        RequirePhysicsWorld().DestroyBody(expectedBody);
        if (!RequireEcsWorld().IsAlive(_bodyEntities[index]))
        {
            throw new InvalidOperationException($"Wheel Lab ECS body at owned index {index} disappeared before mode switch.");
        }

        RequireEcsWorld().Destroy(_bodyEntities[index]);
        switch (_bodyKinds[index])
        {
            case Physics3DBodyKind.Dynamic:
                _dynamicBodyCount--;
                break;
            case Physics3DBodyKind.Kinematic:
                _kinematicBodyCount--;
                break;
            case Physics3DBodyKind.Static:
                _staticBodyCount--;
                break;
            default:
                throw new InvalidOperationException("Wheel Lab trial-boundary body has an unsupported kind.");
        }

        _bodyCount--;
        _bodyIds[index] = default;
        _bodyEntities[index] = default;
        _bodyKinds[index] = default;
        _bodyShapeKinds[index] = default;
        _bodyVisualSizesCm[index] = default;
        _bodyCapsuleCylinderLengthsCm[index] = 0f;
        _bodyColors[index] = default;
    }

    private void ReleaseWheelLabScene()
    {
        ReleaseWheelLabCamera();
        if (_wheelLabVehicles is not null)
        {
            ReleaseRegisteredWheelLabVehicle();
        }

        _wheelLabChassis = default;
        _wheelLabMode = default;
        _wheelLabSection = default;
        _wheelLabModeBodyCount = 0;
        _wheelLabOwnedBodyStartIndex = -1;
        _wheelLabChassisBodyIndex = -1;
        _wheelLabMovingPlatformBodyIndex = -1;
        _wheelLabGroundedWheelCount = 0;
        _wheelLabThrottle = 0f;
        _wheelLabSteering = 0f;
        _wheelLabBrake = 0f;
        _wheelLabSpeedKph = 0f;
        _wheelLabAverageCompressionCm = 0f;
        _wheelLabMaximumSlipCmPerSecond = 0f;
        _wheelLabTrialStatus = Physics3DWheelLabTrialStatus.NotRun;
        _wheelLabTrialStartState = default;
        _wheelLabTrialPlatformStartState = default;
        _wheelLabTrialTick = 0;
        _wheelLabGroundedSamples = 0;
        _wheelLabTrialMaximumCompressionCm = 0f;
        _wheelLabTrialMaximumSlipCmPerSecond = 0f;
        _wheelLabTrialBrakeMeasured = false;
        _wheelLabTrialBrakePreviousPositionCm = default;
        _wheelLabTrialBrakingDistanceCm = 0f;
        _wheelLabNextModeRequested = false;
        _wheelLabResetRequested = false;
        _wheelLabHasObservedStep = false;
        Array.Clear(_wheelLabModeBodies, 0, _wheelLabModeBodies.Length);
        Array.Clear(_wheelLabWheelIds, 0, _wheelLabWheelIds.Length);
        Array.Clear(_wheelLabWheelStates, 0, _wheelLabWheelStates.Length);
        _wheelLabTrialResults = Array.Empty<Physics3DWheelLabTrialResult>();
    }

    internal void SynchronizeWheelLabCameraAfterMapFocus()
    {
        if (_scene == Physics3DShowcaseScene.WheelLab)
        {
            ActivateWheelLabCamera();
        }
    }

    private void ActivateWheelLabCamera()
    {
        if (!_wheelLabChassis.IsValid)
        {
            throw new InvalidOperationException("Wheel Lab camera cannot activate before the chassis exists.");
        }

        _wheelLabCameraActive = ActivateStationFollowCamera(
            ActiveConfig.WheelLab.CameraId,
            CaptureWheelLabCameraTarget,
            "Wheel Lab");
    }

    private CameraTargetTransformSnapshot CaptureWheelLabCameraTarget()
    {
        Physics3DBodyState chassis = RequirePhysics3DChassisState();
        return new CameraTargetTransformSnapshot(
            new Vector2(chassis.PositionCm.X, chassis.PositionCm.Z),
            hasHeightCm: true,
            heightCm: chassis.PositionCm.Y + ActiveConfig.WheelLab.CameraTargetHeightOffsetCm);
    }

    private void ReleaseWheelLabCamera()
    {
        if (!_wheelLabCameraActive)
        {
            return;
        }

        RestoreDefaultCamera("Wheel Lab");
        _wheelLabCameraActive = false;
    }

    private void ReleaseRegisteredWheelLabVehicle()
    {
        Vehicle3DWorld vehicles = RequireWheelLabVehicles();
        if (!vehicles.ContainsVehicle(_wheelLabVehicle))
        {
            throw new InvalidOperationException(
                $"Wheel Lab vehicle '{_wheelLabVehicle}' disappeared before its wheel assembly was released.");
        }

        vehicles.RemoveVehicle(_wheelLabVehicle);
        vehicles.Dispose();
        _wheelLabVehicles = null;
        _wheelLabVehicle = default;
        Array.Clear(_wheelLabWheelIds, 0, _wheelLabWheelIds.Length);
        Array.Clear(_wheelLabWheelStates, 0, _wheelLabWheelStates.Length);
    }

    internal bool TryGetWheelLabDebugVisual(int wheelIndex, out Physics3DWheelLabDebugVisual visual)
    {
        if (_scene != Physics3DShowcaseScene.WheelLab ||
            (uint)wheelIndex >= WheelLabWheelCount ||
            !_wheelLabChassis.IsValid)
        {
            visual = default;
            return false;
        }

        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        Physics3DBodyState chassis = RequirePhysicsWorld().GetBodyState(_wheelLabChassis);
        Vector3 mount = WheelLabMount(config, wheelIndex);
        Vector3 origin = chassis.PositionCm + Vector3.Transform(mount, chassis.Orientation);
        Vector3 suspensionDirection = Vector3.Transform(-Vector3.UnitY, chassis.Orientation);
        Vehicle3DWheelState state = _wheelLabWheelStates[wheelIndex];
        float suspensionLength = _wheelLabHasObservedStep
            ? state.SuspensionLengthCm
            : config.SuspensionRestLengthCm;
        Vector3 wheelCenter = origin + (suspensionDirection * suspensionLength);
        visual = new Physics3DWheelLabDebugVisual(
            _wheelLabMode,
            state.Grounded,
            origin,
            wheelCenter,
            config.WheelRadiusCm,
            state.ContactPointCm,
            state.ContactNormal,
            state.SlipVelocityCmPerSecond,
            state.CompressionCm);
        return true;
    }

    internal string CreateWheelLabSummary()
    {
        if (_scene != Physics3DShowcaseScene.WheelLab)
        {
            return "Select Wheel Lab to drive the suspension course.";
        }

        return $"{WheelLabModeName(_wheelLabMode)} · {WheelLabTrialStatusName(_wheelLabTrialStatus)} {_wheelLabTrialTick}/{ActiveConfig.WheelLab.TrialTimeLimitTicks} · " +
               $"{_wheelLabGroundedWheelCount}/4 grounded · " +
               $"{_wheelLabSpeedKph:0.0} km/h · {_wheelLabAverageCompressionCm:0.0} cm compression · " +
               $"{_wheelLabMaximumSlipCmPerSecond:0} cm/s slip";
    }

    internal string CreateWheelLabRouteGuide()
    {
        if (_scene != Physics3DShowcaseScene.WheelLab)
        {
            return "Select Wheel Lab to view the shared route.";
        }

        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        float fixedDeltaSeconds = RequirePhysicsWorld().FixedDeltaSeconds;
        float throttleSeconds = config.TrialRecommendedThrottleTicks * fixedDeltaSeconds;
        float brakeSeconds = config.TrialRecommendedBrakeTicks * fixedDeltaSeconds;
        return $"Reference run: hold W for {throttleSeconds:0.0}s, then Space for up to {brakeSeconds:0.0}s. " +
               "Press Q only between runs.";
    }

    internal bool TryGetWheelLabTrialResult(
        Vehicle3DWheelKind wheelKind,
        out Physics3DWheelLabTrialResult result)
    {
        if (_scene != Physics3DShowcaseScene.WheelLab || _wheelLabTrialResults.Length == 0)
        {
            result = default;
            return false;
        }

        int slot = WheelLabResultSlot(wheelKind);
        if ((uint)slot >= (uint)_wheelLabTrialResults.Length)
        {
            throw new InvalidOperationException(
                $"Wheel Lab comparison result capacity {_wheelLabTrialResults.Length} cannot read slot {slot} for '{wheelKind}'.");
        }

        result = _wheelLabTrialResults[slot];
        return true;
    }

    internal string CreateWheelLabTrialResultSummary(Vehicle3DWheelKind wheelKind)
    {
        if (!TryGetWheelLabTrialResult(wheelKind, out Physics3DWheelLabTrialResult result))
        {
            return "NOT AVAILABLE";
        }

        if (result.Status == Physics3DWheelLabTrialStatus.NotRun)
        {
            return "NOT RUN · choose this wheel type, then use the shared route";
        }

        string outcome = result.Status switch
        {
            Physics3DWheelLabTrialStatus.Running => "LIVE",
            Physics3DWheelLabTrialStatus.Succeeded => "PASS",
            Physics3DWheelLabTrialStatus.Failed => "FAIL",
            Physics3DWheelLabTrialStatus.Invalidated => "VOID",
            _ => throw new InvalidOperationException($"Unsupported Wheel Lab result status '{result.Status}'.")
        };
        string brake = result.BrakeMeasured ? $"{result.BrakingDistanceCm:0} cm brake" : "brake not measured";
        string reason = result.Status is Physics3DWheelLabTrialStatus.Failed or Physics3DWheelLabTrialStatus.Invalidated
            ? $" · {WheelLabTrialReasonName(result.Reason)}"
            : string.Empty;
        return $"{outcome} · tick {result.CompletionTick} · {result.MaximumSuspensionCompressionCm:0.0} cm max travel · " +
               $"{result.MaximumSlipCmPerSecond:0} cm/s max slip · {result.GroundedRatio:P0} contact · {brake}{reason}";
    }

    private void UpdateWheelLabCourseSection(float positionZCm)
    {
        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        WheelLabCourseSection next;
        if (positionZCm < config.PotholeStartZCm)
        {
            next = WheelLabCourseSection.Bumps;
        }
        else if (positionZCm < config.PotholeEndZCm)
        {
            next = WheelLabCourseSection.Pothole;
        }
        else if (positionZCm < config.BankEndZCm)
        {
            next = WheelLabCourseSection.SideSlope;
        }
        else if (positionZCm < config.PlatformGapEndZCm)
        {
            next = WheelLabCourseSection.MovingPlatform;
        }
        else if (positionZCm < config.RampEndZCm)
        {
            next = WheelLabCourseSection.Jump;
        }
        else
        {
            next = WheelLabCourseSection.Braking;
        }
        if (next == _wheelLabSection)
        {
            return;
        }

        _wheelLabSection = next;
        _lastAction = next switch
        {
            WheelLabCourseSection.Bumps => "Hold throttle over the yellow approach ramps and watch suspension travel.",
            WheelLabCourseSection.Pothole => "The recessed brown lane exposes wheel drop and recovery.",
            WheelLabCourseSection.SideSlope => "Counter-steer across the blue side slope while lateral grip holds the line.",
            WheelLabCourseSection.MovingPlatform => "Cross the moving purple platform; tire velocity includes its contact-point motion.",
            WheelLabCourseSection.Jump => "Commit to the red ramp, then watch all four contact markers clear the ground.",
            WheelLabCourseSection.Braking => "Release throttle and hold Space inside the green braking zone.",
            WheelLabCourseSection.Finish => "Course complete. Press R to restart or Q to compare another wheel type.",
            _ => "Drive forward to begin the Wheel Lab course."
        };
    }

    private Physics3DBodyState RequirePhysics3DChassisState()
    {
        if (!_wheelLabChassis.IsValid || !RequirePhysicsWorld().ContainsBody(_wheelLabChassis))
        {
            throw new InvalidOperationException("Wheel Lab chassis is missing.");
        }

        return RequirePhysicsWorld().GetBodyState(_wheelLabChassis);
    }

    private Vehicle3DWorld RequireWheelLabVehicles() => _wheelLabVehicles
        ?? throw new InvalidOperationException("Wheel Lab vehicle world is unavailable.");

    private static Physics3DCollisionSubgroup CreateWheelLabCollisionSubgroup()
        => new(WheelLabAssemblyId, subgroupIndex: 0, collidesWithSubgroups: 0u);

    private static Vector3 WheelLabMount(Physics3DWheelLabShowcaseConfig config, int wheelIndex)
    {
        float side = (wheelIndex & 1) == 0 ? -1f : 1f;
        float end = wheelIndex < 2 ? -1f : 1f;
        return new Vector3(
            side * config.WheelTrackCm * 0.5f,
            config.WheelMountYCm,
            end * config.WheelBaseCm * 0.5f);
    }

    private static Vector3 WheelLabSpawnPosition(Physics3DWheelLabShowcaseConfig config)
        => new(config.SpawnXCm, config.SpawnYCm, config.SpawnZCm);

    private static Vehicle3DWheelKind NextWheelLabMode(Vehicle3DWheelKind mode) => mode switch
    {
        Vehicle3DWheelKind.Physical => Vehicle3DWheelKind.Box,
        Vehicle3DWheelKind.Box => Vehicle3DWheelKind.Scanning,
        Vehicle3DWheelKind.Scanning => Vehicle3DWheelKind.Physical,
        _ => throw new InvalidOperationException($"Unsupported Wheel Lab wheel kind '{mode}'.")
    };

    private static int WheelLabResultSlot(Vehicle3DWheelKind mode) => mode switch
    {
        Vehicle3DWheelKind.Physical => 0,
        Vehicle3DWheelKind.Box => 1,
        Vehicle3DWheelKind.Scanning => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Wheel Lab wheel kind.")
    };

    private static string WheelLabModeName(Vehicle3DWheelKind mode) => mode switch
    {
        Vehicle3DWheelKind.Physical => "Physical Wheels",
        Vehicle3DWheelKind.Box => "Box Wheels",
        Vehicle3DWheelKind.Scanning => "Scanning Wheels",
        _ => throw new InvalidOperationException($"Unsupported Wheel Lab wheel kind '{mode}'.")
    };

    private static string WheelLabModeMessage(Vehicle3DWheelKind mode) => mode switch
    {
        Vehicle3DWheelKind.Physical => "Physical wheels selected: real wheel bodies, suspension constraints, steering, drive, and braking are active.",
        Vehicle3DWheelKind.Box => "Box Wheels selected: the same suspension recipe now drives collision boxes through the course.",
        Vehicle3DWheelKind.Scanning => "Scanning wheels selected: sphere casts support the same chassis without separate wheel bodies.",
        _ => throw new InvalidOperationException($"Unsupported Wheel Lab wheel kind '{mode}'.")
    };

    private static string WheelLabTrialStatusName(Physics3DWheelLabTrialStatus status) => status switch
    {
        Physics3DWheelLabTrialStatus.NotRun => "NOT RUN",
        Physics3DWheelLabTrialStatus.Ready => "READY",
        Physics3DWheelLabTrialStatus.Running => "RUNNING",
        Physics3DWheelLabTrialStatus.Succeeded => "PASS",
        Physics3DWheelLabTrialStatus.Failed => "FAIL",
        Physics3DWheelLabTrialStatus.Invalidated => "VOID",
        _ => throw new InvalidOperationException($"Unsupported Wheel Lab trial status '{status}'.")
    };

    private static string WheelLabTrialReasonName(Physics3DWheelLabTrialReason reason) => reason switch
    {
        Physics3DWheelLabTrialReason.None => "completed",
        Physics3DWheelLabTrialReason.WheelTypeChanged => "wheel type changed mid-run",
        Physics3DWheelLabTrialReason.ManualReset => "manual reset mid-run",
        Physics3DWheelLabTrialReason.FellBelowCourse => "vehicle fell below the course",
        Physics3DWheelLabTrialReason.LeftRoute => "vehicle left the route",
        Physics3DWheelLabTrialReason.OvershotBrakingZone => "vehicle passed the braking zone without stopping",
        Physics3DWheelLabTrialReason.TimeLimit => "time limit reached",
        _ => throw new InvalidOperationException($"Unsupported Wheel Lab trial reason '{reason}'.")
    };

    private static float Midpoint(float a, float b) => (a + b) * 0.5f;
    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
}

internal enum WheelLabCourseSection : byte
{
    Start = 0,
    Bumps = 1,
    Pothole = 2,
    SideSlope = 3,
    MovingPlatform = 4,
    Jump = 5,
    Braking = 6,
    Finish = 7
}

internal enum Physics3DWheelLabTrialStatus : byte
{
    NotRun = 0,
    Ready = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Invalidated = 5
}

internal enum Physics3DWheelLabTrialReason : byte
{
    None = 0,
    WheelTypeChanged = 1,
    ManualReset = 2,
    FellBelowCourse = 3,
    LeftRoute = 4,
    TimeLimit = 5,
    OvershotBrakingZone = 6
}

internal readonly struct Physics3DWheelLabTrialResult
{
    public Physics3DWheelLabTrialResult(
        Vehicle3DWheelKind wheelKind,
        Physics3DWheelLabTrialStatus status,
        Physics3DWheelLabTrialReason reason,
        int completionTick,
        float maximumSuspensionCompressionCm,
        float maximumSlipCmPerSecond,
        float groundedRatio,
        float brakingDistanceCm,
        bool brakeMeasured)
    {
        WheelKind = wheelKind;
        Status = status;
        Reason = reason;
        CompletionTick = completionTick;
        MaximumSuspensionCompressionCm = maximumSuspensionCompressionCm;
        MaximumSlipCmPerSecond = maximumSlipCmPerSecond;
        GroundedRatio = groundedRatio;
        BrakingDistanceCm = brakingDistanceCm;
        BrakeMeasured = brakeMeasured;
    }

    public Vehicle3DWheelKind WheelKind { get; }
    public Physics3DWheelLabTrialStatus Status { get; }
    public Physics3DWheelLabTrialReason Reason { get; }
    public int CompletionTick { get; }
    public float MaximumSuspensionCompressionCm { get; }
    public float MaximumSlipCmPerSecond { get; }
    public float GroundedRatio { get; }
    public float BrakingDistanceCm { get; }
    public bool BrakeMeasured { get; }

    public static Physics3DWheelLabTrialResult NotRun(Vehicle3DWheelKind wheelKind)
        => new(
            wheelKind,
            Physics3DWheelLabTrialStatus.NotRun,
            Physics3DWheelLabTrialReason.None,
            completionTick: 0,
            maximumSuspensionCompressionCm: 0f,
            maximumSlipCmPerSecond: 0f,
            groundedRatio: 0f,
            brakingDistanceCm: 0f,
            brakeMeasured: false);
}

internal readonly struct Physics3DWheelLabDebugVisual
{
    public Physics3DWheelLabDebugVisual(
        Vehicle3DWheelKind mode,
        bool grounded,
        Vector3 suspensionOriginCm,
        Vector3 wheelCenterCm,
        float wheelRadiusCm,
        Vector3 contactPointCm,
        Vector3 contactNormal,
        Vector3 slipVelocityCmPerSecond,
        float compressionCm)
    {
        Mode = mode;
        Grounded = grounded;
        SuspensionOriginCm = suspensionOriginCm;
        WheelCenterCm = wheelCenterCm;
        WheelRadiusCm = wheelRadiusCm;
        ContactPointCm = contactPointCm;
        ContactNormal = contactNormal;
        SlipVelocityCmPerSecond = slipVelocityCmPerSecond;
        CompressionCm = compressionCm;
    }

    public Vehicle3DWheelKind Mode { get; }
    public bool Grounded { get; }
    public Vector3 SuspensionOriginCm { get; }
    public Vector3 WheelCenterCm { get; }
    public float WheelRadiusCm { get; }
    public Vector3 ContactPointCm { get; }
    public Vector3 ContactNormal { get; }
    public Vector3 SlipVelocityCmPerSecond { get; }
    public float CompressionCm { get; }
}
