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
        if (_cubeMeshId <= 0 || _sphereMeshId <= 0)
        {
            throw new InvalidOperationException("Physics3D showcase requires registered cube and sphere meshes.");
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
        float refreshInterval = 1f / _runtime.ActiveConfig.PanelRefreshHz;
        _panelRefreshAccumulator += t;
        if (!_hasPanelState || _panelRefreshAccumulator >= refreshInterval)
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

        Vector4 color = _runtime.ScannerQueryFailed
            ? new Vector4(0.96f, 0.30f, 0.30f, 0.42f)
            : _runtime.ScannerHasResult
                ? new Vector4(0.20f, 0.90f, 0.62f, 0.38f)
                : new Vector4(1.00f, 0.72f, 0.18f, 0.38f);
        const int stableId = 940_000;
        EmitSelectedQueryVolume(in query, color, stableId);

        for (int hitIndex = 0; hitIndex < query.HitCount; hitIndex++)
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
            int hitStableId = 950_000 + (hitIndex * 2);
            AddPrimitive(
                _sphereMeshId,
                ToMeters(hit.PositionCm),
                Quaternion.Identity,
                new Vector3(hitIndex == 0 ? 0.18f : 0.12f),
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
        }
    }

    private void EmitSelectedQueryVolume(
        in Physics3DShowcaseQueryVisual query,
        Vector4 color,
        int stableId)
    {
        if (query.IsOverlap)
        {
            EmitQueryVolume(in query, query.OriginCm, color, stableId);
            return;
        }

        Vector3 direction = Vector3.Normalize(query.Direction);
        Vector3 midpointCm = query.OriginCm + (direction * query.DistanceCm * 0.5f);
        switch (query.Kind)
        {
            case Physics3DShowcaseQueryKind.Ray:
                AddLinePrimitive(
                    query.OriginCm,
                    query.OriginCm + (direction * query.DistanceCm),
                    MathF.Max(2f, _runtime.ActiveConfig.BodySizeCm * 0.04f),
                    color,
                    stableId);
                break;
            case Physics3DShowcaseQueryKind.BoxCast:
                AddPrimitive(
                    _cubeMeshId,
                    ToMeters(midpointCm),
                    RotationFromUnitX(direction),
                    ToMeters(new Vector3(
                        query.DistanceCm + query.SizeCm.X,
                        query.SizeCm.Y,
                        query.SizeCm.Z)),
                    color,
                    stableId);
                break;
            case Physics3DShowcaseQueryKind.SphereCast:
                EmitCapsule(
                    midpointCm,
                    RotationFromUnitY(direction),
                    query.SizeCm.X,
                    query.DistanceCm,
                    color,
                    stableId);
                break;
            case Physics3DShowcaseQueryKind.CapsuleCast:
            {
                float maximumSpacingCm = _runtime.ActiveConfig.ScannerRange.SweepVisualMaximumSpacingCm;
                int segmentCount = Math.Max(1, (int)MathF.Ceiling(query.DistanceCm / maximumSpacingCm));
                for (int sample = 0; sample <= segmentCount; sample++)
                {
                    float distanceCm = query.DistanceCm * (sample / (float)segmentCount);
                    EmitQueryVolume(
                        in query,
                        query.OriginCm + (direction * distanceCm),
                        color,
                        stableId + (sample * 3));
                }
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported Scanner Range cast visual '{query.Kind}'.");
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

            float markerDiameterCm = wheel.Mode == Vehicle3DWheelKind.Scanning
                ? wheel.WheelRadiusCm * 2f
                : config.DebugContactMarkerDiameterCm;
            Vector4 markerColor = wheel.Mode == Vehicle3DWheelKind.Scanning
                ? new Vector4(0.20f, 0.72f, 0.96f, config.DebugScanningWheelAlpha)
                : new Vector4(0.70f, 0.78f, 0.86f, 0.75f);
            AddPrimitive(
                _sphereMeshId,
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
        int stableId)
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
                    ToMeters(query.SizeCm),
                    color,
                    stableId);
                break;
            case Physics3DShowcaseQueryKind.SphereCast:
            case Physics3DShowcaseQueryKind.SphereOverlap:
                AddPrimitive(
                    _sphereMeshId,
                    ToMeters(positionCm),
                    Quaternion.Identity,
                    ToMeters(query.SizeCm),
                    color,
                    stableId);
                break;
            case Physics3DShowcaseQueryKind.CapsuleCast:
            case Physics3DShowcaseQueryKind.CapsuleOverlap:
                EmitCapsule(
                    positionCm,
                    Quaternion.Identity,
                    query.SizeCm.X,
                    query.SizeCm.Y - query.SizeCm.X,
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
