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

    internal int MaterialHillImpulseSubmissionCount => _materialHillImpulseSubmissionCount;
    internal int WindTunnelFieldCount => _windTunnelFields?.Count ?? 0;

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
            _materialHillStartPositionsCm[laneIndex] = boxPosition;
        }

        _materialHillImpulsePending = true;
        _materialHillImpulseSubmissionCount = 0;
    }

    private void PrepareMaterialHillStep()
    {
        if (!_materialHillImpulsePending)
        {
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
    }

    private void BuildWindTunnelScene()
    {
        Physics3DWindTunnelShowcaseConfig config = ActiveConfig.WindTunnel;
        config.Validate(nameof(ActiveConfig.WindTunnel));
        RequireRegisteredEnvironmentShape(_windTunnelObjectShape, "Wind Tunnel object");
        RequireOwnedEnvironmentBodyCapacity(1 + WindTunnelBodyCount, "Wind Tunnel");
        Physics3DForceFieldSet fields = _windTunnelFields
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
        fields.Clear();
        Vector3 zoneSize = new(config.ZoneWidthCm, config.ZoneHeightCm, config.ZoneDepthCm);
        Vector3 steadyCenter = new(config.SteadyCenterXCm, config.ZoneCenterYCm, 0f);
        Vector3 gustCenter = new(config.GustCenterXCm, config.ZoneCenterYCm, 0f);
        Vector3 vortexCenter = new(config.VortexCenterXCm, config.ZoneCenterYCm, 0f);
        fields.Add(new Physics3DBoxWindField(
            steadyCenter,
            zoneSize,
            Quaternion.Identity,
            Vector3.UnitZ * config.SteadySpeedCmPerSecond,
            config.ForcePerRelativeSpeed));
        fields.Add(new Physics3DBoxGustField(
            gustCenter,
            zoneSize,
            Quaternion.Identity,
            Vector3.UnitZ * config.GustBaseSpeedCmPerSecond,
            Vector3.UnitZ * config.GustPeakSpeedCmPerSecond,
            config.ForcePerRelativeSpeed,
            config.GustAttackTicks,
            config.GustHoldTicks,
            config.GustReleaseTicks,
            config.GustCalmTicks));
        fields.Add(new Physics3DVortexWindField(
            vortexCenter,
            config.VortexRadiusCm,
            Vector3.UnitY,
            config.VortexTangentialSpeedCmPerSecond,
            config.VortexAxialSpeedCmPerSecond,
            config.ForcePerRelativeSpeed,
            linearFalloff: true));

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
        _materialHillDownhillDirection = default;
        Array.Clear(_materialHillBoxes);
        Array.Clear(_materialHillStartPositionsCm);
        Array.Clear(_windTunnelLightBodies);
        Array.Clear(_windTunnelHeavyBodies);
        Array.Clear(_windTunnelLightStartPositionsCm);
        Array.Clear(_windTunnelHeavyStartPositionsCm);
        _windTunnelFields?.Clear();
    }

    private string CreateMaterialHillSummary()
    {
        if (_scene != Physics3DShowcaseScene.MaterialHill)
        {
            return "Visit Material Hill to compare the three surfaces.";
        }

        Physics3DMaterialHillLaneShowcaseConfig[] lanes = ActiveConfig.MaterialHill.Lanes;
        float first = GetMaterialHillTravelCm(0);
        float second = GetMaterialHillTravelCm(1);
        float third = GetMaterialHillTravelCm(2);
        return $"{lanes[0].Name} slid {first:0} cm | {lanes[1].Name} slid {second:0} cm | {lanes[2].Name} slid {third:0} cm";
    }

    private string CreateWindTunnelSummary()
    {
        if (_scene != Physics3DShowcaseScene.WindTunnel)
        {
            return "Visit Wind Tunnel to compare light and heavy objects.";
        }

        GetWindTunnelTravelCm(0, out float steadyLight, out float steadyHeavy);
        GetWindTunnelTravelCm(1, out float gustLight, out float gustHeavy);
        GetWindTunnelTravelCm(2, out float vortexLight, out float vortexHeavy);
        return $"Steady: light {steadyLight:0} cm / heavy {steadyHeavy:0} cm | " +
               $"Gust: light {gustLight:0} cm / heavy {gustHeavy:0} cm | " +
               $"Vortex: light {vortexLight:0} cm / heavy {vortexHeavy:0} cm";
    }

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
