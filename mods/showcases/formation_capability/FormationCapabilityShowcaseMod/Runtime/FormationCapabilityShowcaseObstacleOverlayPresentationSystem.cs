using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;

namespace FormationCapabilityShowcaseMod.Runtime;

internal sealed class FormationCapabilityShowcaseObstacleOverlayPresentationSystem : ISystem<float>
{
    private static readonly QueryDescription ObstacleOverlayQuery = new QueryDescription()
        .WithAll<FormationCapabilityShowcaseObstacleOverlay, VisualTransform, PresentationStableId>()
        .WithNone<PresentationDestroyPending>();

    private readonly GameEngine _engine;
    private readonly FormationCapabilityShowcaseRuntime _runtime;
    private readonly GroundOverlayBuffer _overlays;
    private readonly IVisualHeightmap _heightmap;
    private readonly int _overlayCapacity;
    private readonly List<int> _currentStableIds;
    private readonly List<int> _previousStableIds;
    private readonly HashSet<int> _currentStableIdSet;
    private readonly Dictionary<int, ObstacleOverlayEmissionState> _emittedStateByStableId;

    public FormationCapabilityShowcaseObstacleOverlayPresentationSystem(GameEngine engine, FormationCapabilityShowcaseRuntime runtime, int overlayCapacity)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        if (overlayCapacity < 0)
        {
            throw new InvalidOperationException("Formation Capability obstacle overlay requires config-derived overlay capacity >= 0.");
        }

