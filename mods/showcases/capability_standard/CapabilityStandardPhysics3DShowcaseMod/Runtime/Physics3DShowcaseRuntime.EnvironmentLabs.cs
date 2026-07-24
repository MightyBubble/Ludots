using System;
using System.Numerics;
using Ludots.Core.Physics3D;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    private const int MaterialHillLaneCount = 3;
    private const int WindTunnelZoneCount = 3;
    private const int WindTunnelBodyCount = WindTunnelZoneCount * 2;

    private static readonly Vector4 MaterialHillCrateColor = new(0.96f, 0.68f, 0.18f, 1f);
    private static readonly Vector4 WindTunnelLightColor = new(0.26f, 0.78f, 0.92f, 1f);
    private static readonly Vector4 WindTunnelHeavyColor = new(0.95f, 0.47f, 0.20f, 1f);

    private readonly Physics3DBodyId[] _materialHillBoxes = new Physics3DBodyId[MaterialHillLaneCount];
    private readonly Vector3[] _materialHillStartPositionsCm = new Vector3[MaterialHillLaneCount];
    private readonly Physics3DBodyId[] _windTunnelLightBodies = new Physics3DBodyId[WindTunnelZoneCount];
    private readonly Physics3DBodyId[] _windTunnelHeavyBodies = new Physics3DBodyId[WindTunnelZoneCount];
    private readonly int[] _windTunnelLightBodyIndices = new int[WindTunnelZoneCount];
    private readonly int[] _windTunnelHeavyBodyIndices = new int[WindTunnelZoneCount];
    private readonly Vector3[] _windTunnelLightStartPositionsCm = new Vector3[WindTunnelZoneCount];
    private readonly Vector3[] _windTunnelHeavyStartPositionsCm = new Vector3[WindTunnelZoneCount];

    private Physics3DShapeId _materialHillRampShape;
    private Physics3DShapeId _materialHillBoxShape;
    private Physics3DShapeId _windTunnelObjectShape;
    private Physics3DForceFieldSet? _windTunnelFields;
    private Physics3DAwakeBodyBuffer? _windTunnelAwakeBodies;
    private Vector3 _materialHillDownhillDirection;
    private bool _materialHillImpulsePending;
    private int _materialHillImpulseSubmissionCount;
    private Physics3DShowcaseChallengeStatus _materialHillStatus;
    private int _materialHillElapsedTicks;
    private int _materialHillStableTicks;
    private Physics3DShowcaseWindZone _windTunnelZone;
    private Physics3DShowcaseDriveDirection _windTunnelDirection;

    internal int MaterialHillImpulseSubmissionCount => _materialHillImpulseSubmissionCount;
    internal Physics3DMaterialHillShowcaseState MaterialHillState
    {
        get
        {
            if (!_isActive || _scene != Physics3DShowcaseScene.MaterialHill)
            {
                return Physics3DMaterialHillShowcaseState.Empty;
            }

            Span<float> travel = stackalloc float[MaterialHillLaneCount];
            Span<int> order = stackalloc int[MaterialHillLaneCount] { 0, 1, 2 };
            for (int laneIndex = 0; laneIndex < MaterialHillLaneCount; laneIndex++)
            {
                travel[laneIndex] = GetMaterialHillTravelCm(laneIndex);
            }

            for (int index = 1; index < order.Length; index++)
            {
                int lane = order[index];
                int destination = index;
                while (destination > 0 && travel[order[destination - 1]] < travel[lane])
                {
                    order[destination] = order[destination - 1];
                    destination--;
                }

                order[destination] = lane;
            }

            Physics3DMaterialHillShowcaseConfig config = ActiveConfig.MaterialHill;
            return new Physics3DMaterialHillShowcaseState(
                Status: _materialHillStatus,
                ElapsedTicks: _materialHillElapsedTicks,
                TicksRemaining: Math.Max(0, config.CompletionTimeLimitTicks - _materialHillElapsedTicks),
                StableTicks: _materialHillStableTicks,
                RequiredStableTicks: config.RequiredStableTicks,
                FirstPlaceLaneIndex: order[0],
                SecondPlaceLaneIndex: order[1],
                ThirdPlaceLaneIndex: order[2],
                FirstPlaceTravelCm: travel[order[0]],
                SecondPlaceTravelCm: travel[order[1]],
                ThirdPlaceTravelCm: travel[order[2]]);
        }
    }
    internal int WindTunnelFieldCount => _windTunnelFields?.Count ?? 0;
    internal Physics3DShowcaseWindZone WindTunnelZone => _windTunnelZone;
    internal Physics3DShowcaseDriveDirection WindTunnelDirection => _windTunnelDirection;

    private void EnsureEnvironmentLabStorage(Physics3DShowcaseConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Physics3DWindTunnelShowcaseConfig wind = config.WindTunnel;
        wind.Validate(nameof(config.WindTunnel));

        if (_windTunnelFields == null ||
            _windTunnelFields.Capacity != wind.FieldCapacity ||
            _windTunnelFields.AwakeBodyCapacity != wind.AwakeBodyCapacity)
        {
            _windTunnelFields = new Physics3DForceFieldSet(wind.FieldCapacity, wind.AwakeBodyCapacity);
        }

        if (_windTunnelAwakeBodies == null || _windTunnelAwakeBodies.Capacity != wind.AwakeBodyCapacity)
        {
            _windTunnelAwakeBodies = new Physics3DAwakeBodyBuffer(wind.AwakeBodyCapacity);
        }
    }

    private void RegisterEnvironmentLabShapes(Physics3DShowcaseConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Physics3DMaterialHillShowcaseConfig hill = config.MaterialHill;
        Physics3DWindTunnelShowcaseConfig wind = config.WindTunnel;
        hill.Validate(nameof(config.MaterialHill));
        wind.Validate(nameof(config.WindTunnel));

        IPhysics3DWorld world = RequirePhysicsWorld();
        _materialHillRampShape = world.RegisterBoxShape(new Vector3(
            hill.RampWidthCm,
            hill.RampThicknessCm,
            hill.RampLengthCm));
        _materialHillBoxShape = world.RegisterBoxShape(new Vector3(hill.BoxSizeCm));
        _windTunnelObjectShape = world.RegisterSphereShape(wind.ObjectRadiusCm);
    }

    private void BuildMaterialHillScene()
    {
        Physics3DMaterialHillShowcaseConfig config = ActiveConfig.MaterialHill;
        config.Validate(nameof(ActiveConfig.MaterialHill));
        RequireRegisteredEnvironmentShape(_materialHillRampShape, "Material Hill ramp");
        RequireRegisteredEnvironmentShape(_materialHillBoxShape, "Material Hill box");
        RequireOwnedEnvironmentBodyCapacity(1 + (MaterialHillLaneCount * 2), "Material Hill");

        AddFloor();
        float angleRadians = config.RampAngleDegrees * (MathF.PI / 180f);
        Quaternion rampOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, angleRadians);
        _materialHillDownhillDirection = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, rampOrientation));
        float rampCenterYCm = config.RampBaseYCm +
            (0.5f * config.RampLengthCm * MathF.Sin(angleRadians)) +
            (0.5f * config.RampThicknessCm * MathF.Cos(angleRadians));

        for (int laneIndex = 0; laneIndex < MaterialHillLaneCount; laneIndex++)
        {
            Physics3DMaterialHillLaneShowcaseConfig lane = config.Lanes[laneIndex];
            Physics3DMaterial material = CreateEnvironmentMaterial(lane.FrictionCoefficient);
            Vector3 rampCenter = new(lane.CenterXCm, rampCenterYCm, config.RampCenterZCm);
            Vector4 laneColor = new(lane.ColorR, lane.ColorG, lane.ColorB, 1f);
            AddOwnedBody(
                Physics3DBodyKind.Static,
                _materialHillRampShape,
                Physics3DShapeKind.Box,
                new Vector3(config.RampWidthCm, config.RampThicknessCm, config.RampLengthCm),
                0f,
                rampCenter,
                rampOrientation,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Discrete,
                laneColor,
                material: material);

            Vector3 boxPosition = rampCenter + Vector3.Transform(
                new Vector3(
                    0f,
                    0.5f * (config.RampThicknessCm + config.BoxSizeCm) + 1f,
                    (-0.5f * config.RampLengthCm) + config.BoxSizeCm),
                rampOrientation);
            _materialHillBoxes[laneIndex] = AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _materialHillBoxShape,
                Physics3DShapeKind.Box,
                new Vector3(config.BoxSizeCm),
                0f,
                boxPosition,
                rampOrientation,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                MaterialHillCrateColor,
                mass: config.BoxMass,
                material: material);
            RequirePhysicsWorld().SetBodyAwake(_materialHillBoxes[laneIndex], false);
            _materialHillStartPositionsCm[laneIndex] = boxPosition;
        }

        _materialHillImpulsePending = false;
        _materialHillImpulseSubmissionCount = 0;
        _materialHillStatus = Physics3DShowcaseChallengeStatus.Ready;
        _materialHillElapsedTicks = 0;
        _materialHillStableTicks = 0;
    }

    private void PrepareMaterialHillStep()
    {
        if (!_materialHillImpulsePending)
        {
            if (_materialHillStatus == Physics3DShowcaseChallengeStatus.Running)
            {
                ApplyMaterialHillSettlingResistance();
            }
            return;
        }

        IPhysics3DWorld world = RequirePhysicsWorld();
        int remainingCommands = world.ActuationCommandCapacity - world.PendingActuationCommandCount;
        if (remainingCommands < MaterialHillLaneCount)
        {
            throw new Physics3DCapacityExceededException("actuation commands", world.ActuationCommandCapacity);
        }

        for (int laneIndex = 0; laneIndex < MaterialHillLaneCount; laneIndex++)
        {
            Physics3DBodyId body = _materialHillBoxes[laneIndex];
            if (!body.IsValid || !world.ContainsBody(body) || world.GetBodyKind(body) != Physics3DBodyKind.Dynamic)
            {
                throw new InvalidOperationException($"Material Hill lost dynamic lane body {laneIndex} before its launch impulse.");
            }
        }

        Vector3 impulse = _materialHillDownhillDirection * ActiveConfig.MaterialHill.PushImpulseMassCmPerSecond;
        for (int laneIndex = 0; laneIndex < MaterialHillLaneCount; laneIndex++)
        {
            world.EnqueueLinearImpulse(_materialHillBoxes[laneIndex], impulse);
        }

        _materialHillImpulsePending = false;
        _materialHillImpulseSubmissionCount = MaterialHillLaneCount;
        _materialHillStatus = Physics3DShowcaseChallengeStatus.Running;
        _materialHillElapsedTicks = 0;
        _materialHillStableTicks = 0;
        _lastAction = $"Pushed {MaterialHillLaneCount} identical crates with the same impulse; compare their stopping distance.";
    }

    private void ApplyMaterialHillSettlingResistance()
    {
        IPhysics3DWorld world = RequirePhysicsWorld();
        int requiredCommands = MaterialHillLaneCount * 2;
        if (world.ActuationCommandCapacity - world.PendingActuationCommandCount < requiredCommands)
        {
            throw new Physics3DCapacityExceededException(
                "Material Hill settling resistance commands",
                world.ActuationCommandCapacity);
        }

        Physics3DMaterialHillShowcaseConfig config = ActiveConfig.MaterialHill;
        for (int laneIndex = 0; laneIndex < MaterialHillLaneCount; laneIndex++)
        {
            Physics3DBodyId body = _materialHillBoxes[laneIndex];
            Physics3DBodyState state = RequireEnvironmentBodyState(body, "Material Hill", laneIndex);
            float travelCm = MathF.Max(0f, Vector3.Dot(
                state.PositionCm - _materialHillStartPositionsCm[laneIndex],
                _materialHillDownhillDirection));
            if (travelCm < config.SettlingStartTravelCm)
            {
                continue;
            }

            Vector3 horizontalVelocity = state.LinearVelocityCmPerSecond;
            horizontalVelocity.Y = 0f;
            world.EnqueueAcceleration(body, -horizontalVelocity * config.SettlingLinearDragPerSecond);
            world.EnqueueTorque(
                body,
                -state.AngularVelocityRadiansPerSecond * config.SettlingAngularTorquePerAngularSpeed);
        }
    }

    private void ObserveMaterialHillStep()
    {
        if (_materialHillStatus != Physics3DShowcaseChallengeStatus.Running)
        {
            return;
        }

        Physics3DMaterialHillShowcaseConfig config = ActiveConfig.MaterialHill;
        float maximumLinearSpeedSquared =
            config.StableMaximumLinearSpeedCmPerSecond * config.StableMaximumLinearSpeedCmPerSecond;
        float maximumAngularSpeedSquared =
            config.StableMaximumAngularSpeedRadiansPerSecond * config.StableMaximumAngularSpeedRadiansPerSecond;
        bool allStable = true;
        for (int laneIndex = 0; laneIndex < MaterialHillLaneCount; laneIndex++)
        {
            Physics3DBodyState state = RequireEnvironmentBodyState(
                _materialHillBoxes[laneIndex],
                "Material Hill",
                laneIndex);
            allStable &= state.LinearVelocityCmPerSecond.LengthSquared() <= maximumLinearSpeedSquared &&
                         state.AngularVelocityRadiansPerSecond.LengthSquared() <= maximumAngularSpeedSquared;
        }

        _materialHillElapsedTicks++;
        _materialHillStableTicks = allStable ? _materialHillStableTicks + 1 : 0;
        if (_materialHillStableTicks >= config.RequiredStableTicks)
        {
            _materialHillStatus = Physics3DShowcaseChallengeStatus.Complete;
            Physics3DMaterialHillShowcaseState result = MaterialHillState;
            Physics3DMaterialHillLaneShowcaseConfig winner = config.Lanes[result.FirstPlaceLaneIndex];
            _lastAction =
                $"Material Hill complete. {winner.Name} won by {result.WinningMarginCm:0} cm; Reset makes all three lanes playable again.";
            return;
        }

        if (_materialHillElapsedTicks >= config.CompletionTimeLimitTicks)
        {
            _materialHillStatus = Physics3DShowcaseChallengeStatus.Failed;
            _lastAction =
                $"Material Hill timed out before all three crates stayed stable for {config.RequiredStableTicks} ticks. Press Reset to retry.";
        }
    }

    private void BuildWindTunnelScene()
    {
        Physics3DWindTunnelShowcaseConfig config = ActiveConfig.WindTunnel;
        config.Validate(nameof(ActiveConfig.WindTunnel));
        RequireRegisteredEnvironmentShape(_windTunnelObjectShape, "Wind Tunnel object");
        RequireOwnedEnvironmentBodyCapacity(1 + WindTunnelBodyCount, "Wind Tunnel");
        _ = _windTunnelFields
            ?? throw new InvalidOperationException("Wind Tunnel force-field storage was not initialized.");
        _ = _windTunnelAwakeBodies
            ?? throw new InvalidOperationException("Wind Tunnel awake-body storage was not initialized.");

        float pairHalfSpacing = config.ObjectPairSpacingZCm * 0.5f;
        if (pairHalfSpacing + config.ObjectRadiusCm >= config.ZoneDepthCm * 0.5f)
        {
            throw new InvalidOperationException("Wind Tunnel comparison bodies must begin fully inside each configured zone.");
        }

        if (pairHalfSpacing + config.ObjectRadiusCm >= config.VortexRadiusCm)
        {
            throw new InvalidOperationException("Wind Tunnel comparison bodies must begin inside the vortex radius.");
        }

        AddFloor();
        _windTunnelZone = config.InitialZone;
        _windTunnelDirection = config.InitialDirection;
        RebuildWindTunnelFields();
        Vector3 steadyCenter = new(config.SteadyCenterXCm, config.ZoneCenterYCm, 0f);
        Vector3 gustCenter = new(config.GustCenterXCm, config.ZoneCenterYCm, 0f);
        Vector3 vortexCenter = new(config.VortexCenterXCm, config.ZoneCenterYCm, 0f);

        CreateWindTunnelPair(0, steadyCenter, pairHalfSpacing, config);
        CreateWindTunnelPair(1, gustCenter, pairHalfSpacing, config);
        CreateWindTunnelPair(2, vortexCenter, pairHalfSpacing, config);
    }

    private void PrepareWindTunnelStep()
    {
        Physics3DForceFieldSet fields = _windTunnelFields
            ?? throw new InvalidOperationException("Wind Tunnel force-field storage is unavailable.");
        Physics3DAwakeBodyBuffer awakeBodies = _windTunnelAwakeBodies
            ?? throw new InvalidOperationException("Wind Tunnel awake-body storage is unavailable.");
        IPhysics3DWorld world = RequirePhysicsWorld();
        world.CopyAwakeBodies(awakeBodies);
        fields.Apply(awakeBodies, world);
    }

    private void ReleaseEnvironmentLabScene()
    {
        _materialHillImpulsePending = false;
        _materialHillImpulseSubmissionCount = 0;
        _materialHillStatus = Physics3DShowcaseChallengeStatus.Ready;
        _materialHillElapsedTicks = 0;
        _materialHillStableTicks = 0;
        _materialHillDownhillDirection = default;
        Array.Clear(_materialHillBoxes);
        Array.Clear(_materialHillStartPositionsCm);
        Array.Clear(_windTunnelLightBodies);
        Array.Clear(_windTunnelHeavyBodies);
        Array.Clear(_windTunnelLightStartPositionsCm);
        Array.Clear(_windTunnelHeavyStartPositionsCm);
        _windTunnelFields?.Clear();
        _windTunnelZone = default;
        _windTunnelDirection = Physics3DShowcaseDriveDirection.Forward;
    }

    private string CreateMaterialHillSummary()
    {
        if (_scene != Physics3DShowcaseScene.MaterialHill)
        {
            return "Visit Material Hill to compare the three surfaces.";
        }

        Physics3DMaterialHillShowcaseState state = MaterialHillState;
        Physics3DMaterialHillLaneShowcaseConfig[] lanes = ActiveConfig.MaterialHill.Lanes;
        string status = ChallengeStatusLabel(state.Status);
        return $"{status} | 1 {lanes[state.FirstPlaceLaneIndex].Name} slid {state.FirstPlaceTravelCm:0} cm | " +
               $"2 {lanes[state.SecondPlaceLaneIndex].Name} slid {state.SecondPlaceTravelCm:0} cm " +
               $"(-{state.FirstPlaceTravelCm - state.SecondPlaceTravelCm:0}) | " +
               $"3 {lanes[state.ThirdPlaceLaneIndex].Name} slid {state.ThirdPlaceTravelCm:0} cm " +
               $"(-{state.FirstPlaceTravelCm - state.ThirdPlaceTravelCm:0})";
    }

    private string CreateWindTunnelSummary()
    {
        if (_scene != Physics3DShowcaseScene.WindTunnel)
        {
            return "Visit Wind Tunnel to compare light and heavy objects.";
        }

        GetWindTunnelTravelCm((int)_windTunnelZone, out float lightTravel, out float heavyTravel);
        return $"{WindZoneLabel(_windTunnelZone)} | {DriveDirectionLabel(_windTunnelDirection)} | " +
               $"light {lightTravel:0} cm / heavy {heavyTravel:0} cm";
    }

    internal static string ChallengeStatusLabel(Physics3DShowcaseChallengeStatus status) => status switch
    {
        Physics3DShowcaseChallengeStatus.Ready => "READY",
        Physics3DShowcaseChallengeStatus.Running => "RUNNING",
        Physics3DShowcaseChallengeStatus.Complete => "COMPLETE",
        Physics3DShowcaseChallengeStatus.Failed => "FAILED",
        _ => throw new InvalidOperationException($"Unknown showcase challenge status '{status}'.")
    };

    internal bool TryGetMaterialHillLaneState(
        int laneIndex,
        out Physics3DBodyState state,
        out float frictionCoefficient,
        out float travelCm)
    {
        if ((uint)laneIndex >= MaterialHillLaneCount)
        {
            state = default;
            frictionCoefficient = 0f;
            travelCm = 0f;
            return false;
        }

        Physics3DBodyId body = _materialHillBoxes[laneIndex];
        if (!body.IsValid)
        {
            state = default;
            frictionCoefficient = 0f;
            travelCm = 0f;
            return false;
        }

        state = RequireEnvironmentBodyState(body, "Material Hill", laneIndex);
        frictionCoefficient = ActiveConfig.MaterialHill.Lanes[laneIndex].FrictionCoefficient;
        travelCm = MathF.Max(0f, Vector3.Dot(
            state.PositionCm - _materialHillStartPositionsCm[laneIndex],
            _materialHillDownhillDirection));
        return true;
    }

    internal bool TryGetWindTunnelPairState(
        int zoneIndex,
        out Physics3DBodyState lightState,
        out Physics3DBodyState heavyState,
        out float lightTravelCm,
        out float heavyTravelCm)
    {
        if ((uint)zoneIndex >= WindTunnelZoneCount)
        {
            lightState = default;
            heavyState = default;
            lightTravelCm = 0f;
            heavyTravelCm = 0f;
            return false;
        }

        Physics3DBodyId lightBody = _windTunnelLightBodies[zoneIndex];
        Physics3DBodyId heavyBody = _windTunnelHeavyBodies[zoneIndex];
        if (!lightBody.IsValid || !heavyBody.IsValid)
        {
            lightState = default;
            heavyState = default;
            lightTravelCm = 0f;
            heavyTravelCm = 0f;
            return false;
        }

        lightState = RequireEnvironmentBodyState(lightBody, "Wind Tunnel light", zoneIndex);
        heavyState = RequireEnvironmentBodyState(heavyBody, "Wind Tunnel heavy", zoneIndex);
        lightTravelCm = Vector3.Distance(lightState.PositionCm, _windTunnelLightStartPositionsCm[zoneIndex]);
        heavyTravelCm = Vector3.Distance(heavyState.PositionCm, _windTunnelHeavyStartPositionsCm[zoneIndex]);
        return true;
    }

    private void CreateWindTunnelPair(
        int zoneIndex,
        Vector3 center,
        float pairHalfSpacing,
        Physics3DWindTunnelShowcaseConfig config)
    {
        Vector3 lightPosition = center + (Vector3.UnitZ * pairHalfSpacing);
        Vector3 heavyPosition = center - (Vector3.UnitZ * pairHalfSpacing);
        Vector3 visualSize = new(config.ObjectRadiusCm * 2f);
        _windTunnelLightBodyIndices[zoneIndex] = _bodyCount;
        _windTunnelLightBodies[zoneIndex] = AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            _windTunnelObjectShape,
            Physics3DShapeKind.Sphere,
            visualSize,
            0f,
            lightPosition,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            WindTunnelLightColor,
            mass: config.LightMass);
        _windTunnelHeavyBodyIndices[zoneIndex] = _bodyCount;
        _windTunnelHeavyBodies[zoneIndex] = AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            _windTunnelObjectShape,
            Physics3DShapeKind.Sphere,
            visualSize,
            0f,
            heavyPosition,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            WindTunnelHeavyColor,
            mass: config.HeavyMass);
        _windTunnelLightStartPositionsCm[zoneIndex] = lightPosition;
        _windTunnelHeavyStartPositionsCm[zoneIndex] = heavyPosition;
    }

    private void RebuildWindTunnelFields()
    {
        Physics3DWindTunnelShowcaseConfig config = ActiveConfig.WindTunnel;
        Physics3DForceFieldSet fields = _windTunnelFields
            ?? throw new InvalidOperationException("Wind Tunnel force-field storage is unavailable.");
        float direction = (float)_windTunnelDirection;
        Vector3 zoneSize = new(config.ZoneWidthCm, config.ZoneHeightCm, config.ZoneDepthCm);
        Vector3 steadyCenter = new(config.SteadyCenterXCm, config.ZoneCenterYCm, 0f);
        Vector3 gustCenter = new(config.GustCenterXCm, config.ZoneCenterYCm, 0f);
        Vector3 vortexCenter = new(config.VortexCenterXCm, config.ZoneCenterYCm, 0f);
        fields.Clear();
        fields.Add(new Physics3DBoxWindField(
            steadyCenter,
            zoneSize,
            Quaternion.Identity,
            Vector3.UnitZ * (config.SteadySpeedCmPerSecond * direction),
            config.ForcePerRelativeSpeed));
        fields.Add(new Physics3DBoxGustField(
            gustCenter,
            zoneSize,
            Quaternion.Identity,
            Vector3.UnitZ * (config.GustBaseSpeedCmPerSecond * direction),
            Vector3.UnitZ * (config.GustPeakSpeedCmPerSecond * direction),
            config.ForcePerRelativeSpeed,
            config.GustAttackTicks,
            config.GustHoldTicks,
            config.GustReleaseTicks,
            config.GustCalmTicks));
        fields.Add(new Physics3DVortexWindField(
            vortexCenter,
            config.VortexRadiusCm,
            Vector3.UnitY,
            config.VortexTangentialSpeedCmPerSecond * direction,
            config.VortexAxialSpeedCmPerSecond * direction,
            config.ForcePerRelativeSpeed,
            linearFalloff: true));
    }

    private void SetWindTunnelZone(int value)
    {
        RequireWindTunnelCommand(nameof(Physics3DShowcaseCommandKind.SetWindZone));
        if ((uint)value > byte.MaxValue || !Enum.IsDefined(typeof(Physics3DShowcaseWindZone), (byte)value))
        {
            throw new InvalidOperationException($"Unknown Wind Tunnel zone value {value}.");
        }

        _windTunnelZone = (Physics3DShowcaseWindZone)value;
        _lastAction = $"Selected {WindZoneLabel(_windTunnelZone)}. Relaunch the pair to compare from the same start.";
    }

    private void ReverseWindTunnelDirection()
    {
        RequireWindTunnelCommand(nameof(Physics3DShowcaseCommandKind.ReverseWindDirection));
        _windTunnelDirection = _windTunnelDirection == Physics3DShowcaseDriveDirection.Forward
            ? Physics3DShowcaseDriveDirection.Reverse
            : Physics3DShowcaseDriveDirection.Forward;
        RebuildWindTunnelFields();
        _lastAction = $"All formal wind fields now run {DriveDirectionLabel(_windTunnelDirection)}. " +
            "Relaunch the selected pair for a clean comparison.";
    }

    private void RelaunchSelectedWindTunnelPair()
    {
        RequireWindTunnelCommand(nameof(Physics3DShowcaseCommandKind.RelaunchWindPair));
        int zoneIndex = (int)_windTunnelZone;
        ResetWindTunnelBody(
            _windTunnelLightBodyIndices[zoneIndex],
            _windTunnelLightBodies[zoneIndex],
            _windTunnelLightStartPositionsCm[zoneIndex],
            "light");
        ResetWindTunnelBody(
            _windTunnelHeavyBodyIndices[zoneIndex],
            _windTunnelHeavyBodies[zoneIndex],
            _windTunnelHeavyStartPositionsCm[zoneIndex],
            "heavy");
        _lastAction = $"Relaunched the {WindZoneLabel(_windTunnelZone)} light and heavy pair with zero initial velocity.";
    }

    private void ResetWindTunnelBody(int bodyIndex, Physics3DBodyId body, Vector3 positionCm, string role)
    {
        IPhysics3DWorld world = RequirePhysicsWorld();
        if (!body.IsValid || !world.ContainsBody(body) || world.GetBodyKind(body) != Physics3DBodyKind.Dynamic)
        {
            throw new InvalidOperationException($"Wind Tunnel lost its selected {role} comparison body.");
        }

        var state = new Physics3DBodyState
        {
            PositionCm = positionCm,
            Orientation = Quaternion.Identity,
            LinearVelocityCmPerSecond = Vector3.Zero,
            AngularVelocityRadiansPerSecond = Vector3.Zero,
            Awake = true
        };
        SetBodyStateAndPose(bodyIndex, in state);
        world.SetBodyAwake(body, true);
    }

    internal bool TryGetWindTunnelZoneVisual(
        out Vector3 centerCm,
        out Vector3 sizeCm,
        out Vector3 direction)
    {
        if (_scene != Physics3DShowcaseScene.WindTunnel)
        {
            centerCm = default;
            sizeCm = default;
            direction = default;
            return false;
        }

        Physics3DWindTunnelShowcaseConfig config = ActiveConfig.WindTunnel;
        centerCm = new Vector3(_windTunnelZone switch
        {
            Physics3DShowcaseWindZone.Steady => config.SteadyCenterXCm,
            Physics3DShowcaseWindZone.Gust => config.GustCenterXCm,
            Physics3DShowcaseWindZone.Vortex => config.VortexCenterXCm,
            _ => throw new InvalidOperationException($"Unknown Wind Tunnel zone '{_windTunnelZone}'.")
        }, config.ZoneCenterYCm, 0f);
        sizeCm = new Vector3(config.ZoneWidthCm, config.ZoneHeightCm, config.ZoneDepthCm);
        direction = Vector3.UnitZ * (float)_windTunnelDirection;
        return true;
    }

    private void RequireWindTunnelCommand(string commandName)
    {
        if (_scene != Physics3DShowcaseScene.WindTunnel)
        {
            throw new InvalidOperationException($"{commandName} requires the active Wind Tunnel station.");
        }
    }

    internal static string WindZoneLabel(Physics3DShowcaseWindZone zone) => zone switch
    {
        Physics3DShowcaseWindZone.Steady => "Steady",
        Physics3DShowcaseWindZone.Gust => "Gust",
        Physics3DShowcaseWindZone.Vortex => "Vortex",
        _ => throw new InvalidOperationException($"Unknown Wind Tunnel zone '{zone}'.")
    };

    internal static string DriveDirectionLabel(Physics3DShowcaseDriveDirection direction) => direction switch
    {
        Physics3DShowcaseDriveDirection.Forward => "FORWARD",
        Physics3DShowcaseDriveDirection.Reverse => "REVERSE",
        _ => throw new InvalidOperationException($"Unknown showcase drive direction '{direction}'.")
    };

    private float GetMaterialHillTravelCm(int laneIndex)
    {
        if (!TryGetMaterialHillLaneState(laneIndex, out _, out _, out float travelCm))
        {
            throw new InvalidOperationException($"Material Hill lane {laneIndex} is unavailable for its player summary.");
        }

        return travelCm;
    }

    private void GetWindTunnelTravelCm(int zoneIndex, out float lightTravelCm, out float heavyTravelCm)
    {
        if (!TryGetWindTunnelPairState(zoneIndex, out _, out _, out lightTravelCm, out heavyTravelCm))
        {
            throw new InvalidOperationException($"Wind Tunnel zone {zoneIndex} is unavailable for its player summary.");
        }
    }

    private Physics3DBodyState RequireEnvironmentBodyState(Physics3DBodyId body, string exhibit, int index)
    {
        IPhysics3DWorld world = RequirePhysicsWorld();
        if (!world.ContainsBody(body))
        {
            throw new InvalidOperationException($"{exhibit} lost comparison body {index}.");
        }

        return world.GetBodyState(body);
    }

    private Physics3DMaterial CreateEnvironmentMaterial(float frictionCoefficient)
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        return new Physics3DMaterial(
            frictionCoefficient,
            config.MaximumRecoveryVelocityCmPerSecond,
            config.SpringAngularFrequency,
            config.SpringTwiceDampingRatio);
    }

    private void RequireOwnedEnvironmentBodyCapacity(int requiredBodies, string exhibit)
    {
        int remaining = _bodyIds.Length - _bodyCount;
        if (remaining < requiredBodies)
        {
            throw new InvalidOperationException(
                $"{exhibit} requires {requiredBodies} owned body slots, but only {remaining} remain.");
        }
    }

    private static void RequireRegisteredEnvironmentShape(Physics3DShapeId shape, string name)
    {
        if (!shape.IsValid)
        {
            throw new InvalidOperationException($"{name} shape was not registered before scene construction.");
        }
    }
}
