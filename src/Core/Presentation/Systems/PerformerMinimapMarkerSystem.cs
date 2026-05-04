using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PerformerMinimapMarkerSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription MarkerQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerWorldPosition>()
            .WithNone<PerformerBootstrapPending>();

        private readonly PerformerDefinitionRegistry _definitions;
        private readonly MinimapMarkerBuffer _markers;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;

        public PerformerMinimapMarkerSystem(
            World world,
            PerformerDefinitionRegistry definitions,
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

            var job = new CollectMarkersChunkJob
            {
                Definitions = _definitions,
                Markers = _markers,
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
                _timingDiagnostics.ObservePerformerMinimapMarker(
                    (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency,
                    _markers.Count,
                    _markers.DroppedSinceClear);
            }
        }

        private struct CollectMarkersChunkJob : IChunkJob
        {
            public PerformerDefinitionRegistry Definitions;
            public MinimapMarkerBuffer Markers;

            public void Execute(ref Chunk chunk)
            {
                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                Span<PerformerWorldPosition> positions = chunk.GetSpan<PerformerWorldPosition>();
                bool hasFloatParams = chunk.Has<PerformerFloatParams>();
                bool hasFloatDefaults = chunk.Has<PerformerFloatDefaults>();
                bool hasVectorParams = chunk.Has<PerformerVectorParams>();
                bool hasVectorDefaults = chunk.Has<PerformerVectorDefaults>();
                bool hasIntParams = chunk.Has<PerformerIntParams>();
                bool hasIntDefaults = chunk.Has<PerformerIntDefaults>();
                Span<PerformerFloatParams> floatParams = hasFloatParams ? chunk.GetSpan<PerformerFloatParams>() : default;
                Span<PerformerFloatDefaults> floatDefaults = hasFloatDefaults ? chunk.GetSpan<PerformerFloatDefaults>() : default;
                Span<PerformerVectorParams> vectorParams = hasVectorParams ? chunk.GetSpan<PerformerVectorParams>() : default;
                Span<PerformerVectorDefaults> vectorDefaults = hasVectorDefaults ? chunk.GetSpan<PerformerVectorDefaults>() : default;
                Span<PerformerIntParams> intParams = hasIntParams ? chunk.GetSpan<PerformerIntParams>() : default;
                Span<PerformerIntDefaults> intDefaults = hasIntDefaults ? chunk.GetSpan<PerformerIntDefaults>() : default;

                for (int i = 0; i < chunk.Count; i++)
                {
                    ref readonly PerformerState state = ref states[i];
                    if (!Definitions.TryGet(state.DefId, out PerformerDefinition? definition) ||
                        !definition.HasMinimapMarkerBehavior)
                    {
                        continue;
                    }

                    int[] markerIndices = definition.MinimapMarkerBehaviorIndices;
                    for (int markerIndex = 0; markerIndex < markerIndices.Length; markerIndex++)
                    {
                        ref readonly BehaviorSlot slot = ref definition.Behaviors[markerIndices[markerIndex]];
                        if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                        {
                            continue;
                        }

                        MinimapMarkerConfig marker = slot.MinimapMarker;
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
                        Vector3 worldPosition = positions[i].Value;
                        int stableId = PerformerBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, slot.SlotIndex);
                        Markers.TryAddThreadSafe(
                            stableId,
                            worldPosition.X * 100f,
                            worldPosition.Z * 100f,
                            in color,
                            sizePx);
                    }
                }
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
            in Span<PerformerIntParams> intParams,
            in Span<PerformerIntDefaults> intDefaults,
            in Span<PerformerFloatParams> floatParams,
            in Span<PerformerFloatDefaults> floatDefaults,
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

            return true;
        }

        private static Vector4 ResolveColor(
            in MinimapMarkerConfig marker,
            bool hasVectorParams,
            bool hasVectorDefaults,
            in Span<PerformerVectorParams> vectorParams,
            in Span<PerformerVectorDefaults> vectorDefaults,
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
            in Span<PerformerFloatParams> floatParams,
            in Span<PerformerFloatDefaults> floatDefaults,
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
    }
}
