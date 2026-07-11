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
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;

namespace FormationCapabilityShowcaseMod.Runtime;

internal sealed class FormationCapabilityShowcaseFormationOutlinePresentationSystem : ISystem<float>
{
    private const string AzureOutlineKey = "formation_capability.formation_outline.azure";
    private const string CrimsonOutlineKey = "formation_capability.formation_outline.crimson";

    private static readonly QueryDescription FormationOutlineQuery = new QueryDescription()
        .WithAll<FormationCapabilityShowcaseFormationAgent, FormationCapabilityShowcaseFormationState, FormationCapabilityShowcaseFormationOutline, VisualTransform, PresentationStableId>()
        .WithNone<PresentationDestroyPending, SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly FormationCapabilityShowcaseRuntime _runtime;
    private readonly PresentationWorldFactPublisher _facts;
    private readonly IVisualHeightmap _heightmap;
    private readonly int _stableIdCapacity;
    private readonly int _ownerCapacity;
    private readonly List<int> _currentStableIds;
    private readonly List<int> _previousStableIds;
    private readonly HashSet<int> _currentStableIdSet;
    private readonly HashSet<int> _currentOwnerStableIds;
    private readonly List<int> _staleOwnerStableIds;
    private readonly Dictionary<int, OutlineEmissionState> _emittedStateByOwnerStableId;
    private readonly Dictionary<int, string> _factKeyByStableId;
    private int _lastPublishedFormationOutlineCount = -1;

    public FormationCapabilityShowcaseFormationOutlinePresentationSystem(GameEngine engine, FormationCapabilityShowcaseRuntime runtime, FormationCapabilityShowcaseConfig config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ArgumentNullException.ThrowIfNull(config);
        _stableIdCapacity = config.FormationOutlineSplineCapacity;
        _ownerCapacity = config.FormationOutlineOwnerCapacity;
        if (_stableIdCapacity <= 0)
        {
            throw new InvalidOperationException("Formation Capability formation outline requires config-derived FormationOutlineSplineCapacity > 0.");
        }

        if (_ownerCapacity <= 0)
        {
            throw new InvalidOperationException("Formation Capability formation outline requires config-derived FormationOutlineOwnerCapacity > 0.");
        }

        _currentStableIds = new List<int>(_stableIdCapacity);
        _previousStableIds = new List<int>(_stableIdCapacity);
        _currentStableIdSet = new HashSet<int>(_stableIdCapacity);
        _currentOwnerStableIds = new HashSet<int>(_ownerCapacity);
        _staleOwnerStableIds = new List<int>(_ownerCapacity);
        _emittedStateByOwnerStableId = new Dictionary<int, OutlineEmissionState>(_ownerCapacity);
        _factKeyByStableId = new Dictionary<int, string>(_stableIdCapacity);
        if (!PresentationWorldFactPublisher.TryCreate(engine.GlobalContext, out _facts))
        {
            throw new InvalidOperationException("Formation Capability formation outline presentation requires PresentationEventStream.");
        }

        _heightmap = engine.GetService(CoreServiceKeys.VisualHeightmap)
            ?? throw new InvalidOperationException("Formation Capability formation outline requires VisualHeightmap.");
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtime.IsCurrentShowcaseMap(_engine))
        {
            RemovePreviousSplines();
            return;
        }

        _currentStableIds.Clear();
        _currentStableIdSet.Clear();
        _currentOwnerStableIds.Clear();
        int emitted = 0;
        foreach (ref var chunk in _engine.World.Query(in FormationOutlineQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<FormationCapabilityShowcaseFormationState> states = chunk.GetSpan<FormationCapabilityShowcaseFormationState>();
            Span<FormationCapabilityShowcaseFormationOutline> outlines = chunk.GetSpan<FormationCapabilityShowcaseFormationOutline>();
            Span<VisualTransform> transforms = chunk.GetSpan<VisualTransform>();
            Span<PresentationStableId> stableIds = chunk.GetSpan<PresentationStableId>();

            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                emitted += EmitOutline(
                    entity,
                    in states[index],
                    in outlines[index],
                    in transforms[index],
                    stableIds[index].Value);
            }
        }