        _overlayCapacity = overlayCapacity;
        _currentStableIds = new List<int>(_overlayCapacity);
        _previousStableIds = new List<int>(_overlayCapacity);
        _currentStableIdSet = new HashSet<int>(_overlayCapacity);
        _emittedStateByStableId = new Dictionary<int, ObstacleOverlayEmissionState>(_overlayCapacity);
        _overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
            ?? throw new InvalidOperationException("Formation Capability obstacle overlay requires GroundOverlayBuffer.");
        _heightmap = engine.GetService(CoreServiceKeys.VisualHeightmap)
            ?? throw new InvalidOperationException("Formation Capability obstacle overlay requires VisualHeightmap.");
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtime.IsCurrentShowcaseMap(_engine))
        {
            RemovePreviousOverlays();
            return;
        }

        _currentStableIds.Clear();
        _currentStableIdSet.Clear();
        foreach (ref var chunk in _engine.World.Query(in ObstacleOverlayQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<FormationCapabilityShowcaseObstacleOverlay> overlays = chunk.GetSpan<FormationCapabilityShowcaseObstacleOverlay>();
            Span<VisualTransform> transforms = chunk.GetSpan<VisualTransform>();
            Span<PresentationStableId> stableIds = chunk.GetSpan<PresentationStableId>();

            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                EmitOverlay(entity, in overlays[index], in transforms[index], stableIds[index].Value);
            }
        }

        RemoveStaleOverlays();
    }

    private void EmitOverlay(
        Entity entity,
        in FormationCapabilityShowcaseObstacleOverlay overlay,
        in VisualTransform transform,
        int ownerStableId)
    {
        if (ownerStableId <= 0)
        {
            throw new InvalidOperationException("Formation Capability obstacle overlay requires a positive PresentationStableId.");
        }

        if (!(overlay.RadiusCm > 0f))
        {
            throw new InvalidOperationException($"Formation Capability obstacle overlay entity {entity.Id} requires RadiusCm > 0.");
        }

        if (!(overlay.BorderWidthCm > 0f))
        {
            throw new InvalidOperationException($"Formation Capability obstacle overlay entity {entity.Id} requires BorderWidthCm > 0.");
        }

        int stableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(
            ownerStableId,
            FormationCapabilityShowcaseObstacleOverlayVisualSlots.ObstacleRing,
            AssetKind.GroundOverlay,
            (int)GroundOverlayShape.Ring);
        ObstacleOverlayEmissionState nextState = ObstacleOverlayEmissionState.From(in overlay, in transform);
        if (_emittedStateByStableId.TryGetValue(stableId, out ObstacleOverlayEmissionState previousState) &&
            previousState.Equals(nextState))
        {
            TrackStableId(stableId);
            return;
        }

        RequireEmissionStateCapacity(stableId);
        _emittedStateByStableId[stableId] = nextState;
        Vector3 center = ProjectToGround(transform.Position, overlay.HeightOffsetM);
        RequireStableIdCapacity();
        var item = new GroundOverlayItem
        {
            StableId = stableId,
            Shape = GroundOverlayShape.Ring,
            Center = center,
            Radius = WorldUnits.CmToM(overlay.RadiusCm),
            InnerRadius = 0f,
            FillColor = overlay.FillColor,
            BorderColor = overlay.BorderColor,
            BorderWidth = WorldUnits.CmToM(overlay.BorderWidthCm),
        };

        if (!_overlays.Upsert(in item))
        {
            throw new InvalidOperationException("GroundOverlayBuffer overflowed while emitting Formation Capability obstacle overlay.");
        }

        TrackStableId(stableId);
    }

    private Vector3 ProjectToGround(in Vector3 position, float heightOffsetM)
    {
        float worldXCm = WorldUnits.MToCm(position.X);
        float worldYCm = WorldUnits.MToCm(position.Z);
        if (!_heightmap.TrySampleHeightCm(worldXCm, worldYCm, out float heightCm))
        {
            throw new InvalidOperationException(
                $"Formation Capability obstacle overlay requires visual heightmap coverage at world cm ({worldXCm:0.##}, {worldYCm:0.##}).");
        }

        return new Vector3(position.X, WorldUnits.CmToM(heightCm) + heightOffsetM, position.Z);
    }

    private void RemovePreviousOverlays()
    {
        for (int i = 0; i < _previousStableIds.Count; i++)
        {
            _overlays.Remove(_previousStableIds[i]);
        }

        _previousStableIds.Clear();
        _currentStableIds.Clear();
        _currentStableIdSet.Clear();
        _emittedStateByStableId.Clear();
    }

    private void RemoveStaleOverlays()
    {
        for (int i = 0; i < _previousStableIds.Count; i++)
        {
            int stableId = _previousStableIds[i];
            if (!_currentStableIdSet.Contains(stableId))
            {
                _overlays.Remove(stableId);
                _emittedStateByStableId.Remove(stableId);
            }
        }

        _previousStableIds.Clear();
        CopyCurrentStableIdsToPrevious();
    }

    private void TrackStableId(int stableId)
    {
        RequireStableIdCapacity();
        _currentStableIds.Add(stableId);
        _currentStableIdSet.Add(stableId);
    }

    private void RequireStableIdCapacity()
    {
        if (_currentStableIds.Count >= _overlayCapacity)
        {
            throw new InvalidOperationException(
                $"Formation Capability obstacle overlay stable id count exceeds config-derived obstacle overlay capacity {_overlayCapacity}.");
        }
    }

    private void RequireEmissionStateCapacity(int stableId)
    {
        if (!_emittedStateByStableId.ContainsKey(stableId) && _emittedStateByStableId.Count >= _overlayCapacity)
        {
            throw new InvalidOperationException(
                $"Formation Capability obstacle overlay emission-state count exceeds config-derived obstacle overlay capacity {_overlayCapacity}.");
        }
    }

    private void CopyCurrentStableIdsToPrevious()
    {
        for (int i = 0; i < _currentStableIds.Count; i++)
        {
            if (_previousStableIds.Count >= _overlayCapacity)
            {
                throw new InvalidOperationException(
                    $"Formation Capability obstacle overlay previous stable id count exceeds config-derived obstacle overlay capacity {_overlayCapacity}.");
            }

            _previousStableIds.Add(_currentStableIds[i]);
        }
    }

    private readonly struct ObstacleOverlayEmissionState : IEquatable<ObstacleOverlayEmissionState>
    {
        public readonly float CenterX;
        public readonly float CenterY;
        public readonly float CenterZ;
        public readonly float RadiusCm;
        public readonly float HeightOffsetM;
        public readonly float BorderWidthCm;
        public readonly Vector4 FillColor;
        public readonly Vector4 BorderColor;

        private ObstacleOverlayEmissionState(
            in FormationCapabilityShowcaseObstacleOverlay overlay,
            in VisualTransform transform)
        {
            CenterX = transform.Position.X;
            CenterY = transform.Position.Y;
            CenterZ = transform.Position.Z;
            RadiusCm = overlay.RadiusCm;
            HeightOffsetM = overlay.HeightOffsetM;
            BorderWidthCm = overlay.BorderWidthCm;
            FillColor = overlay.FillColor;
            BorderColor = overlay.BorderColor;
        }

        public static ObstacleOverlayEmissionState From(
            in FormationCapabilityShowcaseObstacleOverlay overlay,
            in VisualTransform transform)
        {
            return new ObstacleOverlayEmissionState(in overlay, in transform);
        }

        public bool Equals(ObstacleOverlayEmissionState other)
        {
            return CenterX.Equals(other.CenterX) &&
                CenterY.Equals(other.CenterY) &&
                CenterZ.Equals(other.CenterZ) &&
                RadiusCm.Equals(other.RadiusCm) &&
                HeightOffsetM.Equals(other.HeightOffsetM) &&
                BorderWidthCm.Equals(other.BorderWidthCm) &&
                FillColor.Equals(other.FillColor) &&
                BorderColor.Equals(other.BorderColor);
        }

        public override bool Equals(object? obj) => obj is ObstacleOverlayEmissionState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(CenterX, CenterY, CenterZ, RadiusCm, HeightOffsetM, BorderWidthCm, FillColor, BorderColor);
    }
}

internal static class FormationCapabilityShowcaseObstacleOverlayVisualSlots
{
    public const int ObstacleRing = 0;
}
