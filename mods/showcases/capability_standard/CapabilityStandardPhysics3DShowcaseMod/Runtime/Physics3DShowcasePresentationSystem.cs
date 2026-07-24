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
        if (_runtime.ActiveScene == Physics3DShowcaseScene.Determinism)
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
        if (_runtime.ActiveScene != Physics3DShowcaseScene.Queries)
        {
            return;
        }

        for (int i = 0; i < 7; i++)
        {
            if (!_runtime.TryGetQueryVisual(i, out Physics3DShowcaseQueryVisual query))
            {
                throw new InvalidOperationException($"Physics3D query visual {i} is unavailable in Queries scene.");
            }

            Vector4 color = query.HitCount > 0
                ? new Vector4(0.20f, 0.90f, 0.62f, 0.58f)
                : new Vector4(0.96f, 0.30f, 0.30f, 0.58f);
            int stableId = 940_000 + (i * 8);
            if (!query.IsOverlap)
            {
                Vector3 direction = Vector3.Normalize(query.Direction);
                Vector3 midpointCm = query.OriginCm + (direction * query.DistanceCm * 0.5f);
                AddPrimitive(
                    _cubeMeshId,
                    ToMeters(midpointCm),
                    RotationFromUnitX(direction),
                    new Vector3(query.DistanceCm * 0.01f, 0.035f, 0.035f),
                    color,
                    stableId);
                EmitQueryVolume(in query, query.OriginCm, color, stableId + 1);
                EmitQueryVolume(in query, query.OriginCm + (direction * query.DistanceCm), color, stableId + 4);
            }
            else
            {
                EmitQueryVolume(in query, query.OriginCm, color, stableId + 1);
            }

            if (query.HasFirstHit)
            {
                AddPrimitive(
                    _sphereMeshId,
                    ToMeters(query.FirstHitPositionCm),
                    Quaternion.Identity,
                    new Vector3(0.18f),
                    new Vector4(1f, 0.92f, 0.35f, 1f),
                    stableId + 7);
            }
        }
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
}