        RemoveStaleSplines();
        RemoveStaleEmissionStates();
        PublishFormationOutlineCountIfChanged(emitted);
    }

    private void PublishFormationOutlineCountIfChanged(int emitted)
    {
        if (_lastPublishedFormationOutlineCount == emitted)
        {
            return;
        }

        _lastPublishedFormationOutlineCount = emitted;
        _engine.GlobalContext[FormationCapabilityShowcaseContextKeys.FormationOutlineCount] = emitted;
    }

    private int EmitOutline(
        Entity entity,
        in FormationCapabilityShowcaseFormationState state,
        in FormationCapabilityShowcaseFormationOutline outline,
        in VisualTransform transform,
        int ownerStableId)
    {
        if (ownerStableId <= 0)
        {
            throw new InvalidOperationException("Formation Capability formation outline requires a positive PresentationStableId.");
        }

        TrackOwnerStableId(ownerStableId);
        OutlineEmissionState nextState = OutlineEmissionState.From(in state, in outline, in transform);
        if (_emittedStateByOwnerStableId.TryGetValue(ownerStableId, out OutlineEmissionState previousState) &&
            previousState.Equals(nextState, outline.EmissionPositionEpsilonM, outline.EmissionFacingEpsilonRadians))
        {
            TrackExistingStableIds(ownerStableId, in outline);
            return OutlineStableIdCount(in outline);
        }

        RequireEmissionStateCapacity(ownerStableId);
        _emittedStateByOwnerStableId[ownerStableId] = nextState;
        return outline.Shape switch
        {
            FormationCapabilityShowcaseFormationOutlineShape.Rectangle => EmitRectangle(entity, ownerStableId, in state, in outline, in transform),
            FormationCapabilityShowcaseFormationOutlineShape.Circle => EmitCircle(entity, ownerStableId, in state, in outline, in transform),
            _ => throw new InvalidOperationException($"Formation Capability formation outline has unsupported shape {outline.Shape}."),
        };
    }

    private int EmitRectangle(
        Entity entity,
        int ownerStableId,
        in FormationCapabilityShowcaseFormationState state,
        in FormationCapabilityShowcaseFormationOutline outline,
        in VisualTransform transform)
    {
        float widthM = WorldUnits.CmToM(outline.WidthCm);
        float depthM = WorldUnits.CmToM(outline.DepthCm);
        float edgeWidthM = WorldUnits.CmToM(outline.EdgeLineWidthCm);
        Vector3 center = ResolveCenter(in transform, outline.HeightOffsetM);
        Vector2 forward = ResolveForward(state.FacingRad);
        Vector2 lateral = new(-forward.Y, forward.X);
        Vector3 forward3 = ToVisualVector(forward);
        Vector3 lateral3 = ToVisualVector(lateral);
        Vector3 frontCenter = center + (forward3 * (depthM * 0.5f));
        Vector3 backCenter = center - (forward3 * (depthM * 0.5f));
        Vector3 leftCenter = center - (lateral3 * (widthM * 0.5f));
        Vector3 rightCenter = center + (lateral3 * (widthM * 0.5f));

        int count = 0;
        count += AddSampledLine(
            entity,
            ownerStableId,
            FormationCapabilityShowcaseFormationOutlineSegment.RectangleFront,
            frontCenter - (lateral3 * (widthM * 0.5f)),
            frontCenter + (lateral3 * (widthM * 0.5f)),
            edgeWidthM,
            in outline);
        count += AddSampledLine(
            entity,
            ownerStableId,
            FormationCapabilityShowcaseFormationOutlineSegment.RectangleBack,
            backCenter - (lateral3 * (widthM * 0.5f)),
            backCenter + (lateral3 * (widthM * 0.5f)),
            edgeWidthM,
            in outline);
        count += AddSampledLine(
            entity,
            ownerStableId,
            FormationCapabilityShowcaseFormationOutlineSegment.RectangleLeft,
            leftCenter - (forward3 * (depthM * 0.5f)),
            leftCenter + (forward3 * (depthM * 0.5f)),
            edgeWidthM,
            in outline);
        count += AddSampledLine(
            entity,
            ownerStableId,
            FormationCapabilityShowcaseFormationOutlineSegment.RectangleRight,
            rightCenter - (forward3 * (depthM * 0.5f)),
            rightCenter + (forward3 * (depthM * 0.5f)),
            edgeWidthM,
            in outline);
        count += EmitFrontIndicator(entity, ownerStableId, center, forward, in outline);
        return count;
    }

    private int EmitCircle(
        Entity entity,
        int ownerStableId,
        in FormationCapabilityShowcaseFormationState state,
        in FormationCapabilityShowcaseFormationOutline outline,
        in VisualTransform transform)
    {
        float radiusM = WorldUnits.CmToM(outline.RadiusCm);
        float ringWidthM = WorldUnits.CmToM(outline.CircleRingWidthCm);
        if (radiusM <= 0f || ringWidthM <= 0f)
        {
            throw new InvalidOperationException("Formation Capability circle formation outline requires positive radius and ring width.");
        }

        Vector3 center = ResolveCenter(in transform, outline.HeightOffsetM);
        int count = EmitSampledCircle(entity, ownerStableId, center, radiusM, ringWidthM, in outline);
        count += EmitFrontIndicator(entity, ownerStableId, center, ResolveForward(state.FacingRad), in outline);
        return count;
    }

    private int EmitFrontIndicator(
        Entity entity,
        int ownerStableId,
        Vector3 center,
        Vector2 forward,
        in FormationCapabilityShowcaseFormationOutline outline)
    {
        float lengthM = WorldUnits.CmToM(outline.FrontIndicatorLengthCm);
        if (!(lengthM > 0f))
        {
            return 0;
        }

        float widthM = WorldUnits.CmToM(outline.FrontIndicatorLineWidthCm);
        Vector3 start = center;
        Vector3 end = center + (ToVisualVector(forward) * lengthM);
        return AddSampledLine(entity, ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment.FrontIndicator, start, end, widthM, in outline);
    }

    private int AddSampledLine(
        Entity entity,
        int ownerStableId,
        FormationCapabilityShowcaseFormationOutlineSegment segment,
        Vector3 start,
        Vector3 end,
        float widthM,
        in FormationCapabilityShowcaseFormationOutline outline)
    {
        if (widthM <= 0f)
        {
            throw new InvalidOperationException("Formation Capability formation outline line segments require positive width.");
        }

        int count = 0;
        Vector3 previous = ProjectToGround(start, outline.HeightOffsetM);
        int sampleCount = outline.CurveSampleCount;
        for (int sample = 1; sample <= sampleCount; sample++)
        {
            float t = sample / (float)sampleCount;
            Vector3 current = ProjectToGround(Vector3.Lerp(start, end, t), outline.HeightOffsetM);
            int stableId = CreateSegmentStableId(ownerStableId, segment, sample - 1, sampleCount);
            RequireStableIdCapacity();
            PublishSplineUpdated(entity, stableId, previous, current, widthM, in outline);

            TrackStableId(stableId);
            previous = current;
            count++;
        }

        return count;
    }

    private int EmitSampledCircle(
        Entity entity,
        int ownerStableId,
        Vector3 center,
        float radiusM,
        float widthM,
        in FormationCapabilityShowcaseFormationOutline outline)
    {
        int count = 0;
        Vector3 previous = ProjectToGround(center + new Vector3(radiusM, 0f, 0f), outline.HeightOffsetM);
        int sampleCount = outline.CurveSampleCount;
        for (int sample = 1; sample <= sampleCount; sample++)
        {
            float angle = MathF.Tau * sample / sampleCount;
            Vector3 current = ProjectToGround(
                center + new Vector3(MathF.Cos(angle) * radiusM, 0f, MathF.Sin(angle) * radiusM),
                outline.HeightOffsetM);
            int stableId = CreateSegmentStableId(ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment.CircleRing, sample - 1, sampleCount);
            RequireStableIdCapacity();
            PublishSplineUpdated(entity, stableId, previous, current, widthM, in outline);

            TrackStableId(stableId);
            previous = current;
            count++;
        }

        return count;
    }

    private void RemovePreviousSplines()
    {
        for (int i = 0; i < _previousStableIds.Count; i++)
        {
            PublishSplineEnded(_previousStableIds[i]);
        }

        _previousStableIds.Clear();
        _currentStableIds.Clear();
        _currentStableIdSet.Clear();
        _currentOwnerStableIds.Clear();
        _staleOwnerStableIds.Clear();
        _emittedStateByOwnerStableId.Clear();
    }

    private void RemoveStaleSplines()
    {
        for (int i = 0; i < _previousStableIds.Count; i++)
        {
            int stableId = _previousStableIds[i];
            if (!_currentStableIdSet.Contains(stableId))
            {
                PublishSplineEnded(stableId);
            }
        }

        _previousStableIds.Clear();
        CopyCurrentStableIdsToPrevious();
    }

    private void PublishSplineEnded(int stableId)
    {
        string key = _factKeyByStableId.TryGetValue(stableId, out string? resolvedKey)
            ? resolvedKey
            : AzureOutlineKey;
        _facts.PublishWorldSplineEnded(key, Entity.Null, stableId);
        _factKeyByStableId.Remove(stableId);
    }

    private void PublishSplineUpdated(
        Entity entity,
        int stableId,
        in Vector3 previous,
        in Vector3 current,
        float widthM,
        in FormationCapabilityShowcaseFormationOutline outline)
    {
        string key = ResolveOutlineKey(in outline);
        if (_factKeyByStableId.TryGetValue(stableId, out string? previousKey) &&
            !string.Equals(previousKey, key, StringComparison.Ordinal))
        {
            _facts.PublishWorldSplineEnded(previousKey, Entity.Null, stableId);
        }

        _facts.PublishWorldSplineUpdated(
            key,
            entity,
            stableId,
            previous,
            current,
            widthM,
            widthM);
        _factKeyByStableId[stableId] = key;
    }

    private void RemoveStaleEmissionStates()
    {
        if (_emittedStateByOwnerStableId.Count == 0)
        {
            return;
        }

        _staleOwnerStableIds.Clear();
        foreach (int ownerStableId in _emittedStateByOwnerStableId.Keys)
        {
            if (!_currentOwnerStableIds.Contains(ownerStableId))
            {
                _staleOwnerStableIds.Add(ownerStableId);
            }
        }

        for (int i = 0; i < _staleOwnerStableIds.Count; i++)
        {
            _emittedStateByOwnerStableId.Remove(_staleOwnerStableIds[i]);
        }
    }

    private void TrackExistingStableIds(int ownerStableId, in FormationCapabilityShowcaseFormationOutline outline)
    {
        if (outline.Shape == FormationCapabilityShowcaseFormationOutlineShape.Rectangle)
        {
            TrackSegmentStableIds(ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment.RectangleFront, in outline);
            TrackSegmentStableIds(ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment.RectangleBack, in outline);
            TrackSegmentStableIds(ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment.RectangleLeft, in outline);
            TrackSegmentStableIds(ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment.RectangleRight, in outline);
        }
        else if (outline.Shape == FormationCapabilityShowcaseFormationOutlineShape.Circle)
        {
            TrackSegmentStableIds(ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment.CircleRing, in outline);
        }
        else
        {
            throw new InvalidOperationException($"Formation Capability formation outline has unsupported shape {outline.Shape}.");
        }

        if (outline.FrontIndicatorLengthCm > 0f)
        {
            TrackSegmentStableIds(ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment.FrontIndicator, in outline);
        }
    }

    private void TrackSegmentStableIds(int ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment segment, in FormationCapabilityShowcaseFormationOutline outline)
    {
        int sampleCount = outline.CurveSampleCount;
        for (int sample = 0; sample < sampleCount; sample++)
        {
            int stableId = CreateSegmentStableId(ownerStableId, segment, sample, sampleCount);
            TrackStableId(stableId);
        }
    }

    private void TrackStableId(int stableId)
    {
        RequireStableIdCapacity();
        _currentStableIds.Add(stableId);
        _currentStableIdSet.Add(stableId);
    }

    private void RequireStableIdCapacity()
    {
        if (_currentStableIds.Count >= _stableIdCapacity)
        {
            throw new InvalidOperationException(
                $"Formation Capability formation outline stable id count exceeds config-derived FormationOutlineSplineCapacity {_stableIdCapacity}.");
        }
    }

    private void TrackOwnerStableId(int ownerStableId)
    {
        if (!_currentOwnerStableIds.Contains(ownerStableId) && _currentOwnerStableIds.Count >= _ownerCapacity)
        {
            throw new InvalidOperationException(
                $"Formation Capability formation outline owner count exceeds config-derived FormationOutlineOwnerCapacity {_ownerCapacity}.");
        }

        _currentOwnerStableIds.Add(ownerStableId);
    }

    private void RequireEmissionStateCapacity(int ownerStableId)
    {
        RemoveStaleEmissionStates();

        if (!_emittedStateByOwnerStableId.ContainsKey(ownerStableId) && _emittedStateByOwnerStableId.Count >= _ownerCapacity)
        {
            throw new InvalidOperationException(
                $"Formation Capability formation outline emission-state count exceeds config-derived FormationOutlineOwnerCapacity {_ownerCapacity}.");
        }
    }

    private void CopyCurrentStableIdsToPrevious()
    {
        for (int i = 0; i < _currentStableIds.Count; i++)
        {
            if (_previousStableIds.Count >= _stableIdCapacity)
            {
                throw new InvalidOperationException(
                    $"Formation Capability formation outline previous stable id count exceeds config-derived FormationOutlineSplineCapacity {_stableIdCapacity}.");
            }

            _previousStableIds.Add(_currentStableIds[i]);
        }
    }

    private static int OutlineStableIdCount(in FormationCapabilityShowcaseFormationOutline outline)
    {
        return FormationCapabilityShowcaseFormationOutlineSegments.CountSplineSegments(in outline);
    }

    private static string ResolveOutlineKey(in FormationCapabilityShowcaseFormationOutline outline)
    {
        return outline.BorderColor.X >= outline.BorderColor.Z
            ? CrimsonOutlineKey
            : AzureOutlineKey;
    }

    private static int CreateSegmentStableId(int ownerStableId, FormationCapabilityShowcaseFormationOutlineSegment segment, int sampleIndex, int sampleCount)
    {
        if (sampleCount <= 0)
        {
            throw new InvalidOperationException("Formation Capability formation outline requires configured CurveSampleCount > 0.");
        }

        if (sampleIndex < 0 || sampleIndex >= sampleCount)
        {
            throw new InvalidOperationException($"Formation Capability formation outline sample index {sampleIndex} is outside configured curve samples {sampleCount}.");
        }

        return PerformerBehaviorRuntimeUtility.ComposeVisualStableId(
            ownerStableId,
            ((int)segment * sampleCount) + sampleIndex,
            AssetKind.Spline,
            (int)segment);
    }

    private static Vector3 ResolveCenter(in VisualTransform transform, float heightOffsetM)
    {
        return new Vector3(transform.Position.X, transform.Position.Y + heightOffsetM, transform.Position.Z);
    }

    private Vector3 ProjectToGround(in Vector3 position, float heightOffsetM)
    {
        float worldXCm = WorldUnits.MToCm(position.X);
        float worldYCm = WorldUnits.MToCm(position.Z);
        if (!_heightmap.TrySampleHeightCm(worldXCm, worldYCm, out float heightCm))
        {
            throw new InvalidOperationException(
                $"Formation Capability formation outline requires visual heightmap coverage at world cm ({worldXCm:0.##}, {worldYCm:0.##}).");
        }

        return new Vector3(position.X, WorldUnits.CmToM(heightCm) + heightOffsetM, position.Z);
    }

    private static Vector2 ResolveForward(float facingRad)
    {
        return Vector2.Normalize(new Vector2(MathF.Cos(facingRad), MathF.Sin(facingRad)));
    }

    private static Vector3 ToVisualVector(in Vector2 logicVector)
    {
        return new Vector3(logicVector.X, 0f, logicVector.Y);
    }

    private readonly struct OutlineEmissionState
    {
        public readonly FormationCapabilityShowcaseFormationOutlineShape Shape;
        public readonly float CenterX;
        public readonly float CenterY;
        public readonly float CenterZ;
        public readonly float FacingRad;
        public readonly float WidthCm;
        public readonly float DepthCm;
        public readonly float RadiusCm;
        public readonly float HeightOffsetM;
        public readonly int CurveSampleCount;
        public readonly float EdgeLineWidthCm;
        public readonly float CircleRingWidthCm;
        public readonly float FrontIndicatorLengthCm;
        public readonly float FrontIndicatorLineWidthCm;
        public readonly Vector4 FillColor;
        public readonly Vector4 BorderColor;

        private OutlineEmissionState(
            FormationCapabilityShowcaseFormationOutlineShape shape,
            float centerX,
            float centerY,
            float centerZ,
            float facingRad,
            in FormationCapabilityShowcaseFormationOutline outline)
        {
            Shape = shape;
            CenterX = centerX;
            CenterY = centerY;
            CenterZ = centerZ;
            FacingRad = facingRad;
            WidthCm = outline.WidthCm;
            DepthCm = outline.DepthCm;
            RadiusCm = outline.RadiusCm;
            HeightOffsetM = outline.HeightOffsetM;
            CurveSampleCount = outline.CurveSampleCount;
            EdgeLineWidthCm = outline.EdgeLineWidthCm;
            CircleRingWidthCm = outline.CircleRingWidthCm;
            FrontIndicatorLengthCm = outline.FrontIndicatorLengthCm;
            FrontIndicatorLineWidthCm = outline.FrontIndicatorLineWidthCm;
            FillColor = outline.FillColor;
            BorderColor = outline.BorderColor;
        }

        public static OutlineEmissionState From(
            in FormationCapabilityShowcaseFormationState state,
            in FormationCapabilityShowcaseFormationOutline outline,
            in VisualTransform transform)
        {
            return new OutlineEmissionState(
                outline.Shape,
                transform.Position.X,
                transform.Position.Y,
                transform.Position.Z,
                state.FacingRad,
                in outline);
        }

        public bool Equals(
            OutlineEmissionState other,
            float positionEpsilonM,
            float facingEpsilonRadians)
        {
            if (!(positionEpsilonM > 0f))
            {
                throw new InvalidOperationException("Formation Capability formation outline requires EmissionPositionEpsilonM > 0.");
            }

            if (!(facingEpsilonRadians > 0f))
            {
                throw new InvalidOperationException("Formation Capability formation outline requires EmissionFacingEpsilonRadians > 0.");
            }

            return Shape == other.Shape &&
                Within(CenterX, other.CenterX, positionEpsilonM) &&
                Within(CenterY, other.CenterY, positionEpsilonM) &&
                Within(CenterZ, other.CenterZ, positionEpsilonM) &&
                MathF.Abs(NormalizeAngleRadians(FacingRad - other.FacingRad)) < facingEpsilonRadians &&
                WidthCm.Equals(other.WidthCm) &&
                DepthCm.Equals(other.DepthCm) &&
                RadiusCm.Equals(other.RadiusCm) &&
                HeightOffsetM.Equals(other.HeightOffsetM) &&
                CurveSampleCount == other.CurveSampleCount &&
                EdgeLineWidthCm.Equals(other.EdgeLineWidthCm) &&
                CircleRingWidthCm.Equals(other.CircleRingWidthCm) &&
                FrontIndicatorLengthCm.Equals(other.FrontIndicatorLengthCm) &&
                FrontIndicatorLineWidthCm.Equals(other.FrontIndicatorLineWidthCm) &&
                FillColor.Equals(other.FillColor) &&
                BorderColor.Equals(other.BorderColor);
        }

        private static bool Within(float left, float right, float epsilon)
        {
            return MathF.Abs(left - right) < epsilon;
        }

        private static float NormalizeAngleRadians(float angle)
        {
            while (angle > MathF.PI)
            {
                angle -= MathF.Tau;
            }

            while (angle < -MathF.PI)
            {
                angle += MathF.Tau;
            }

            return angle;
        }
    }
}

internal enum FormationCapabilityShowcaseFormationOutlineSegment : byte
{
    CircleRing = 1,
    RectangleFront = 2,
    RectangleBack = 3,
    RectangleLeft = 4,
    RectangleRight = 5,
    FrontIndicator = 6,
    ReservedMax = FrontIndicator,
}
