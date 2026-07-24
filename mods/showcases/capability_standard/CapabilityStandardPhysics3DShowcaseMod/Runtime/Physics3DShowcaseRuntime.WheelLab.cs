using System;
using System.Numerics;
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
    private const int WheelLabMaximumModeBodies = WheelLabWheelCount * 2;
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

    private readonly Physics3DShapeId[] _wheelLabRoadShapes = new Physics3DShapeId[10];
    private readonly Physics3DBodyId[] _wheelLabModeBodies = new Physics3DBodyId[WheelLabMaximumModeBodies];
    private readonly Vehicle3DWheelId[] _wheelLabWheelIds = new Vehicle3DWheelId[WheelLabWheelCount];
    private readonly Vehicle3DWheelState[] _wheelLabWheelStates = new Vehicle3DWheelState[WheelLabWheelCount];

    private Physics3DShapeId _wheelLabBumpShape;
    private Physics3DShapeId _wheelLabMovingPlatformShape;
    private Physics3DShapeId _wheelLabStopWallShape;
    private Physics3DShapeId _wheelLabChassisShape;
    private Physics3DShapeId _wheelLabCarrierShape;
    private Physics3DShapeId _wheelLabPhysicalWheelShape;
    private Physics3DShapeId _wheelLabBoxWheelShape;
    private Vehicle3DWorld? _wheelLabVehicles;
    private Vehicle3DVehicleId _wheelLabVehicle;
    private Physics3DBodyId _wheelLabChassis;
    private Vehicle3DWheelKind _wheelLabMode;
    private WheelLabCourseSection _wheelLabSection;
    private int _wheelLabModeBodyCount;
    private int _wheelLabChassisBodyIndex = -1;
    private int _wheelLabMovingPlatformBodyIndex = -1;
    private int _wheelLabGroundedWheelCount;
    private float _wheelLabThrottle;
    private float _wheelLabSteering;
    private float _wheelLabBrake;
    private float _wheelLabSpeedKph;
    private float _wheelLabAverageCompressionCm;
    private float _wheelLabMaximumSlipCmPerSecond;
    private bool _wheelLabNextModeRequested;
    private bool _wheelLabResetRequested;
    private bool _wheelLabHasObservedStep;

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

    internal Physics3DBodyState GetWheelLabChassisState()
    {
        if (_scene != Physics3DShowcaseScene.WheelLab)
        {
            throw new InvalidOperationException("Wheel Lab chassis state is only available while Wheel Lab is active.");
        }

        return RequirePhysics3DChassisState();
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
        _wheelLabRoadShapes[1] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.PotholeEndZCm - config.PotholeStartZCm));
        _wheelLabRoadShapes[2] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.BankStartZCm - config.PotholeEndZCm));
        _wheelLabRoadShapes[3] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.BankEndZCm - config.BankStartZCm));
        _wheelLabRoadShapes[4] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.PlatformGapStartZCm - config.BankEndZCm));
        _wheelLabRoadShapes[5] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.RampStartZCm - config.PlatformGapEndZCm));
        _wheelLabRoadShapes[6] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.RampEndZCm - config.RampStartZCm));
        _wheelLabRoadShapes[7] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.BrakeStartZCm - config.RampEndZCm));
        _wheelLabRoadShapes[8] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.BrakeEndZCm - config.BrakeStartZCm));
        _wheelLabRoadShapes[9] = world.RegisterBoxShape(new Vector3(roadWidth, thickness, config.RoadEndZCm - config.BrakeEndZCm));
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
        _wheelLabCarrierShape = world.RegisterSphereShape(config.CarrierRadiusCm);
        _wheelLabPhysicalWheelShape = world.RegisterSphereShape(config.WheelRadiusCm);
        _wheelLabBoxWheelShape = world.RegisterBoxShape(new Vector3(
            config.WheelWidthCm,
            config.WheelRadiusCm * 2f,
            config.WheelRadiusCm * 2f));
    }

    private void BuildWheelLabScene()
    {
        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        BuildWheelLabCourse(config);
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

        _wheelLabMode = config.InitialWheelKind;
        CreateWheelLabVehicle(config, _wheelLabMode, RequirePhysicsWorld().GetBodyState(_wheelLabChassis));
        _wheelLabSection = WheelLabCourseSection.Start;
        _wheelLabHasObservedStep = false;
        _wheelLabGroundedWheelCount = 0;
        _wheelLabSpeedKph = 0f;
        _wheelLabAverageCompressionCm = 0f;
        _wheelLabMaximumSlipCmPerSecond = 0f;
    }

    private void BuildWheelLabCourse(Physics3DWheelLabShowcaseConfig config)
    {
        AddWheelLabRoadSegment(0, config.RoadStartZCm, config.PotholeStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);
        AddWheelLabRoadSegment(1, config.PotholeStartZCm, config.PotholeEndZCm, -config.PotholeDepthCm, Quaternion.Identity, WheelLabPitColor);
        AddWheelLabRoadSegment(2, config.PotholeEndZCm, config.BankStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);
        AddWheelLabRoadSegment(
            3,
            config.BankStartZCm,
            config.BankEndZCm,
            0f,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, DegreesToRadians(config.BankAngleDegrees)),
            WheelLabBankColor);
        AddWheelLabRoadSegment(4, config.BankEndZCm, config.PlatformGapStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);

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

        AddWheelLabRoadSegment(5, config.PlatformGapEndZCm, config.RampStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);
        float rampAngle = DegreesToRadians(config.RampAngleDegrees);
        float rampLength = config.RampEndZCm - config.RampStartZCm;
        float rampCenterY = (MathF.Sin(rampAngle) * rampLength * 0.5f) -
                            (MathF.Cos(rampAngle) * config.RoadThicknessCm * 0.5f);
        AddWheelLabRoadSegment(
            6,
            config.RampStartZCm,
            config.RampEndZCm,
            rampCenterY + (config.RoadThicknessCm * 0.5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, -rampAngle),
            WheelLabRampColor);
        AddWheelLabRoadSegment(7, config.RampEndZCm, config.BrakeStartZCm, 0f, Quaternion.Identity, WheelLabRoadColor);
        AddWheelLabRoadSegment(8, config.BrakeStartZCm, config.BrakeEndZCm, 0f, Quaternion.Identity, WheelLabBrakeColor);
        AddWheelLabRoadSegment(9, config.BrakeEndZCm, config.RoadEndZCm, 0f, Quaternion.Identity, WheelLabRoadColor);

        for (int i = 0; i < config.BumpCount; i++)
        {
            AddOwnedBody(
                Physics3DBodyKind.Static,
                _wheelLabBumpShape,
                Physics3DShapeKind.Box,
                new Vector3(config.BumpWidthCm, config.BumpHeightCm, config.BumpDepthCm),
                0f,
                new Vector3(0f, config.BumpHeightCm * 0.5f, config.FirstBumpZCm + (i * config.BumpSpacingCm)),
                Quaternion.Identity,
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
            Physics3DBodyId carrier = AddWheelLabModeBody(
                _wheelLabCarrierShape,
                Physics3DShapeKind.Sphere,
                new Vector3(config.CarrierRadiusCm * 2f),
                bodyPosition,
                chassisState.Orientation,
                WheelLabCarrierColor,
                config.CarrierMass);
            Physics3DShapeId wheelShape = mode == Vehicle3DWheelKind.Physical
                ? _wheelLabPhysicalWheelShape
                : _wheelLabBoxWheelShape;
            Physics3DShapeKind wheelShapeKind = mode == Vehicle3DWheelKind.Physical
                ? Physics3DShapeKind.Sphere
                : Physics3DShapeKind.Box;
            Vector3 wheelVisualSize = mode == Vehicle3DWheelKind.Physical
                ? new Vector3(config.WheelRadiusCm * 2f)
                : new Vector3(config.WheelWidthCm, config.WheelRadiusCm * 2f, config.WheelRadiusCm * 2f);
            Physics3DBodyId wheel = AddWheelLabModeBody(
                wheelShape,
                wheelShapeKind,
                wheelVisualSize,
                bodyPosition,
                chassisState.Orientation,
                mode == Vehicle3DWheelKind.Physical ? WheelLabPhysicalWheelColor : WheelLabBoxWheelColor,
                config.WheelMass);
            descriptions[wheelIndex] = CreateWheelLabPhysicalDescription(
                config,
                mode,
                carrier,
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

    private static Vehicle3DWheelDescription CreateWheelLabPhysicalDescription(
        Physics3DWheelLabShowcaseConfig config,
        Vehicle3DWheelKind mode,
        Physics3DBodyId carrier,
        Physics3DBodyId wheel,
        Vector3 mount,
        float steeringScale)
        => Vehicle3DWheelDescription.Physical(
            mode,
            Vehicle3DWheelQueryKind.Raycast,
            carrier,
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
            config.MaximumDriveForce,
            config.MaximumBrakeForce,
            config.MaximumLateralForce,
            config.MaximumWheelAngularSpeedRadiansPerSecond,
            steeringScale,
            1f,
            1f,
            WheelLabGroundQueryLayer,
            CreateWheelLabJointSettings(config));

    private static Vehicle3DWheelJointSettings CreateWheelLabJointSettings(Physics3DWheelLabShowcaseConfig config)
    {
        var alignment = new Physics3DSpringSettings(config.AlignmentSpringAngularFrequency, config.AlignmentSpringTwiceDampingRatio);
        var suspension = new Physics3DSpringSettings(config.JointSuspensionSpringAngularFrequency, config.JointSuspensionSpringTwiceDampingRatio);
        var limit = new Physics3DSpringSettings(config.LimitSpringAngularFrequency, config.LimitSpringTwiceDampingRatio);
        var steering = new Physics3DSpringSettings(config.SteeringSpringAngularFrequency, config.SteeringSpringTwiceDampingRatio);
        var hub = new Physics3DSpringSettings(config.HubSpringAngularFrequency, config.HubSpringTwiceDampingRatio);
        var lineServo = new Physics3DServoSettings(
            config.LineServoMaximumSpeed,
            config.LineServoBaseSpeed,
            config.LineServoMaximumForce);
        var steeringServo = new Physics3DServoSettings(
            config.SteeringServoMaximumSpeed,
            config.SteeringServoBaseSpeed,
            config.SteeringServoMaximumForce);
        var motor = new Physics3DMotorSettings(config.AxleMotorMaximumForce, config.AxleMotorSoftness);
        return new Vehicle3DWheelJointSettings(
            alignment,
            suspension,
            limit,
            steering,
            hub,
            lineServo,
            steeringServo,
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

        AnimateWheelLabPlatform();
        Vehicle3DWorld vehicles = RequireWheelLabVehicles();
        vehicles.SetInput(
            _wheelLabVehicle,
            new Vehicle3DInput(_wheelLabThrottle, _wheelLabBrake, _wheelLabSteering));
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
        UpdateWheelLabCourseSection(chassis.PositionCm.Z);
        if (chassis.PositionCm.Y < ActiveConfig.WheelLab.ResetBelowYCm)
        {
            _wheelLabResetRequested = true;
            _lastAction = "The vehicle left the course. It will return to the start on the next fixed step.";
        }
    }

    private void AnimateWheelLabPlatform()
    {
        if (_wheelLabMovingPlatformBodyIndex < 0)
        {
            throw new InvalidOperationException("Wheel Lab moving platform body is missing.");
        }

        Physics3DWheelLabShowcaseConfig config = ActiveConfig.WheelLab;
        float phase = (_sceneStep + 1f) * config.MovingPlatformRadiansPerStep;
        Vector3 nextPosition = new(
            MathF.Sin(phase) * config.MovingPlatformTravelCm,
            -config.MovingPlatformThicknessCm * 0.5f,
            Midpoint(config.PlatformGapStartZCm, config.PlatformGapEndZCm));
        Quaternion nextOrientation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            MathF.Sin(phase * 0.63f) * config.MovingPlatformMaximumYawRadians);
        Physics3DBodyId body = _bodyIds[_wheelLabMovingPlatformBodyIndex];
        RequirePhysicsWorld().SetKinematicNextPose(body, nextPosition, nextOrientation);
        ref Physics3DPoseCm pose = ref RequireEcsWorld().Get<Physics3DPoseCm>(_bodyEntities[_wheelLabMovingPlatformBodyIndex]);
        pose.Position = nextPosition;
        pose.Orientation = nextOrientation;
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

        Physics3DBodyState chassisState = RequirePhysics3DChassisState();
        DestroyWheelLabVehicleAndModeBodies();
        _wheelLabMode = mode;
        CreateWheelLabVehicle(ActiveConfig.WheelLab, mode, chassisState);
        _wheelLabHasObservedStep = false;
        _lastAction = WheelLabModeMessage(mode);
    }

    private void ResetWheelLabVehicle()
    {
        Vehicle3DWheelKind mode = _wheelLabMode;
        DestroyWheelLabVehicleAndModeBodies();
        Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(_wheelLabChassis);
        state.PositionCm = WheelLabSpawnPosition(ActiveConfig.WheelLab);
        state.Orientation = Quaternion.Identity;
        state.LinearVelocityCmPerSecond = Vector3.Zero;
        state.AngularVelocityRadiansPerSecond = Vector3.Zero;
        state.Awake = true;
        SetBodyStateAndPose(_wheelLabChassisBodyIndex, in state);
        CreateWheelLabVehicle(ActiveConfig.WheelLab, mode, state);
        _wheelLabSection = WheelLabCourseSection.Start;
        _wheelLabHasObservedStep = false;
        _wheelLabGroundedWheelCount = 0;
        _wheelLabSpeedKph = 0f;
        _wheelLabAverageCompressionCm = 0f;
        _wheelLabMaximumSlipCmPerSecond = 0f;
        _lastAction = "Vehicle returned to the start with the selected wheel type unchanged.";
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
        if (_bodyKinds[index] != Physics3DBodyKind.Dynamic)
        {
            throw new InvalidOperationException("Wheel Lab mode bodies must remain dynamic until removal.");
        }

        _dynamicBodyCount--;
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
        if (_wheelLabVehicles is not null)
        {
            ReleaseRegisteredWheelLabVehicle();
        }

        _wheelLabChassis = default;
        _wheelLabMode = default;
        _wheelLabSection = default;
        _wheelLabModeBodyCount = 0;
        _wheelLabChassisBodyIndex = -1;
        _wheelLabMovingPlatformBodyIndex = -1;
        _wheelLabGroundedWheelCount = 0;
        _wheelLabThrottle = 0f;
        _wheelLabSteering = 0f;
        _wheelLabBrake = 0f;
        _wheelLabSpeedKph = 0f;
        _wheelLabAverageCompressionCm = 0f;
        _wheelLabMaximumSlipCmPerSecond = 0f;
        _wheelLabNextModeRequested = false;
        _wheelLabResetRequested = false;
        _wheelLabHasObservedStep = false;
        Array.Clear(_wheelLabModeBodies, 0, _wheelLabModeBodies.Length);
        Array.Clear(_wheelLabWheelIds, 0, _wheelLabWheelIds.Length);
        Array.Clear(_wheelLabWheelStates, 0, _wheelLabWheelStates.Length);
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

        return $"{WheelLabModeName(_wheelLabMode)} · {_wheelLabGroundedWheelCount}/4 grounded · " +
               $"{_wheelLabSpeedKph:0.0} km/h · {_wheelLabAverageCompressionCm:0.0} cm compression · " +
               $"{_wheelLabMaximumSlipCmPerSecond:0} cm/s slip";
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
        else if (positionZCm < config.BrakeEndZCm)
        {
            next = WheelLabCourseSection.Braking;
        }
        else
        {
            next = WheelLabCourseSection.Finish;
        }
        if (next == _wheelLabSection)
        {
            return;
        }

        _wheelLabSection = next;
        _lastAction = next switch
        {
            WheelLabCourseSection.Bumps => "Hold throttle through the yellow speed bumps and watch suspension travel.",
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
