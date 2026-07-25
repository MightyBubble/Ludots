using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    private static readonly Vector4 FloorColor = new(0.16f, 0.20f, 0.25f, 1f);
    private static readonly Vector4 StaticColor = new(0.40f, 0.45f, 0.52f, 1f);
    private static readonly Vector4 DynamicBlue = new(0.20f, 0.60f, 0.95f, 1f);
    private static readonly Vector4 DynamicGold = new(0.96f, 0.68f, 0.18f, 1f);
    private static readonly Vector4 DynamicGreen = new(0.25f, 0.78f, 0.48f, 1f);
    private static readonly Vector4 DynamicRed = new(0.94f, 0.28f, 0.25f, 1f);
    private static readonly Vector4 KinematicColor = new(0.70f, 0.38f, 0.96f, 1f);

    private void BuildSelectedScene()
    {
        ClearOwnedScene();
        ResetSceneDiagnostics();
        switch (_scene)
        {
            case Physics3DShowcaseScene.ScannerRange:
                BuildScannerRangeScene();
                break;
            case Physics3DShowcaseScene.MaterialHill:
                BuildMaterialHillScene();
                break;
            case Physics3DShowcaseScene.PlatformStation:
                BuildPlatformStationScene();
                break;
            case Physics3DShowcaseScene.WindTunnel:
                BuildWindTunnelScene();
                break;
            case Physics3DShowcaseScene.TraversalCourse:
                BuildTraversalCourseScene();
                break;
            case Physics3DShowcaseScene.WheelLab:
                BuildWheelLabScene();
                break;
            case Physics3DShowcaseScene.RagdollLab:
                BuildRagdollLabScene(ActiveConfig.RagdollLab);
                break;
            case Physics3DShowcaseScene.ConstraintForge:
                BuildConstraintForgeScene();
                break;
            case Physics3DShowcaseScene.ReplayTheater:
                BuildReplayTheaterScene();
                break;
            case Physics3DShowcaseScene.ScaleCity:
                BuildScaleCityScene(_benchmarkBodies);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Physics3D showcase scene '{_scene}'.");
        }

        _sceneRevision++;
        _lastAction = $"Loaded {SceneTitle(_scene)}. {SceneDescription(_scene)}";
    }

    private void ResetSceneDiagnostics()
    {
        _sceneStep = 0;
        ResetScaleCityPerformanceWindow();
        _replayCursor = 0;
        _replayExpectedHash = 0;
        _replayActualHash = 0;
        _replayDifferenceRequested = false;
        _replayDifferenceInjected = false;
        _replayStatus = Physics3DShowcaseReplayStatus.NotRunning;
        Array.Clear(_queryHitCounts, 0, _queryHitCounts.Length);
        Array.Clear(_queryHasFirstHit, 0, _queryHasFirstHit.Length);
        Array.Clear(_queryFirstHitPositionsCm, 0, _queryFirstHitPositionsCm.Length);
        Array.Clear(_queryOriginsCm, 0, _queryOriginsCm.Length);
        Array.Clear(_queryDirections, 0, _queryDirections.Length);
        Array.Clear(_querySizesCm, 0, _querySizesCm.Length);
        Array.Clear(_queryDistancesCm, 0, _queryDistancesCm.Length);
        Array.Clear(_queryHitPositionsCm, 0, _queryHitPositionsCm.Length);
        Array.Clear(_queryHitNormals, 0, _queryHitNormals.Length);
        Array.Clear(_queryHitDistancesCm, 0, _queryHitDistancesCm.Length);
        Array.Clear(_queryHitStartedOverlapping, 0, _queryHitStartedOverlapping.Length);
        _scannerHasResult = false;
        _scannerQueryFailed = false;
        _scannerRunSequence = 0;
        _scannerPlaybackTick = 0;
        _scannerVisibleHitCount = 0;
        _scannerPlaybackDistanceCm = 0f;
        _scannerPlaybackStatus = Physics3DScannerPlaybackStatus.Waiting;
    }

    private void BuildScannerRangeScene()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        Physics3DScannerRangeShowcaseConfig scanner = config.ScannerRange;
        scanner.Validate(nameof(config.ScannerRange));
        _scannerQueryKind = scanner.InitialQueryKind;
        _scannerDistancePresetIndex = scanner.InitialDistancePresetIndex;
        _scannerLayerFilterIndex = scanner.InitialLayerFilterIndex;
        AddFloor();
        float size = config.BodySizeCm;
        for (int lane = 0; lane < QueryKindCount; lane++)
        {
            float z = scanner.FirstLaneZCm + (lane * scanner.LaneSpacingCm);
            _queryOriginsCm[lane] = lane < 4
                ? new Vector3(scanner.CastOriginXCm, scanner.OriginYCm, z)
                : new Vector3(scanner.OverlapOriginXCm, scanner.OriginYCm, z);
            if (lane == (int)Physics3DShowcaseQueryKind.CapsuleCast - 1)
            {
                _queryOriginsCm[lane].X = scanner.FirstTargetXCm +
                    (scanner.CapsuleCastStartingOverlapTargetIndex * scanner.TargetSpacingCm);
            }
            _queryDirections[lane] = Vector3.UnitX;
            _queryDistancesCm[lane] = lane < 4
                ? scanner.DistancePresetsCm[_scannerDistancePresetIndex]
                : 0f;
            for (int target = 0; target < scanner.TargetCount; target++)
            {
                Physics3DScannerLayerShowcaseConfig layer = scanner.Layers[scanner.TargetLayerIndices[target]];
                AddOwnedBody(
                    Physics3DBodyKind.Static,
                    _boxShape,
                    Physics3DShapeKind.Box,
                    new Vector3(size),
                    0f,
                    new Vector3(
                        scanner.FirstTargetXCm + (target * scanner.TargetSpacingCm),
                        scanner.OriginYCm,
                        z),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, target * 0.18f),
                    Vector3.Zero,
                    Vector3.Zero,
                    Physics3DContinuousDetectionMode.Discrete,
                    new Vector4(layer.ColorR, layer.ColorG, layer.ColorB, 1f),
                    collisionLayer: new LayerMask(layer.Category, uint.MaxValue));
            }
        }

        _querySizesCm[0] = Vector3.Zero;
        _querySizesCm[1] = new Vector3(size * 0.65f);
        _querySizesCm[2] = new Vector3(size * 0.7f);
        _querySizesCm[3] = new Vector3(size * 0.6f, size * 1.6f, size * 0.6f);
        _querySizesCm[4] = new Vector3(size * 2.4f, size * 1.2f, size * 1.2f);
        _querySizesCm[5] = new Vector3(size * 1.25f);
        _querySizesCm[6] = new Vector3(size, size * 2.5f, size);
        ClearScannerResult();
    }

    private void BuildConstraintForgeLegacyExhibits()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        float size = config.BodySizeCm;
        float spacing = size * 1.25f;
        Physics3DSpringSettings spring = CreateSpring();

        int previous = -1;
        for (int i = 0; i < config.ChainLinkCount; i++)
        {
            int index = _bodyCount;
            AddOwnedBody(
                i == 0 ? Physics3DBodyKind.Kinematic : Physics3DBodyKind.Dynamic,
                _sphereShape,
                Physics3DShapeKind.Sphere,
                new Vector3(size),
                0f,
                new Vector3(-2800f + (i * spacing), 1900f, -900f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                i == 0 ? KinematicColor : DynamicBlue);
            if (previous >= 0)
            {
                AddOwnedConstraint(RequirePhysicsWorld().CreateBallSocketConstraint(
                    _bodyIds[previous],
                    _bodyIds[index],
                    new Vector3(spacing * 0.5f, 0f, 0f),
                    new Vector3(-spacing * 0.5f, 0f, 0f),
                    spring));
            }

            previous = index;
        }

        previous = -1;
        for (int i = 0; i < config.ChainLinkCount; i++)
        {
            int index = _bodyCount;
            AddOwnedBody(
                i == 0 ? Physics3DBodyKind.Kinematic : Physics3DBodyKind.Dynamic,
                _plankShape,
                Physics3DShapeKind.Box,
                PlankVisualSize(config),
                0f,
                new Vector3(-2800f + (i * spacing), 1250f, 200f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                i == 0 ? KinematicColor : DynamicGold);
            if (previous >= 0)
            {
                AddOwnedConstraint(RequirePhysicsWorld().CreateHingeConstraint(
                    _bodyIds[previous],
                    _bodyIds[index],
                    new Vector3(spacing * 0.5f, 0f, 0f),
                    Vector3.UnitZ,
                    new Vector3(-spacing * 0.5f, 0f, 0f),
                    Vector3.UnitZ,
                    spring));
            }

            previous = index;
        }

        int weldPairCount = Math.Max(2, config.ChainLinkCount / 2);
        for (int pair = 0; pair < weldPairCount; pair++)
        {
            float x = -2200f + (pair * spacing * 2.2f);
            int bodyAIndex = _bodyCount;
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _boxShape,
                Physics3DShapeKind.Box,
                new Vector3(size),
                0f,
                new Vector3(x, 650f, 1100f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                DynamicGreen);
            int bodyBIndex = _bodyCount;
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _boxShape,
                Physics3DShapeKind.Box,
                new Vector3(size),
                0f,
                new Vector3(x + spacing, 650f, 1100f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                DynamicGreen);
            AddOwnedConstraint(RequirePhysicsWorld().CreateWeldConstraint(
                _bodyIds[bodyAIndex],
                _bodyIds[bodyBIndex],
                new Vector3(spacing, 0f, 0f),
                Quaternion.Identity,
                spring));
        }

        for (int i = 0; i < _constraintCount; i++)
        {
            if (!RequirePhysicsWorld().ContainsConstraint(_constraintIds[i]))
            {
                throw new InvalidOperationException($"Joints scene created invalid constraint '{_constraintIds[i]}'.");
            }
        }
    }

    private void BuildReplayTheaterScene()
    {
        BuildDeterminismLayout();
        _replayStatus = Physics3DShowcaseReplayStatus.Recording;
        _replayCursor = 0;
        _replayDifferenceRequested = false;
        _replayDifferenceInjected = false;
        RequireSimulation().Enabled = true;
    }

    private void BuildDeterminismLayout()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        float size = config.BodySizeCm;
        float spacing = config.ReplayBodySpacingCm;
        int gridSize = config.ReplayGridSize;
        float halfGrid = (gridSize - 1) * 0.5f;
        _determinismFirstBodyIndex = _bodyCount;
        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                float velocityX = ((x & 1) == 0 ? 1f : -1f) * (80f + (y * 7f));
                float velocityZ = ((y & 1) == 0 ? 1f : -1f) * (55f + (x * 5f));
                AddOwnedBody(
                    Physics3DBodyKind.Dynamic,
                    _boxShape,
                    Physics3DShapeKind.Box,
                    new Vector3(size),
                    0f,
                    new Vector3(
                        config.ReplayCenterXCm + ((x - halfGrid) * spacing),
                        config.ReplayBaseHeightCm + (y * spacing),
                        (y - halfGrid) * spacing),
                    Quaternion.CreateFromYawPitchRoll(x * 0.03f, y * 0.025f, (x + y) * 0.01f),
                    new Vector3(velocityX, 0f, velocityZ),
                    new Vector3(0.15f + (x * 0.01f), 0.10f + (y * 0.01f), 0.08f),
                    Physics3DContinuousDetectionMode.Passive,
                    ((x + y) & 1) == 0 ? DynamicBlue : DynamicGold);
            }
        }

        _determinismBodyCount = checked(gridSize * gridSize);
        for (int i = 0; i < _determinismBodyCount; i++)
        {
            _replayInitialStates[i] = RequirePhysicsWorld().GetBodyState(_bodyIds[_determinismFirstBodyIndex + i]);
        }
    }

    private void AddFloor()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _floorShape,
            Physics3DShapeKind.Box,
            new Vector3(config.FloorSizeCm, config.FloorThicknessCm, config.FloorSizeCm),
            0f,
            new Vector3(0f, -config.FloorThicknessCm * 0.5f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            FloorColor);
    }

    private void PrepareSceneForPhysicsStep()
    {
        switch (_scene)
        {
            case Physics3DShowcaseScene.ReplayTheater:
                PrepareReplayDifferenceStep();
                break;
            case Physics3DShowcaseScene.MaterialHill:
                PrepareMaterialHillStep();
                break;
            case Physics3DShowcaseScene.WindTunnel:
                PrepareWindTunnelStep();
                break;
            case Physics3DShowcaseScene.ScaleCity:
                PrepareScaleCityFixedStep();
                break;
            case Physics3DShowcaseScene.ConstraintForge:
                PrepareConstraintForgeStep();
                break;
            case Physics3DShowcaseScene.PlatformStation:
            case Physics3DShowcaseScene.TraversalCourse:
                PrepareCharacterTraversalStep();
                break;
            case Physics3DShowcaseScene.WheelLab:
                PrepareWheelLabStep();
                break;
            case Physics3DShowcaseScene.RagdollLab:
                PrepareRagdollLabFixedStep();
                break;
        }
    }

    private void ObserveSceneAfterPhysicsStep()
    {
        switch (_scene)
        {
            case Physics3DShowcaseScene.MaterialHill:
                ObserveMaterialHillStep();
                break;
            case Physics3DShowcaseScene.ReplayTheater:
                ObserveDeterminismStep();
                break;
            case Physics3DShowcaseScene.PlatformStation:
            case Physics3DShowcaseScene.TraversalCourse:
                ObserveCharacterTraversalStep();
                break;
            case Physics3DShowcaseScene.WheelLab:
                ObserveWheelLabStep();
                RequireWheelLabVehicles().ObserveFixedStep();
                break;
            case Physics3DShowcaseScene.RagdollLab:
                ObserveRagdollLabFixedStep(_sceneStep - 1);
                break;
            case Physics3DShowcaseScene.ScaleCity:
                RecordScaleCityPhysicsPerformanceSample(RequireSimulation().MaximumStepMillisecondsLastUpdate);
                break;
            case Physics3DShowcaseScene.ScannerRange:
                AdvanceScannerPlayback();
                break;
        }
    }

    private void SetScannerQueryKind(int value)
    {
        RequireScannerRangeCommand(nameof(Physics3DShowcaseCommandKind.SetScannerQueryKind));
        if ((uint)value > byte.MaxValue || !Enum.IsDefined(typeof(Physics3DShowcaseQueryKind), (byte)value))
        {
            throw new InvalidOperationException($"Unknown Scanner Range query kind value {value}.");
        }

        _scannerQueryKind = (Physics3DShowcaseQueryKind)value;
        ClearScannerResult();
        _lastAction = $"Selected {ScannerQueryLabel(_scannerQueryKind)}. Press Run Scan to query the authored targets.";
    }

    private void SetScannerDistancePreset(int value)
    {
        RequireScannerRangeCommand(nameof(Physics3DShowcaseCommandKind.SetScannerDistancePreset));
        float[] presets = ActiveConfig.ScannerRange.DistancePresetsCm;
        if ((uint)value >= (uint)presets.Length)
        {
            throw new InvalidOperationException($"Scanner Range distance preset {value} is outside [0, {presets.Length - 1}].");
        }

        _scannerDistancePresetIndex = value;
        for (int queryIndex = 0; queryIndex < 4; queryIndex++)
        {
            _queryDistancesCm[queryIndex] = presets[value];
        }
        ClearScannerResult();
        _lastAction = $"Scan distance set to {presets[value]:0} cm. Press Run Scan to apply it.";
    }

    private void SetScannerLayerFilter(int value)
    {
        RequireScannerRangeCommand(nameof(Physics3DShowcaseCommandKind.SetScannerLayerFilter));
        Physics3DScannerLayerFilterShowcaseConfig[] filters = ActiveConfig.ScannerRange.LayerFilters;
        if ((uint)value >= (uint)filters.Length)
        {
            throw new InvalidOperationException($"Scanner Range layer filter {value} is outside [0, {filters.Length - 1}].");
        }

        _scannerLayerFilterIndex = value;
        ClearScannerResult();
        _lastAction = $"Layer filter set to {filters[value].Name}. Press Run Scan to apply it.";
    }

    private void ExecuteSelectedScannerQuery()
    {
        RequireScannerRangeCommand(nameof(Physics3DShowcaseCommandKind.RunScannerQuery));
        ClearScannerResult();
        int queryIndex = ScannerQueryIndex;
        Physics3DScannerLayerFilterShowcaseConfig filterConfig =
            ActiveConfig.ScannerRange.LayerFilters[_scannerLayerFilterIndex];
        LayerMask filter = new(uint.MaxValue, filterConfig.Mask);
        IPhysics3DWorld world = RequirePhysicsWorld();

        try
        {
            switch (_scannerQueryKind)
            {
                case Physics3DShowcaseQueryKind.Ray:
                    _queryHitCounts[queryIndex] = world.Raycast(
                        _queryOriginsCm[queryIndex],
                        _queryDirections[queryIndex],
                        _queryDistancesCm[queryIndex],
                        filter,
                        _rayHits);
                    CaptureRaycastHits(queryIndex);
                    break;
                case Physics3DShowcaseQueryKind.BoxCast:
                    _queryHitCounts[queryIndex] = world.BoxCast(
                        _queryOriginsCm[queryIndex],
                        _querySizesCm[queryIndex],
                        Quaternion.Identity,
                        _queryDirections[queryIndex],
                        _queryDistancesCm[queryIndex],
                        filter,
                        _shapeCastHits);
                    CaptureShapeCastHits(queryIndex);
                    break;
                case Physics3DShowcaseQueryKind.SphereCast:
                    _queryHitCounts[queryIndex] = world.SphereCast(
                        _queryOriginsCm[queryIndex],
                        _querySizesCm[queryIndex].X * 0.5f,
                        _queryDirections[queryIndex],
                        _queryDistancesCm[queryIndex],
                        filter,
                        _shapeCastHits);
                    CaptureShapeCastHits(queryIndex);
                    break;
                case Physics3DShowcaseQueryKind.CapsuleCast:
                {
                    float diameter = _querySizesCm[queryIndex].X;
                    _queryHitCounts[queryIndex] = world.CapsuleCast(
                        _queryOriginsCm[queryIndex],
                        diameter * 0.5f,
                        _querySizesCm[queryIndex].Y - diameter,
                        Quaternion.Identity,
                        _queryDirections[queryIndex],
                        _queryDistancesCm[queryIndex],
                        filter,
                        _shapeCastHits);
                    CaptureShapeCastHits(queryIndex);
                    break;
                }
                case Physics3DShowcaseQueryKind.BoxOverlap:
                    _queryHitCounts[queryIndex] = world.OverlapBox(
                        _queryOriginsCm[queryIndex],
                        _querySizesCm[queryIndex],
                        Quaternion.Identity,
                        filter,
                        _overlapHits);
                    CaptureOverlapHits(queryIndex);
                    break;
                case Physics3DShowcaseQueryKind.SphereOverlap:
                    _queryHitCounts[queryIndex] = world.OverlapSphere(
                        _queryOriginsCm[queryIndex],
                        _querySizesCm[queryIndex].X * 0.5f,
                        filter,
                        _overlapHits);
                    CaptureOverlapHits(queryIndex);
                    break;
                case Physics3DShowcaseQueryKind.CapsuleOverlap:
                {
                    float diameter = _querySizesCm[queryIndex].X;
                    _queryHitCounts[queryIndex] = world.OverlapCapsule(
                        _queryOriginsCm[queryIndex],
                        diameter * 0.5f,
                        _querySizesCm[queryIndex].Y - diameter,
                        Quaternion.Identity,
                        filter,
                        _overlapHits);
                    CaptureOverlapHits(queryIndex);
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unsupported Scanner Range query kind '{_scannerQueryKind}'.");
            }

            SortQueryHitsByDistance(queryIndex);
            _scannerHasResult = true;
            _scannerRunSequence++;
            BeginScannerPlayback(queryIndex, filterConfig.Name);
        }
        catch (Physics3DCapacityExceededException exception)
        {
            ClearScannerResult();
            _scannerQueryFailed = true;
            _scannerRunSequence++;
            _scannerPlaybackStatus = Physics3DScannerPlaybackStatus.Failed;
            _lastAction = $"Scan failed: {filterConfig.Name} exceeded the configured {exception.Resource} capacity " +
                $"of {exception.Capacity}. No result was truncated.";
        }
    }

    private void ClearScannerResult()
    {
        Array.Clear(_queryHitCounts, 0, _queryHitCounts.Length);
        Array.Clear(_queryHasFirstHit, 0, _queryHasFirstHit.Length);
        Array.Clear(_queryFirstHitPositionsCm, 0, _queryFirstHitPositionsCm.Length);
        Array.Clear(_queryHitPositionsCm, 0, _queryHitPositionsCm.Length);
        Array.Clear(_queryHitNormals, 0, _queryHitNormals.Length);
        Array.Clear(_queryHitDistancesCm, 0, _queryHitDistancesCm.Length);
        Array.Clear(_queryHitStartedOverlapping, 0, _queryHitStartedOverlapping.Length);
        _scannerHasResult = false;
        _scannerQueryFailed = false;
        _scannerPlaybackTick = 0;
        _scannerVisibleHitCount = 0;
        _scannerPlaybackDistanceCm = 0f;
        _scannerPlaybackStatus = Physics3DScannerPlaybackStatus.Waiting;
    }

    private void BeginScannerPlayback(int queryIndex, string filterName)
    {
        _scannerPlaybackTick = 0;
        _scannerPlaybackDistanceCm = 0f;
        if (_scannerQueryKind is Physics3DShowcaseQueryKind.BoxOverlap or
            Physics3DShowcaseQueryKind.SphereOverlap or
            Physics3DShowcaseQueryKind.CapsuleOverlap)
        {
            _scannerVisibleHitCount = _queryHitCounts[queryIndex];
            _scannerPlaybackStatus = Physics3DScannerPlaybackStatus.Pulsing;
            _lastAction = $"{ScannerQueryLabel(_scannerQueryKind)} found {_queryHitCounts[queryIndex]} " +
                $"{filterName} target(s). The overlap volume now pulses at its origin.";
            return;
        }

        UpdateScannerVisibleHitCount(queryIndex);
        _scannerPlaybackStatus = Physics3DScannerPlaybackStatus.Playing;
        _lastAction = $"Playing {ScannerQueryLabel(_scannerQueryKind)} over " +
            $"{ActiveConfig.ScannerRange.CastPlaybackDurationTicks} fixed ticks; hits appear nearest first.";
    }

    private void AdvanceScannerPlayback()
    {
        if (!_scannerHasResult)
        {
            return;
        }

        if (_scannerPlaybackStatus == Physics3DScannerPlaybackStatus.Pulsing)
        {
            int cycleTicks = ActiveConfig.ScannerRange.OverlapPulseCycleTicks;
            _scannerPlaybackTick = _scannerPlaybackTick + 1 == cycleTicks
                ? 0
                : _scannerPlaybackTick + 1;
            return;
        }

        if (_scannerPlaybackStatus != Physics3DScannerPlaybackStatus.Playing)
        {
            return;
        }

        int durationTicks = ActiveConfig.ScannerRange.CastPlaybackDurationTicks;
        _scannerPlaybackTick = Math.Min(_scannerPlaybackTick + 1, durationTicks);
        float fullDistanceCm = _queryDistancesCm[ScannerQueryIndex];
        _scannerPlaybackDistanceCm = fullDistanceCm * (_scannerPlaybackTick / (float)durationTicks);
        UpdateScannerVisibleHitCount(ScannerQueryIndex);
        if (_scannerPlaybackTick == durationTicks)
        {
            _scannerPlaybackDistanceCm = fullDistanceCm;
            _scannerVisibleHitCount = _queryHitCounts[ScannerQueryIndex];
            _scannerPlaybackStatus = Physics3DScannerPlaybackStatus.Complete;
            _lastAction = $"{ScannerQueryLabel(_scannerQueryKind)} playback complete: " +
                $"{_scannerVisibleHitCount} ordered hit(s), numbered in the world.";
        }
    }

    private void UpdateScannerVisibleHitCount(int queryIndex)
    {
        int count = _queryHitCounts[queryIndex];
        int offset = checked(queryIndex * ActiveConfig.QueryHitCapacity);
        int visible = 0;
        while (visible < count)
        {
            int hitOffset = offset + visible;
            if (_queryHitStartedOverlapping[hitOffset] == 0 &&
                _queryHitDistancesCm[hitOffset] > _scannerPlaybackDistanceCm + 0.001f)
            {
                break;
            }

            visible++;
        }

        _scannerVisibleHitCount = visible;
    }

    private float ScannerPulseScale()
    {
        if (_scannerPlaybackStatus != Physics3DScannerPlaybackStatus.Pulsing)
        {
            return 1f;
        }

        Physics3DScannerRangeShowcaseConfig scanner = ActiveConfig.ScannerRange;
        int cycleTick = _scannerPlaybackTick % scanner.OverlapPulseCycleTicks;
        float normalized = cycleTick / (float)scanner.OverlapPulseCycleTicks;
        float triangle = 1f - MathF.Abs((normalized * 2f) - 1f);
        return 1f + ((scanner.OverlapPulseMaximumScale - 1f) * triangle);
    }

    private void RequireScannerRangeCommand(string commandName)
    {
        if (_scene != Physics3DShowcaseScene.ScannerRange)
        {
            throw new InvalidOperationException($"{commandName} requires the active Scanner Range station.");
        }
    }

    private static string ScannerQueryLabel(Physics3DShowcaseQueryKind kind) => kind switch
    {
        Physics3DShowcaseQueryKind.Ray => "Ray",
        Physics3DShowcaseQueryKind.BoxCast => "Box Cast",
        Physics3DShowcaseQueryKind.SphereCast => "Sphere Cast",
        Physics3DShowcaseQueryKind.CapsuleCast => "Capsule Cast",
        Physics3DShowcaseQueryKind.BoxOverlap => "Box Overlap",
        Physics3DShowcaseQueryKind.SphereOverlap => "Sphere Overlap",
        Physics3DShowcaseQueryKind.CapsuleOverlap => "Capsule Overlap",
        _ => throw new InvalidOperationException($"Unknown Scanner Range query kind '{kind}'.")
    };

    private void CaptureRaycastHits(int queryIndex)
    {
        int count = _queryHitCounts[queryIndex];
        int offset = checked(queryIndex * ActiveConfig.QueryHitCapacity);
        for (int i = 0; i < count; i++)
        {
            Physics3DRaycastHit hit = _rayHits[i];
            _queryHitPositionsCm[offset + i] = hit.PositionCm;
            _queryHitNormals[offset + i] = hit.Normal;
            _queryHitDistancesCm[offset + i] = hit.DistanceCm;
            _queryHitStartedOverlapping[offset + i] = 0;
        }

        CaptureFirstQueryHit(queryIndex);
    }

    private void CaptureShapeCastHits(int queryIndex)
    {
        int count = _queryHitCounts[queryIndex];
        int offset = checked(queryIndex * ActiveConfig.QueryHitCapacity);
        for (int i = 0; i < count; i++)
        {
            Physics3DShapeCastHit hit = _shapeCastHits[i];
            _queryHitPositionsCm[offset + i] = hit.PositionCm;
            _queryHitNormals[offset + i] = hit.Normal;
            _queryHitDistancesCm[offset + i] = hit.DistanceCm;
            _queryHitStartedOverlapping[offset + i] = hit.StartedOverlapping ? (byte)1 : (byte)0;
        }

        CaptureFirstQueryHit(queryIndex);
    }

    private void CaptureOverlapHits(int queryIndex)
    {
        int count = _queryHitCounts[queryIndex];
        int offset = checked(queryIndex * ActiveConfig.QueryHitCapacity);
        IPhysics3DWorld world = RequirePhysicsWorld();
        for (int i = 0; i < count; i++)
        {
            Vector3 positionCm = world.GetBodyState(_overlapHits[i].Body).PositionCm;
            _queryHitPositionsCm[offset + i] = positionCm;
            _queryHitNormals[offset + i] = Vector3.Zero;
            _queryHitDistancesCm[offset + i] = Vector3.Distance(_queryOriginsCm[queryIndex], positionCm);
            _queryHitStartedOverlapping[offset + i] = 1;
        }

        CaptureFirstQueryHit(queryIndex);
    }

    private void CaptureFirstQueryHit(int queryIndex)
    {
        if (_queryHitCounts[queryIndex] <= 0)
        {
            return;
        }

        int offset = checked(queryIndex * ActiveConfig.QueryHitCapacity);
        _queryHasFirstHit[queryIndex] = 1;
        _queryFirstHitPositionsCm[queryIndex] = _queryHitPositionsCm[offset];
    }

    private void SortQueryHitsByDistance(int queryIndex)
    {
        int count = _queryHitCounts[queryIndex];
        int offset = checked(queryIndex * ActiveConfig.QueryHitCapacity);
        for (int i = 1; i < count; i++)
        {
            int source = offset + i;
            Vector3 position = _queryHitPositionsCm[source];
            Vector3 normal = _queryHitNormals[source];
            float distance = _queryHitDistancesCm[source];
            byte startedOverlapping = _queryHitStartedOverlapping[source];
            int destination = source;
            while (destination > offset && _queryHitDistancesCm[destination - 1] > distance)
            {
                _queryHitPositionsCm[destination] = _queryHitPositionsCm[destination - 1];
                _queryHitNormals[destination] = _queryHitNormals[destination - 1];
                _queryHitDistancesCm[destination] = _queryHitDistancesCm[destination - 1];
                _queryHitStartedOverlapping[destination] = _queryHitStartedOverlapping[destination - 1];
                destination--;
            }

            _queryHitPositionsCm[destination] = position;
            _queryHitNormals[destination] = normal;
            _queryHitDistancesCm[destination] = distance;
            _queryHitStartedOverlapping[destination] = startedOverlapping;
        }

        CaptureFirstQueryHit(queryIndex);
    }

    private void ObserveDeterminismStep()
    {
        ulong hash = ComputeOwnedStateHash(_determinismFirstBodyIndex, _determinismBodyCount);
        if (_replayStatus == Physics3DShowcaseReplayStatus.Recording)
        {
            int recordedStep = _replayCursor;
            _replayHashes[recordedStep] = hash;
            CaptureRecordedReplayStates(recordedStep);
            _replayCursor++;
            if (_replayCursor == _replayHashes.Length)
            {
                RebuildDeterminismForReplay();
            }

            return;
        }

        if (_replayStatus != Physics3DShowcaseReplayStatus.Replaying)
        {
            return;
        }

        ulong expected = _replayHashes[_replayCursor];
        if (hash != expected)
        {
            _replayExpectedHash = expected;
            _replayActualHash = hash;
            _replayStatus = Physics3DShowcaseReplayStatus.Failed;
            RequireSimulation().Enabled = false;
            _lastAction = $"Deterministic rebuild verification failed at step {_replayCursor + 1}.";
            return;
        }

        _replayCursor++;
        if (_replayCursor == _replayHashes.Length)
        {
            _replayStatus = Physics3DShowcaseReplayStatus.Passed;
            RequireSimulation().Enabled = false;
            _lastAction = $"Deterministic rebuild passed: {_replayHashes.Length} baseline steps matched the rebuilt run.";
        }
    }

    private void CaptureRecordedReplayStates(int recordedStep)
    {
        int destinationOffset = checked(recordedStep * _determinismBodyCount);
        for (int i = 0; i < _determinismBodyCount; i++)
        {
            _replayRecordedStates[destinationOffset + i] = RequirePhysicsWorld().GetBodyState(
                _bodyIds[_determinismFirstBodyIndex + i]);
        }
    }

    private void RebuildDeterminismForReplay()
    {
        ClearOwnedScene();
        _sceneStep = 0;
        BuildDeterminismLayout();
        _replayCursor = 0;
        _replayStatus = Physics3DShowcaseReplayStatus.ReadyToReplay;
        RequireSimulation().Enabled = false;
        _lastAction = "Scripted baseline captured. The rebuilt station is paused and ready for body-state verification.";
    }

    private void StartReplayComparison(bool injectDifference)
    {
        if (_scene != Physics3DShowcaseScene.ReplayTheater ||
            _replayStatus != Physics3DShowcaseReplayStatus.ReadyToReplay)
        {
            throw new InvalidOperationException("Deterministic rebuild verification can only start from the rebuilt ready frame.");
        }

        _replayCursor = 0;
        _replayStatus = Physics3DShowcaseReplayStatus.Replaying;
        _replayDifferenceRequested = injectDifference;
        _replayDifferenceInjected = false;
        RequireSimulation().Enabled = true;
        _lastAction = injectDifference
            ? $"Difference run started. Body {ActiveConfig.ReplayDifferenceBodyIndex + 1} will change at step {ActiveConfig.ReplayDifferenceStep}."
            : "Clean run started. Comparing the gold rebuilt run against the blue scripted baseline, step by step.";
    }

    private void PrepareReplayDifferenceStep()
    {
        if (_replayStatus != Physics3DShowcaseReplayStatus.Replaying ||
            !_replayDifferenceRequested ||
            _replayDifferenceInjected ||
            _replayCursor + 1 != ActiveConfig.ReplayDifferenceStep)
        {
            return;
        }

        int localBodyIndex = ActiveConfig.ReplayDifferenceBodyIndex;
        int bodyIndex = _determinismFirstBodyIndex + localBodyIndex;
        Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(_bodyIds[bodyIndex]);
        state.LinearVelocityCmPerSecond += new Vector3(
            ActiveConfig.ReplayDifferenceVelocityXCmPerSecond,
            ActiveConfig.ReplayDifferenceVelocityYCmPerSecond,
            ActiveConfig.ReplayDifferenceVelocityZCmPerSecond);
        state.Awake = true;
        SetBodyStateAndPose(bodyIndex, in state);
        _replayDifferenceInjected = true;
        _lastAction = $"Injected the configured difference into body {localBodyIndex + 1} at step {_replayCursor + 1}.";
    }

    private ulong ComputeOwnedStateHash(int firstBodyIndex, int count)
    {
        if (firstBodyIndex < 0 || count <= 0 || firstBodyIndex + count > _bodyCount)
        {
            throw new InvalidOperationException("Physics3D determinism hash range is invalid.");
        }

        ulong hash = 1469598103934665603UL;
        for (int i = 0; i < count; i++)
        {
            int index = firstBodyIndex + i;
            Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(_bodyIds[index]);
            hash = Mix(hash, (uint)_bodyKinds[index]);
            hash = Mix(hash, (uint)_bodyShapeKinds[index]);
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.PositionCm.X)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.PositionCm.Y)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.PositionCm.Z)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.Orientation.X)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.Orientation.Y)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.Orientation.Z)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.Orientation.W)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.LinearVelocityCmPerSecond.X)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.LinearVelocityCmPerSecond.Y)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.LinearVelocityCmPerSecond.Z)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.AngularVelocityRadiansPerSecond.X)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.AngularVelocityRadiansPerSecond.Y)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.AngularVelocityRadiansPerSecond.Z)));
            hash = Mix(hash, state.Awake ? 1u : 0u);
        }

        return hash;
    }

    private void ApplyImpact()
    {
        if (_scene == Physics3DShowcaseScene.MaterialHill)
        {
            if (_materialHillImpulsePending || _materialHillImpulseSubmissionCount != 0)
            {
                _lastAction = "The crates have already launched. Press Reset before pushing them again.";
                return;
            }

            _materialHillImpulsePending = true;
            _lastAction = "Push Crates queued for the next authoritative 30Hz step.";
            return;
        }

        if (_scene == Physics3DShowcaseScene.ScaleCity)
        {
            QueueScaleCityPulse();
            return;
        }

        IPhysics3DWorld physics = RequirePhysicsWorld();
        int affected = 0;
        for (int i = 0; i < _bodyCount; i++)
        {
            if (_bodyKinds[i] != Physics3DBodyKind.Dynamic)
            {
                continue;
            }

            float x = ((i & 1) == 0 ? 1f : -1f) * 0.45f;
            float z = ((i & 2) == 0 ? 1f : -1f) * 0.35f;
            Vector3 direction = Vector3.Normalize(new Vector3(x, 1f, z));
            physics.EnqueueLinearImpulse(
                _bodyIds[i],
                direction * ActiveConfig.ImpactSpeedCmPerSecond);
            affected++;
        }

        if (_scene == Physics3DShowcaseScene.ReplayTheater && affected > 0)
        {
            for (int i = 0; i < _determinismBodyCount; i++)
            {
                _replayInitialStates[i] = RequirePhysicsWorld().GetBodyState(_bodyIds[_determinismFirstBodyIndex + i]);
            }

            _replayCursor = 0;
            _replayStatus = Physics3DShowcaseReplayStatus.Recording;
            RequireSimulation().Enabled = true;
        }

        _lastAction = affected > 0
            ? $"Impact launched {affected} dynamic bodies without changing scene ownership."
            : "This scene has no dynamic body to impact.";
    }

    private void SetBodyStateAndPose(int index, in Physics3DBodyState state)
    {
        RequirePhysicsWorld().SetBodyState(_bodyIds[index], state);
        Entity entity = _bodyEntities[index];
        if (!RequireEcsWorld().IsAlive(entity))
        {
            throw new InvalidOperationException($"Physics3D showcase lost ECS entity for body index {index}.");
        }

        ref Physics3DPoseCm pose = ref RequireEcsWorld().Get<Physics3DPoseCm>(entity);
        pose.Position = state.PositionCm;
        pose.Orientation = state.Orientation;
        pose.LinearVelocity = state.LinearVelocityCmPerSecond;
        pose.AngularVelocity = state.AngularVelocityRadiansPerSecond;
    }

    private static Vector3 PlankVisualSize(Physics3DShowcaseConfig config)
    {
        float size = config.BodySizeCm;
        return new Vector3(size * 1.5f, size * 0.35f, size * 0.6f);
    }

    private static ulong Mix(ulong hash, uint value)
    {
        hash ^= value;
        return hash * 1099511628211UL;
    }
}
