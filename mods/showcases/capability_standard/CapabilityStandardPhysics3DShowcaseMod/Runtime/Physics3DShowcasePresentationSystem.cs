using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Physics3D;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Scripting;
using Ludots.Core.Vehicle3D;
using Ludots.UI;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed class Physics3DShowcasePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly Physics3DShowcaseRuntime _runtime;
    private readonly Physics3DShowcasePanelController _panel;
    private readonly PresentationRequestBuffer _requests;
    private readonly int _cubeMeshId;
    private readonly int _sphereMeshId;
    private readonly int _cylinderMeshId;
    private Physics3DShowcasePanelState _panelState = Physics3DShowcasePanelState.Empty;
    private float _panelRefreshAccumulator;
    private bool _hasPanelState;

    public Physics3DShowcasePresentationSystem(GameEngine engine, Physics3DShowcaseRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _panel = new Physics3DShowcasePanelController(runtime);
        _requests = engine.GetService(CoreServiceKeys.PresentationRequestBuffer)
            ?? throw new InvalidOperationException("Physics3D showcase requires PresentationRequestBuffer.");
        MeshAssetRegistry meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
            ?? throw new InvalidOperationException("Physics3D showcase requires MeshAssetRegistry.");
        _cubeMeshId = meshes.GetId(WellKnownMeshKeys.Cube);
        _sphereMeshId = meshes.GetId(WellKnownMeshKeys.Sphere);
        _cylinderMeshId = meshes.GetId(WellKnownMeshKeys.Cylinder);
        if (_cubeMeshId <= 0 || _sphereMeshId <= 0 || _cylinderMeshId <= 0)
        {
            throw new InvalidOperationException("Physics3D showcase requires registered cube, sphere, and cylinder meshes.");
        }
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }

    public void Dispose()
    {
        if (_engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panel.ClearIfOwned(root);
        }
    }

    public void Update(in float t)
    {
        if (_engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            if (_runtime.IsActive)
            {
                throw new InvalidOperationException("Physics3D showcase requires UIRoot while active.");
            }

            return;
        }

        if (!_runtime.IsActive)
        {
            _panel.ClearIfOwned(root);
            _hasPanelState = false;
            _panelRefreshAccumulator = 0f;
            return;
        }

        if (!float.IsFinite(t) || t < 0f)
        {
            throw new InvalidOperationException($"Physics3D showcase received invalid presentation delta '{t}'.");
        }

        EmitBodies();
        EmitQueries();
        EmitWindTunnelSelection();
        EmitWheelLabDebug();
        EmitConstraintForgeLabels();
        float refreshInterval = 1f / _runtime.ActiveConfig.PanelRefreshHz;
        _panelRefreshAccumulator += t;
        bool sceneChanged = _hasPanelState && _panelState.Scene != _runtime.ActiveScene;
        bool scannerStateChanged = _runtime.ActiveScene == Physics3DShowcaseScene.ScannerRange &&
            (!_hasPanelState ||
             _panelState.ScannerQueryKind != _runtime.ScannerQueryKind ||
             _panelState.ScannerResultMode != _runtime.ScannerResultMode ||
             _panelState.ScannerDistancePresetIndex != _runtime.ScannerDistancePresetIndex ||
             _panelState.ScannerLayerFilterIndex != _runtime.ScannerLayerFilterIndex ||
             _panelState.ScannerIncludeSensors != _runtime.ScannerIncludeSensors ||
             _panelState.ScannerIgnoreSelf != _runtime.ScannerIgnoreSelf ||
             _panelState.ScannerIgnoreAssembly != _runtime.ScannerIgnoreAssembly ||
             _panelState.ScannerRunSequence != _runtime.ScannerRunSequence ||
             _panelState.ScannerPlaybackStatus != _runtime.ScannerPlaybackStatus ||
             _panelState.ScannerPlaybackTick != _runtime.ScannerPlaybackTick ||
             _panelState.ScannerVisibleHitCount != _runtime.ScannerVisibleHitCount ||
             _panelState.ScannerHasResult != _runtime.ScannerHasResult ||
             _panelState.ScannerQueryFailed != _runtime.ScannerQueryFailed);
        if (!_hasPanelState || sceneChanged || scannerStateChanged || _panelRefreshAccumulator >= refreshInterval)
        {
            _panelState = _runtime.CapturePanelState();
            _panelRefreshAccumulator %= refreshInterval;
            _hasPanelState = true;
        }

        _panel.MountOrSync(root, _engine, in _panelState);
    }

    private void EmitBodies()
    {
        if (_runtime.ActiveScene == Physics3DShowcaseScene.ReplayTheater)
        {
            EmitReplayBodies();
            return;
        }

        int bodyCount = _runtime.BodyCount;
        int limit = Math.Min(bodyCount, _runtime.ActiveConfig.VisibleBodyLimit);
        if (limit <= 0)
        {
            _runtime.SetVisibleBodyCount(0);
            return;
        }

        int emittedBodies = 0;
        if (bodyCount <= limit)
        {
            for (int i = 0; i < bodyCount; i++)
            {
                EmitBody(i);
                emittedBodies++;
            }
        }
        else if (_runtime.ActiveScene == Physics3DShowcaseScene.ScaleCity)
        {
            for (int i = 0; i < limit; i++)
            {
                EmitBody(i);
                emittedBodies++;
            }
        }
        else
        {
            EmitBody(0);
            emittedBodies++;
            int remaining = limit - 1;
            for (int sample = 0; sample < remaining; sample++)
            {
                int index = remaining == 1
                    ? 1
                    : 1 + (int)(((long)sample * (bodyCount - 2)) / (remaining - 1));
                EmitBody(index);
                emittedBodies++;
            }
        }

        _runtime.SetVisibleBodyCount(emittedBodies);
    }

    private void EmitBody(int index)
    {
        if (!_runtime.TryGetBodyVisual(
                index,
                out Physics3DBodyState state,
                out _,
                out Physics3DShapeKind shapeKind,
                out Vector3 visualSizeCm,
                out float capsuleCylinderLengthCm,
                out Vector4 color))
        {
            throw new InvalidOperationException($"Physics3D showcase failed to resolve visual body index {index}.");
        }

        int stableId = 860_000 + (index * 3);
        EmitBodyState(
            in state,
            shapeKind,
            visualSizeCm,
            capsuleCylinderLengthCm,
            color,
            stableId);
    }

    private void EmitBodyState(
        in Physics3DBodyState state,
        Physics3DShapeKind shapeKind,
        Vector3 visualSizeCm,
        float capsuleCylinderLengthCm,
        Vector4 color,
        int stableId)
    {
        switch (shapeKind)
        {
            case Physics3DShapeKind.Box:
                AddPrimitive(
                    _cubeMeshId,
                    ToMeters(state.PositionCm),
                    state.Orientation,
                    ToMeters(visualSizeCm),
                    color,
                    stableId);
                break;
            case Physics3DShapeKind.Sphere:
                AddPrimitive(
                    _sphereMeshId,
                    ToMeters(state.PositionCm),
                    state.Orientation,
                    ToMeters(visualSizeCm),
                    color,
                    stableId);
                break;
            case Physics3DShapeKind.Capsule:
                EmitCapsule(
                    state.PositionCm,
                    state.Orientation,
                    visualSizeCm.X,
                    capsuleCylinderLengthCm,
                    color,
                    stableId);
                break;
            case Physics3DShapeKind.Cylinder:
                AddPrimitive(
                    _cylinderMeshId,
                    ToMeters(state.PositionCm),
                    state.Orientation,
                    ToMeters(visualSizeCm),
                    color,
                    stableId);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Physics3D visual shape '{shapeKind}'.");
        }
    }

    private void EmitReplayBodies()
    {
        EmitBody(0);
        Physics3DShowcaseConfig config = _runtime.ActiveConfig;
        float laneSpanCm = ((config.ReplayGridSize - 1) * config.ReplayBodySpacingCm) +
                           (config.BodySizeCm * 2f);
        float padHeightCm = MathF.Max(6f, config.FloorThicknessCm * 0.15f);
        Vector4 recordedColor = new(0.20f, 0.66f, 0.98f, 0.86f);
        Vector4 replayColor = new(1.00f, 0.70f, 0.20f, 0.92f);
        AddPrimitive(
            _cubeMeshId,
            ToMeters(new Vector3(
                config.ReplayCenterXCm - config.ReplayLaneOffsetCm,
                padHeightCm * 0.5f,
                0f)),
            Quaternion.Identity,
            ToMeters(new Vector3(laneSpanCm, padHeightCm, laneSpanCm)),
            new Vector4(recordedColor.X, recordedColor.Y, recordedColor.Z, 0.22f),
            958_000);
        AddPrimitive(
            _cubeMeshId,
            ToMeters(new Vector3(
                config.ReplayCenterXCm + config.ReplayLaneOffsetCm,
                padHeightCm * 0.5f,
                0f)),
            Quaternion.Identity,
            ToMeters(new Vector3(laneSpanCm, padHeightCm, laneSpanCm)),
            new Vector4(replayColor.X, replayColor.Y, replayColor.Z, 0.22f),
            958_001);

        bool showComparison = _runtime.ReplayStatus != Physics3DShowcaseReplayStatus.Recording;
        Vector3 recordedOffset = new(-config.ReplayLaneOffsetCm, 0f, 0f);
        Vector3 replayOffset = new(config.ReplayLaneOffsetCm, 0f, 0f);
        for (int i = 0; i < _runtime.ReplayBodyCount; i++)
        {
            if (!_runtime.TryGetReplayComparisonVisual(
                    i,
                    out Physics3DBodyState recordedState,
                    out Physics3DBodyState actualState,
                    out Vector3 visualSizeCm))
            {
                throw new InvalidOperationException($"Physics3D replay visual {i} is unavailable.");
            }

            recordedState.PositionCm += recordedOffset;
            EmitBodyState(
                in recordedState,
                Physics3DShapeKind.Box,
                visualSizeCm,
                0f,
                recordedColor,
                960_000 + (i * 2));
            if (!showComparison)
            {
                continue;
            }

            actualState.PositionCm += replayOffset;
            EmitBodyState(
                in actualState,
                Physics3DShapeKind.Box,
                visualSizeCm,
                0f,
                replayColor,
                960_001 + (i * 2));
        }

        _runtime.SetVisibleBodyCount(1 + _runtime.ReplayBodyCount);
    }

    private void EmitQueries()
    {
        if (_runtime.ActiveScene != Physics3DShowcaseScene.ScannerRange)
        {
            return;
        }

        int queryIndex = _runtime.ScannerQueryIndex;
        if (!_runtime.TryGetQueryVisual(queryIndex, out Physics3DShowcaseQueryVisual query))
        {
            throw new InvalidOperationException($"Selected Physics3D query visual {queryIndex} is unavailable in Scanner Range.");
        }

        Vector4 color = _runtime.ScannerPlaybackStatus switch
        {
            Physics3DScannerPlaybackStatus.Failed => new Vector4(0.96f, 0.30f, 0.30f, 0.42f),
            Physics3DScannerPlaybackStatus.Playing => new Vector4(0.18f, 0.72f, 0.96f, 0.44f),
            Physics3DScannerPlaybackStatus.Pulsing => new Vector4(0.18f, 0.72f, 0.96f, 0.36f),
            Physics3DScannerPlaybackStatus.Complete
                when _runtime.ScannerResultMode == Physics3DShowcaseQueryResultMode.Any && _runtime.ScannerAnyHit =>
                    new Vector4(0.96f, 0.30f, 0.30f, 0.54f),
            Physics3DScannerPlaybackStatus.Complete => new Vector4(0.20f, 0.90f, 0.62f, 0.38f),
            Physics3DScannerPlaybackStatus.Waiting => new Vector4(1.00f, 0.72f, 0.18f, 0.38f),
            _ => throw new InvalidOperationException(
                $"Unknown Scanner Range playback status '{_runtime.ScannerPlaybackStatus}'.")
        };
        const int stableId = 940_000;
        EmitSelectedQueryVolume(in query, color, stableId);

        for (int hitIndex = 0; hitIndex < query.VisibleHitCount; hitIndex++)
        {
            if (!_runtime.TryGetQueryHitVisual(queryIndex, hitIndex, out Physics3DShowcaseQueryHitVisual hit))
            {
                throw new InvalidOperationException($"Scanner Range query {queryIndex} lost hit visual {hitIndex}.");
            }

            Vector4 hitColor = hit.StartedOverlapping
                ? new Vector4(0.98f, 0.35f, 0.24f, 1f)
                : hitIndex == 0
                    ? new Vector4(1f, 0.92f, 0.35f, 1f)
                    : new Vector4(0.28f, 0.78f, 1f, 0.9f);
            Physics3DScannerRangeShowcaseConfig scanner = _runtime.ActiveConfig.ScannerRange;
            int hitStableId = checked(1_100_000 + (hitIndex * 100));
            AddPrimitive(
                _sphereMeshId,
                ToMeters(hit.PositionCm),
                Quaternion.Identity,
                ToMeters(new Vector3(scanner.HitMarkerDiameterCm)),
                hitColor,
                hitStableId);
            if (hit.Normal.LengthSquared() > 1e-8f)
            {
                AddLinePrimitive(
                    hit.PositionCm,
                    hit.PositionCm + (hit.Normal * (_runtime.ActiveConfig.BodySizeCm * 0.65f)),
                    MathF.Max(2f, _runtime.ActiveConfig.BodySizeCm * 0.04f),
                    new Vector4(0.34f, 1f, 0.52f, 0.95f),
                    hitStableId + 1);
            }

            Vector3 numberCenterCm = hit.PositionCm + (Vector3.UnitY * scanner.HitNumberHeightOffsetCm);
            EmitHitNumber(hitIndex + 1, numberCenterCm, scanner, hitColor, hitStableId + 10);
            if (hit.StartedOverlapping)
            {
                float crossHalfSpanCm = scanner.HitNumberHeightCm * 0.65f;
                Vector3 diagonal = new(crossHalfSpanCm, crossHalfSpanCm, 0f);
                AddLinePrimitive(
                    numberCenterCm - diagonal,
                    numberCenterCm + diagonal,
                    scanner.HitNumberThicknessCm,
                    hitColor,
                    hitStableId + 2);
                diagonal.X = -diagonal.X;
                AddLinePrimitive(
                    numberCenterCm - diagonal,
                    numberCenterCm + diagonal,
                    scanner.HitNumberThicknessCm,
                    hitColor,
                    hitStableId + 3);
            }
        }
    }

    private void EmitSelectedQueryVolume(
        in Physics3DShowcaseQueryVisual query,
        Vector4 color,
        int stableId)
    {
        if (query.IsOverlap)
        {
            EmitQueryVolume(in query, query.OriginCm, color, stableId, query.PulseScale);
            return;
        }

        Vector3 direction = Vector3.Normalize(query.Direction);
        Vector3 playbackPositionCm = query.OriginCm + (direction * query.PlaybackDistanceCm);
        Physics3DScannerRangeShowcaseConfig scanner = _runtime.ActiveConfig.ScannerRange;
        switch (query.Kind)
        {
            case Physics3DShowcaseQueryKind.Ray:
                if (query.PlaybackDistanceCm <= 0.001f)
                {
                    EmitQueryVolume(in query, query.OriginCm, color, stableId);
                }
                else
                {
                    AddLinePrimitive(
                        query.OriginCm,
                        playbackPositionCm,
                        scanner.ScanPathThicknessCm,
                        color,
                        stableId);
                }
                break;
            case Physics3DShowcaseQueryKind.BoxCast:
            case Physics3DShowcaseQueryKind.SphereCast:
            case Physics3DShowcaseQueryKind.CapsuleCast:
                if (query.PlaybackDistanceCm > 0.001f)
                {
                    AddLinePrimitive(
                        query.OriginCm,
                        playbackPositionCm,
                        scanner.ScanPathThicknessCm,
                        color,
                        stableId);
                }
                EmitQueryVolume(in query, playbackPositionCm, color, stableId + 1);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Scanner Range cast visual '{query.Kind}'.");
        }
    }

    private void EmitHitNumber(
        int number,
        Vector3 centerCm,
        Physics3DScannerRangeShowcaseConfig scanner,
        Vector4 color,
        int stableId)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Scanner hit numbers must be positive.");
        }

        int digitCount = 1;
        int divisor = 1;
        while (number / divisor >= 10)
        {
            divisor *= 10;
            digitCount++;
        }

        float digitWidthCm = scanner.HitNumberHeightCm * 0.58f;
        float digitAdvanceCm = digitWidthCm * 1.35f;
        float firstDigitXCm = centerCm.X - (((digitCount - 1) * digitAdvanceCm) * 0.5f);
        for (int digitIndex = 0; digitIndex < digitCount; digitIndex++)
        {
            int digit = (number / divisor) % 10;
            EmitSevenSegmentDigit(
                digit,
                new Vector3(firstDigitXCm + (digitIndex * digitAdvanceCm), centerCm.Y, centerCm.Z),
                scanner.HitNumberHeightCm,
                digitWidthCm,
                scanner.HitNumberThicknessCm,
                color,
                stableId + (digitIndex * 7));
            divisor /= 10;
        }
    }

    private void EmitSevenSegmentDigit(
        int digit,
        Vector3 centerCm,
        float heightCm,
        float widthCm,
        float thicknessCm,
        Vector4 color,
        int stableId)
    {
        byte segments = digit switch
        {
            0 => 0b011_1111,
            1 => 0b000_0110,
            2 => 0b101_1011,
            3 => 0b100_1111,
            4 => 0b110_0110,
            5 => 0b110_1101,
            6 => 0b111_1101,
            7 => 0b000_0111,
            8 => 0b111_1111,
            9 => 0b110_1111,
            _ => throw new ArgumentOutOfRangeException(nameof(digit), digit, "A decimal digit must be inside [0, 9].")
        };

        float halfWidthCm = widthCm * 0.5f;
        float halfHeightCm = heightCm * 0.5f;
        float innerHalfHeightCm = heightCm * 0.08f;
        EmitDigitSegment(segments, 0, centerCm + new Vector3(-halfWidthCm, halfHeightCm, 0f), centerCm + new Vector3(halfWidthCm, halfHeightCm, 0f), thicknessCm, color, stableId);
        EmitDigitSegment(segments, 1, centerCm + new Vector3(halfWidthCm, innerHalfHeightCm, 0f), centerCm + new Vector3(halfWidthCm, halfHeightCm, 0f), thicknessCm, color, stableId + 1);
        EmitDigitSegment(segments, 2, centerCm + new Vector3(halfWidthCm, -halfHeightCm, 0f), centerCm + new Vector3(halfWidthCm, -innerHalfHeightCm, 0f), thicknessCm, color, stableId + 2);
        EmitDigitSegment(segments, 3, centerCm + new Vector3(-halfWidthCm, -halfHeightCm, 0f), centerCm + new Vector3(halfWidthCm, -halfHeightCm, 0f), thicknessCm, color, stableId + 3);
        EmitDigitSegment(segments, 4, centerCm + new Vector3(-halfWidthCm, -halfHeightCm, 0f), centerCm + new Vector3(-halfWidthCm, -innerHalfHeightCm, 0f), thicknessCm, color, stableId + 4);
        EmitDigitSegment(segments, 5, centerCm + new Vector3(-halfWidthCm, innerHalfHeightCm, 0f), centerCm + new Vector3(-halfWidthCm, halfHeightCm, 0f), thicknessCm, color, stableId + 5);
        EmitDigitSegment(segments, 6, centerCm + new Vector3(-halfWidthCm, 0f, 0f), centerCm + new Vector3(halfWidthCm, 0f, 0f), thicknessCm, color, stableId + 6);
    }

    private void EmitDigitSegment(
        byte segments,
        int bit,
        Vector3 startCm,
        Vector3 endCm,
        float thicknessCm,
        Vector4 color,
        int stableId)
    {
        if ((segments & (1 << bit)) != 0)
        {
            AddLinePrimitive(startCm, endCm, thicknessCm, color, stableId);
        }
    }

    private void EmitWindTunnelSelection()
    {
        if (!_runtime.TryGetWindTunnelZoneVisual(
                out Vector3 centerCm,
                out Vector3 sizeCm,
                out Vector3 direction))
        {
            return;
        }

        AddPrimitive(
            _cubeMeshId,
            ToMeters(centerCm),
            Quaternion.Identity,
            ToMeters(sizeCm),
            new Vector4(0.18f, 0.78f, 0.92f, 0.13f),
            970_000);
        float arrowHalfLengthCm = sizeCm.Z * 0.35f;
        AddLinePrimitive(
            centerCm - (direction * arrowHalfLengthCm),
            centerCm + (direction * arrowHalfLengthCm),
            MathF.Max(5f, _runtime.ActiveConfig.BodySizeCm * 0.08f),
            new Vector4(1f, 0.78f, 0.18f, 0.95f),
            970_001);
    }

    private void EmitWheelLabDebug()
    {
        if (_runtime.ActiveScene != Physics3DShowcaseScene.WheelLab)
        {
            return;
        }

        Physics3DWheelLabShowcaseConfig config = _runtime.ActiveConfig.WheelLab;
        for (int i = 0; i < 4; i++)
        {
            if (!_runtime.TryGetWheelLabDebugVisual(i, out Physics3DWheelLabDebugVisual wheel))
            {
                throw new InvalidOperationException($"Wheel Lab debug visual {i} is unavailable.");
            }

            int stableId = 980_000 + (i * 8);
            AddLinePrimitive(
                wheel.SuspensionOriginCm,
                wheel.WheelCenterCm,
                config.DebugLineThicknessCm,
                wheel.CompressionCm > 0f
                    ? new Vector4(0.18f, 0.88f, 0.94f, 0.95f)
                    : new Vector4(0.34f, 0.54f, 0.62f, 0.72f),
                stableId);

            float markerDiameterCm = wheel.Mode switch
            {
                Vehicle3DWheelKind.Physical => wheel.WheelRadiusCm * 0.8f,
                Vehicle3DWheelKind.Box => wheel.WheelRadiusCm * 1.5f,
                Vehicle3DWheelKind.Scanning => wheel.WheelRadiusCm * 2f,
                _ => throw new InvalidOperationException($"Unsupported Wheel Lab visual mode '{wheel.Mode}'.")
            };
            Vector4 markerColor = wheel.Mode switch
            {
                Vehicle3DWheelKind.Physical => new Vector4(0.82f, 0.90f, 0.96f, 0.95f),
                Vehicle3DWheelKind.Box => new Vector4(1f, 0.52f, 0.08f, 0.58f),
                Vehicle3DWheelKind.Scanning => new Vector4(0.20f, 0.72f, 0.96f, config.DebugScanningWheelAlpha),
                _ => throw new InvalidOperationException($"Unsupported Wheel Lab visual mode '{wheel.Mode}'.")
            };
            AddPrimitive(
                wheel.Mode == Vehicle3DWheelKind.Box ? _cubeMeshId : _sphereMeshId,
                ToMeters(wheel.WheelCenterCm),
                Quaternion.Identity,
                ToMeters(new Vector3(markerDiameterCm)),
                markerColor,
                stableId + 1);

            if (!wheel.Grounded)
            {
                continue;
            }

            AddPrimitive(
                _sphereMeshId,
                ToMeters(wheel.ContactPointCm),
                Quaternion.Identity,
                ToMeters(new Vector3(config.DebugContactMarkerDiameterCm)),
                new Vector4(1f, 0.82f, 0.16f, 1f),
                stableId + 2);
            AddLinePrimitive(
                wheel.ContactPointCm,
                wheel.ContactPointCm + (wheel.ContactNormal * config.DebugNormalLengthCm),
                config.DebugLineThicknessCm,
                new Vector4(0.20f, 0.92f, 0.46f, 0.95f),
                stableId + 3);

            float slipLengthCm = MathF.Min(
                wheel.SlipVelocityCmPerSecond.Length() * config.DebugSlipScaleSeconds,
                config.DebugMaximumSlipLengthCm);
            if (slipLengthCm <= config.DebugLineThicknessCm)
            {
                continue;
            }

            Vector3 slipDirection = Vector3.Normalize(wheel.SlipVelocityCmPerSecond);
            AddLinePrimitive(
                wheel.ContactPointCm,
                wheel.ContactPointCm + (slipDirection * slipLengthCm),
                config.DebugLineThicknessCm,
                new Vector4(0.98f, 0.24f, 0.20f, 0.95f),
                stableId + 4);
        }
    }

    private void EmitConstraintForgeLabels()
    {
        if (_runtime.ActiveScene != Physics3DShowcaseScene.ConstraintForge)
        {
            return;
        }

        Physics3DConstraintForgeShowcaseConfig config = _runtime.ActiveConfig.ConstraintForge;
        for (int labelIndex = 0; labelIndex < _runtime.ConstraintForgeExhibitLabelCount; labelIndex++)
        {
            if (!_runtime.TryGetConstraintForgeExhibitLabel(labelIndex, out int number, out Vector3 positionCm))
            {
                throw new InvalidOperationException($"Constraint Forge exhibit label {labelIndex} is unavailable.");
            }

            EmitSevenSegmentDigit(
                number,
                positionCm,
                config.LabelHeightCm,
                config.LabelHeightCm * 0.58f,
                config.LabelThicknessCm,
                new Vector4(1f, 0.82f, 0.18f, 1f),
                990_000 + (labelIndex * 7));
        }
    }

    private void AddLinePrimitive(
        Vector3 startCm,
        Vector3 endCm,
        float thicknessCm,
        Vector4 color,
        int stableId)
    {
        Vector3 deltaCm = endCm - startCm;
        float lengthCm = deltaCm.Length();
        if (lengthCm <= 1e-5f)
        {
            return;
        }

        AddPrimitive(
            _cubeMeshId,
            ToMeters((startCm + endCm) * 0.5f),
            RotationFromUnitX(deltaCm / lengthCm),
            ToMeters(new Vector3(lengthCm, thicknessCm, thicknessCm)),
            color,
            stableId);
    }

    private void EmitQueryVolume(
        in Physics3DShowcaseQueryVisual query,
        Vector3 positionCm,
        Vector4 color,
        int stableId,
        float sizeScale = 1f)
    {
        switch (query.Kind)
        {
            case Physics3DShowcaseQueryKind.Ray:
                AddPrimitive(
                    _sphereMeshId,
                    ToMeters(positionCm),
                    Quaternion.Identity,
                    new Vector3(0.10f),
                    color,
                    stableId);
                break;
            case Physics3DShowcaseQueryKind.BoxCast:
            case Physics3DShowcaseQueryKind.BoxOverlap:
                AddPrimitive(
                    _cubeMeshId,
                    ToMeters(positionCm),
                    Quaternion.Identity,
                    ToMeters(query.SizeCm * sizeScale),
                    color,
                    stableId);
                break;
            case Physics3DShowcaseQueryKind.SphereCast:
            case Physics3DShowcaseQueryKind.SphereOverlap:
                AddPrimitive(
                    _sphereMeshId,
                    ToMeters(positionCm),
                    Quaternion.Identity,
                    ToMeters(query.SizeCm * sizeScale),
                    color,
                    stableId);
                break;
            case Physics3DShowcaseQueryKind.CapsuleCast:
            case Physics3DShowcaseQueryKind.CapsuleOverlap:
                EmitCapsule(
                    positionCm,
                    Quaternion.Identity,
                    query.SizeCm.X * sizeScale,
                    (query.SizeCm.Y - query.SizeCm.X) * sizeScale,
                    color,
                    stableId);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Physics3D query visual '{query.Kind}'.");
        }
    }

    private void EmitCapsule(
        Vector3 centerCm,
        Quaternion orientation,
        float diameterCm,
        float cylinderLengthCm,
        Vector4 color,
        int stableId)
    {
        Vector3 localOffset = Vector3.Transform(Vector3.UnitY * (cylinderLengthCm * 0.5f), orientation);
        AddPrimitive(
            _cubeMeshId,
            ToMeters(centerCm),
            orientation,
            ToMeters(new Vector3(diameterCm, cylinderLengthCm, diameterCm)),
            color,
            stableId);
        Vector3 sphereScale = ToMeters(new Vector3(diameterCm));
        AddPrimitive(
            _sphereMeshId,
            ToMeters(centerCm + localOffset),
            orientation,
            sphereScale,
            color,
            stableId + 1);
        AddPrimitive(
            _sphereMeshId,
            ToMeters(centerCm - localOffset),
            orientation,
            sphereScale,
            color,
            stableId + 2);
    }

    private void AddPrimitive(
        int meshAssetId,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        Vector4 color,
        int stableId)
    {
        var proxy = new PresentationVisualProxy
        {
            ProxyKind = PresentationVisualProxyKind.Entity,
            MeshAssetId = meshAssetId,
            Position = position,
            Rotation = rotation,
            Scale = scale,
            Color = color,
            StableId = stableId,
            RenderPath = VisualRenderPath.StaticMesh,
            Mobility = VisualMobility.Movable,
            Flags = VisualRuntimeFlags.Visible,
            Visibility = VisualVisibility.Visible,
            LOD = LODLevel.High
        };
        _requests.Add(PresentationRequest.FromVisualProxy(default, in proxy));
    }

    private static Vector3 ToMeters(Vector3 valueCm) => valueCm * 0.01f;

    private static Quaternion RotationFromUnitX(Vector3 direction)
    {
        Vector3 normalized = Vector3.Normalize(direction);
        float dot = Math.Clamp(Vector3.Dot(Vector3.UnitX, normalized), -1f, 1f);
        if (dot > 0.99999f)
        {
            return Quaternion.Identity;
        }

        if (dot < -0.99999f)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        }

        Vector3 axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitX, normalized));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
    }

    private static Quaternion RotationFromUnitY(Vector3 direction)
    {
        Vector3 normalized = Vector3.Normalize(direction);
        float dot = Math.Clamp(Vector3.Dot(Vector3.UnitY, normalized), -1f, 1f);
        if (dot > 0.99999f)
        {
            return Quaternion.Identity;
        }

        if (dot < -0.99999f)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        }

        Vector3 axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normalized));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
    }
}
