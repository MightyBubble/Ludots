using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PresenterMinimapMarkerSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription MarkerQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterWorldPlanePosition, PresenterWorldFacing, PerfHasMinimapMarker>()
            .WithNone<PresenterBootstrapPending>();

        private readonly PresenterDefinitionRegistry _definitions;
        private readonly MinimapMarkerBuffer _markers;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private int _cachedDefinitionVersion = -1;
        private MinimapMarkerDefinitionPlan[] _markerPlansByDefinition =
            Array.Empty<MinimapMarkerDefinitionPlan>();

        public PresenterMinimapMarkerSystem(
            World world,
            PresenterDefinitionRegistry definitions,
            MinimapMarkerBuffer markers,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _markers = markers ?? throw new ArgumentNullException(nameof(markers));
            _timingDiagnostics = timingDiagnostics;
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            _markers.BeginFrame();
            EnsureDefinitionWorkCache();

            var job = new CollectMarkersChunkJob
            {
                Markers = _markers,
                MarkerPlansByDefinition = _markerPlansByDefinition,
            };

            if (World.SharedJobScheduler == null)
            {
                foreach (ref var chunk in World.Query(in MarkerQuery))
                {
                    job.Execute(ref chunk);
                }
            }
            else
            {
                World.InlineParallelChunkQuery(in MarkerQuery, in job);
            }

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePresenterMinimapMarker(
                    (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency,
                    _markers.Count,
                    _markers.DroppedSinceClear);
            }
        }

        private void EnsureDefinitionWorkCache()
        {
            if (_cachedDefinitionVersion == _definitions.Version)
            {
                return;
            }

            IReadOnlyList<int> registeredIds = _definitions.RegisteredIds;
            int maxId = 0;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                maxId = Math.Max(maxId, registeredIds[i]);
            }

            if (_markerPlansByDefinition.Length <= maxId)
            {
                _markerPlansByDefinition = new MinimapMarkerDefinitionPlan[maxId + 1];
            }
            else
            {
                Array.Clear(_markerPlansByDefinition);
            }

            for (int i = 0; i < registeredIds.Count; i++)
            {
                int definitionId = registeredIds[i];
                if (_definitions.TryGet(definitionId, out PresenterDefinition definition) &&
                    definition.HasMinimapMarkerBehavior &&
                    definition.MinimapMarkerWorkItems.Length != 0)
                {
                    _markerPlansByDefinition[definitionId] = MinimapMarkerDefinitionPlan.Create(definition.MinimapMarkerWorkItems);
                }
            }

            _cachedDefinitionVersion = _definitions.Version;
        }

        private struct CollectMarkersChunkJob : IChunkJob
        {
            public MinimapMarkerBuffer Markers;
            public MinimapMarkerDefinitionPlan[] MarkerPlansByDefinition;

            public void Execute(ref Chunk chunk)
            {
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                Span<PresenterWorldPlanePosition> planePositions = chunk.GetSpan<PresenterWorldPlanePosition>();
                Span<PresenterWorldFacing> facings = chunk.GetSpan<PresenterWorldFacing>();
                bool hasFloatParams = chunk.Has<PresenterFloatParams>();
                bool hasFloatDefaults = chunk.Has<PresenterFloatDefaults>();
                bool hasVectorParams = chunk.Has<PresenterVectorParams>();
                bool hasVectorDefaults = chunk.Has<PresenterVectorDefaults>();
                bool hasIntParams = chunk.Has<PresenterIntParams>();
                bool hasIntDefaults = chunk.Has<PresenterIntDefaults>();
                Span<PresenterFloatParams> floatParams = hasFloatParams ? chunk.GetSpan<PresenterFloatParams>() : default;
                Span<PresenterFloatDefaults> floatDefaults = hasFloatDefaults ? chunk.GetSpan<PresenterFloatDefaults>() : default;
                Span<PresenterVectorParams> vectorParams = hasVectorParams ? chunk.GetSpan<PresenterVectorParams>() : default;
                Span<PresenterVectorDefaults> vectorDefaults = hasVectorDefaults ? chunk.GetSpan<PresenterVectorDefaults>() : default;
                Span<PresenterIntParams> intParams = hasIntParams ? chunk.GetSpan<PresenterIntParams>() : default;
                Span<PresenterIntDefaults> intDefaults = hasIntDefaults ? chunk.GetSpan<PresenterIntDefaults>() : default;

                int markerCount = CountChunkMarkers(
                    states,
                    hasFloatParams,
                    hasFloatDefaults,
                    hasIntParams,
                    hasIntDefaults,
                    in floatParams,
                    in floatDefaults,
                    in intParams,
                    in intDefaults,
                    chunk.Count,
                    out bool useSingleMarkerFastPath);
                if (markerCount <= 0)
                {
                    return;
                }

                int writeIndex = Markers.ReserveThreadSafe(markerCount, out int acceptedCount);
                if (acceptedCount <= 0)
                {
                    return;
                }

                if (useSingleMarkerFastPath &&
                    TryEmitSingleMarkerFastPath(
                        states,
                        planePositions,
                        facings,
                        chunk.Count,
                        writeIndex,
                        acceptedCount))
                {
                    return;
                }

                int emitted = 0;
                int cachedDefId = -1;
                MinimapMarkerDefinitionPlan cachedPlan = default;
                for (int i = 0; i < chunk.Count; i++)
                {
                    ref readonly PresenterState state = ref states[i];
                    if (!TryGetMarkerPlan(
                            state.DefId,
                            ref cachedDefId,
                            ref cachedPlan,
                            out MinimapMarkerDefinitionPlan plan))
                    {
                        continue;
                    }

                    PresenterDefinition.MinimapMarkerWorkItem[] markerWorkItems = plan.WorkItems;
                    for (int markerIndex = 0; markerIndex < markerWorkItems.Length; markerIndex++)
                    {
                        ref readonly PresenterDefinition.MinimapMarkerWorkItem work = ref markerWorkItems[markerIndex];
                        if (!IsBehaviorActive(state.BehaviorActiveMask, work.SlotIndex))
                        {
                            continue;
                        }

                        MinimapMarkerConfig marker = work.Marker;
                        if (!IsMarkerVisible(
                                marker.VisibilityParamKey,
                                hasIntParams,
                                hasIntDefaults,
                                hasFloatParams,
                                hasFloatDefaults,
                                in intParams,
                                in intDefaults,
                                in floatParams,
                                in floatDefaults,
                                i))
                        {
                            continue;
                        }

                        Vector4 color = ResolveColor(marker, hasVectorParams, hasVectorDefaults, in vectorParams, in vectorDefaults, i);
                        float sizePx = ResolveSize(marker, hasFloatParams, hasFloatDefaults, in floatParams, in floatDefaults, i);
                        uint flags = 0u;
                        float orientationRad = 0f;
                        float orientationLengthPx = 0f;
                        if (TryResolveOrientation(
                                marker,
                                hasFloatParams,
                                hasFloatDefaults,
                                in floatParams,
                                in floatDefaults,
                                in facings,
                                i,
                                out orientationRad,
                                out orientationLengthPx))
                        {
                            flags |= MinimapMarkerFlags.HasOrientation;
                        }

                        Vector2 worldPlanePosition = planePositions[i].ValueCm;
                        int stableId = PresenterBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, work.SlotIndex);
                        Markers.WriteReserved(
                            writeIndex + emitted,
                            stableId,
                            state.OwnerEntity,
                            worldPlanePosition.X,
                            worldPlanePosition.Y,
                            in color,
                            sizePx,
                            flags,
                            orientationRad,
                            orientationLengthPx);
                        emitted++;
                        if (emitted >= acceptedCount)
                        {
                            return;
                        }
                    }
                }
            }

            private int CountChunkMarkers(
                Span<PresenterState> states,
                bool hasFloatParams,
                bool hasFloatDefaults,
                bool hasIntParams,
                bool hasIntDefaults,
                in Span<PresenterFloatParams> floatParams,
                in Span<PresenterFloatDefaults> floatDefaults,
                in Span<PresenterIntParams> intParams,
                in Span<PresenterIntDefaults> intDefaults,
                int count,
                out bool useSingleMarkerFastPath)
            {
                if (TryCountSingleMarkerFastPath(states, count, out int fastCount))
                {
                    useSingleMarkerFastPath = true;
                    return fastCount;
                }

                useSingleMarkerFastPath = false;
                int markerCount = 0;
                int cachedDefId = -1;
                MinimapMarkerDefinitionPlan cachedPlan = default;
                for (int i = 0; i < count; i++)
                {
                    ref readonly PresenterState state = ref states[i];
                    if (!TryGetMarkerPlan(
                            state.DefId,
                            ref cachedDefId,
                            ref cachedPlan,
                            out MinimapMarkerDefinitionPlan plan))
                    {
                        continue;
                    }

                    PresenterDefinition.MinimapMarkerWorkItem[] markerWorkItems = plan.WorkItems;
                    for (int markerIndex = 0; markerIndex < markerWorkItems.Length; markerIndex++)
                    {
                        ref readonly PresenterDefinition.MinimapMarkerWorkItem work = ref markerWorkItems[markerIndex];
                        if (!IsBehaviorActive(state.BehaviorActiveMask, work.SlotIndex))
                        {
                            continue;
                        }

                        MinimapMarkerConfig marker = work.Marker;
                        if (IsMarkerVisible(
                                marker.VisibilityParamKey,
                                hasIntParams,
                                hasIntDefaults,
                                hasFloatParams,
                                hasFloatDefaults,
                                in intParams,
                                in intDefaults,
                                in floatParams,
                                in floatDefaults,
                                i))
                        {
                            markerCount++;
                        }
                    }
                }

                return markerCount;
            }

            private bool TryCountSingleMarkerFastPath(
                Span<PresenterState> states,
                int count,
                out int markerCount)
            {
                markerCount = 0;
                int cachedDefId = -1;
                MinimapMarkerDefinitionPlan cachedPlan = default;
                for (int i = 0; i < count; i++)
                {
                    ref readonly PresenterState state = ref states[i];
                    if (!TryGetMarkerPlan(
                            state.DefId,
                            ref cachedDefId,
                            ref cachedPlan,
                            out MinimapMarkerDefinitionPlan plan))
                    {
                        continue;
                    }

                    if (!plan.CanUseSingleMarkerFastPath)
                    {
                        return false;
                    }

                    if ((state.BehaviorActiveMask & plan.SingleSlotMask) != 0u)
                    {
                        markerCount++;
                    }
                }

                return true;
            }

            private bool TryEmitSingleMarkerFastPath(
                Span<PresenterState> states,
                Span<PresenterWorldPlanePosition> planePositions,
                Span<PresenterWorldFacing> facings,
                int count,
                int writeIndex,
                int acceptedCount)
            {
                int emitted = 0;
                int cachedDefId = -1;
                MinimapMarkerDefinitionPlan cachedPlan = default;
                for (int i = 0; i < count; i++)
                {
                    ref readonly PresenterState state = ref states[i];
                    if (!TryGetMarkerPlan(
                            state.DefId,
                            ref cachedDefId,
                            ref cachedPlan,
                            out MinimapMarkerDefinitionPlan plan))
                    {
                        continue;
                    }

                    if (!plan.CanUseSingleMarkerFastPath)
                    {
                        return false;
                    }

                    if ((state.BehaviorActiveMask & plan.SingleSlotMask) == 0u)
                    {
                        continue;
                    }

                    uint flags = 0u;
                    float orientationRad = 0f;
                    float orientationLengthPx = 0f;
                    if (plan.SingleOrientationMode == MinimapMarkerOrientationMode.PresenterForward &&
                        TryResolvePresenterFacingOrientation(
                            in facings[i],
                            plan.SingleOrientationOffsetRad,
                            plan.SingleOrientationLengthPx,
                            out orientationRad,
                            out orientationLengthPx))
                    {
                        flags = MinimapMarkerFlags.HasOrientation;
                    }

                    Vector2 worldPlanePosition = planePositions[i].ValueCm;
                    int stableId = PresenterBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, plan.SingleSlotIndex);
                    Markers.WriteReserved(
                        writeIndex + emitted,
                        stableId,
                        state.OwnerEntity,
                        worldPlanePosition.X,
                        worldPlanePosition.Y,
                        in plan.SingleColor,
                        plan.SingleSizePx,
                        flags,
                        orientationRad,
                        orientationLengthPx);
                    emitted++;
                    if (emitted >= acceptedCount)
                    {
                        return true;
                    }
                }

                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool TryGetMarkerPlan(
                int definitionId,
                ref int cachedDefId,
                ref MinimapMarkerDefinitionPlan cachedPlan,
                out MinimapMarkerDefinitionPlan plan)
            {
                if (definitionId == cachedDefId)
                {
                    plan = cachedPlan;
                    return plan.HasWork;
                }

                cachedDefId = definitionId;
                if ((uint)definitionId < (uint)MarkerPlansByDefinition.Length)
                {
                    cachedPlan = MarkerPlansByDefinition[definitionId];
                    plan = cachedPlan;
                    return plan.HasWork;
                }

                cachedPlan = default;
                plan = default;
                return false;
            }
        }

        private readonly struct MinimapMarkerDefinitionPlan
        {
            public readonly PresenterDefinition.MinimapMarkerWorkItem[] WorkItems;
            public readonly bool CanUseSingleMarkerFastPath;
            public readonly int SingleSlotIndex;
            public readonly uint SingleSlotMask;
            public readonly Vector4 SingleColor;
            public readonly float SingleSizePx;
            public readonly MinimapMarkerOrientationMode SingleOrientationMode;
            public readonly float SingleOrientationOffsetRad;
            public readonly float SingleOrientationLengthPx;

            private MinimapMarkerDefinitionPlan(
                PresenterDefinition.MinimapMarkerWorkItem[] workItems,
                bool canUseSingleMarkerFastPath,
                int singleSlotIndex,
                uint singleSlotMask,
                in Vector4 singleColor,
                float singleSizePx,
                MinimapMarkerOrientationMode singleOrientationMode,
                float singleOrientationOffsetRad,
                float singleOrientationLengthPx)
            {
                WorkItems = workItems;
                CanUseSingleMarkerFastPath = canUseSingleMarkerFastPath;
                SingleSlotIndex = singleSlotIndex;
                SingleSlotMask = singleSlotMask;
                SingleColor = singleColor;
                SingleSizePx = singleSizePx;
                SingleOrientationMode = singleOrientationMode;
                SingleOrientationOffsetRad = singleOrientationOffsetRad;
                SingleOrientationLengthPx = singleOrientationLengthPx;
            }

            public bool HasWork => WorkItems != null && WorkItems.Length != 0;

            public static MinimapMarkerDefinitionPlan Create(PresenterDefinition.MinimapMarkerWorkItem[] workItems)
            {
                bool fast = TryBuildSingleMarkerFastPath(
                    workItems,
                    out int slotIndex,
                    out uint slotMask,
                    out Vector4 color,
                    out float sizePx,
                    out MinimapMarkerOrientationMode orientationMode,
                    out float orientationOffsetRad,
                    out float orientationLengthPx);
                return new MinimapMarkerDefinitionPlan(
                    workItems,
                    fast,
                    slotIndex,
                    slotMask,
                    in color,
                    sizePx,
                    orientationMode,
                    orientationOffsetRad,
                    orientationLengthPx);
            }

            private static bool TryBuildSingleMarkerFastPath(
                PresenterDefinition.MinimapMarkerWorkItem[] workItems,
                out int slotIndex,
                out uint slotMask,
                out Vector4 color,
                out float sizePx,
                out MinimapMarkerOrientationMode orientationMode,
                out float orientationOffsetRad,
                out float orientationLengthPx)
            {
                slotIndex = -1;
                slotMask = 0u;
                color = default;
                sizePx = 0f;
                orientationMode = MinimapMarkerOrientationMode.None;
                orientationOffsetRad = 0f;
                orientationLengthPx = 0f;
                if (workItems == null || workItems.Length != 1)
                {
                    return false;
                }

                ref readonly PresenterDefinition.MinimapMarkerWorkItem work = ref workItems[0];
                if (work.SlotIndex is < 0 or >= 32)
                {
                    return false;
                }

                MinimapMarkerConfig marker = work.Marker;
                if (marker.ColorParamKey >= 0 ||
                    marker.SizeParamKey >= 0 ||
                    marker.VisibilityParamKey >= 0 ||
                    !float.IsFinite(marker.SizePx) ||
                    marker.SizePx <= 0f)
                {
                    return false;
                }

                if (marker.OrientationMode != MinimapMarkerOrientationMode.None &&
                    marker.OrientationMode != MinimapMarkerOrientationMode.PresenterForward)
                {
                    return false;
                }

                if (marker.OrientationMode == MinimapMarkerOrientationMode.PresenterForward &&
                    (!float.IsFinite(marker.OrientationOffsetRad) ||
                     !float.IsFinite(marker.OrientationLengthPx) ||
                     marker.OrientationLengthPx <= 0f))
                {
                    return false;
                }

                slotIndex = work.SlotIndex;
                slotMask = 1u << slotIndex;
                color = marker.Color;
                sizePx = marker.SizePx;
                orientationMode = marker.OrientationMode;
                orientationOffsetRad = marker.OrientationOffsetRad;
                orientationLengthPx = marker.OrientationLengthPx;
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private static bool IsMarkerVisible(
            int visibilityParamKey,
            bool hasIntParams,
            bool hasIntDefaults,
            bool hasFloatParams,
            bool hasFloatDefaults,
            in Span<PresenterIntParams> intParams,
            in Span<PresenterIntDefaults> intDefaults,
            in Span<PresenterFloatParams> floatParams,
            in Span<PresenterFloatDefaults> floatDefaults,
            int index)
        {
            if (visibilityParamKey < 0)
            {
                return true;
            }

            if (hasIntParams && intParams[index].TryGet(visibilityParamKey, out int intValue))
            {
                return intValue != 0;
            }

            if (hasIntDefaults && intDefaults[index].TryGet(visibilityParamKey, out int intDefaultValue))
            {
                return intDefaultValue != 0;
            }

            if (hasFloatParams && floatParams[index].TryGet(visibilityParamKey, out float floatValue))
            {
                return floatValue > 0.5f;
            }

            if (hasFloatDefaults && floatDefaults[index].TryGet(visibilityParamKey, out float floatDefaultValue))
            {
                return floatDefaultValue > 0.5f;
            }

            return false;
        }

        private static Vector4 ResolveColor(
            in MinimapMarkerConfig marker,
            bool hasVectorParams,
            bool hasVectorDefaults,
            in Span<PresenterVectorParams> vectorParams,
            in Span<PresenterVectorDefaults> vectorDefaults,
            int index)
        {
            if (marker.ColorParamKey >= 0 &&
                hasVectorParams &&
                vectorParams[index].TryGet(marker.ColorParamKey, out Vector4 color))
            {
                return color;
            }

            if (marker.ColorParamKey >= 0 &&
                hasVectorDefaults &&
                vectorDefaults[index].TryGet(marker.ColorParamKey, out Vector4 defaultColor))
            {
                return defaultColor;
            }

            return marker.Color;
        }

        private static float ResolveSize(
            in MinimapMarkerConfig marker,
            bool hasFloatParams,
            bool hasFloatDefaults,
            in Span<PresenterFloatParams> floatParams,
            in Span<PresenterFloatDefaults> floatDefaults,
            int index)
        {
            if (marker.SizeParamKey >= 0 &&
                hasFloatParams &&
                floatParams[index].TryGet(marker.SizeParamKey, out float size) &&
                float.IsFinite(size) &&
                size > 0f)
            {
                return size;
            }

            if (marker.SizeParamKey >= 0 &&
                hasFloatDefaults &&
                floatDefaults[index].TryGet(marker.SizeParamKey, out float defaultSize) &&
                float.IsFinite(defaultSize) &&
                defaultSize > 0f)
            {
                return defaultSize;
            }

            return marker.SizePx;
        }

        private static bool TryResolveOrientation(
            in MinimapMarkerConfig marker,
            bool hasFloatParams,
            bool hasFloatDefaults,
            in Span<PresenterFloatParams> floatParams,
            in Span<PresenterFloatDefaults> floatDefaults,
            in Span<PresenterWorldFacing> facings,
            int index,
            out float orientationRad,
            out float orientationLengthPx)
        {
            orientationRad = 0f;
            orientationLengthPx = 0f;
            if (marker.OrientationMode == MinimapMarkerOrientationMode.None)
            {
                return false;
            }

            if (marker.OrientationMode == MinimapMarkerOrientationMode.PresenterForward)
            {
                return TryResolvePresenterFacingOrientation(
                    in facings[index],
                    marker.OrientationOffsetRad,
                    marker.OrientationLengthPx,
                    out orientationRad,
                    out orientationLengthPx);
            }

            float value;
            if (hasFloatParams && floatParams[index].TryGet(marker.OrientationParamKey, out float paramValue))
            {
                value = paramValue;
            }
            else if (hasFloatDefaults && floatDefaults[index].TryGet(marker.OrientationParamKey, out float defaultValue))
            {
                value = defaultValue;
            }
            else
            {
                return false;
            }

            if (!float.IsFinite(value))
            {
                return false;
            }

            orientationRad = marker.OrientationMode == MinimapMarkerOrientationMode.ParamDegrees
                ? WorldPlane2D.DegToRadValue(value)
                : value;
            orientationRad += marker.OrientationOffsetRad;
            orientationLengthPx = marker.OrientationLengthPx;
            return float.IsFinite(orientationRad) &&
                float.IsFinite(orientationLengthPx) &&
                orientationLengthPx > 0f;
        }

        private static bool TryResolvePresenterFacingOrientation(
            in PresenterWorldFacing facing,
            float orientationOffsetRad,
            float configuredLengthPx,
            out float orientationRad,
            out float orientationLengthPx)
        {
            orientationRad = 0f;
            orientationLengthPx = 0f;
            if (facing.HasValue == 0 ||
                !float.IsFinite(facing.AngleRad) ||
                !float.IsFinite(configuredLengthPx) ||
                configuredLengthPx <= 0f)
            {
                return false;
            }

            orientationRad = facing.AngleRad + orientationOffsetRad;
            orientationLengthPx = configuredLengthPx;
            return float.IsFinite(orientationRad);
        }
    }
}
