using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Mathematics;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// 在 WorldToVisualSyncSystem 之后运行，为 VisualTransform 的 Y 分量采样地形高度。
    /// 
    /// 逻辑层 WorldPositionCm 仅包含 XY 平面（厘米），无高度信息。
    /// 表现层需将实体贴附到地形表面，避免悬浮或穿地。
    /// 
    /// 仅处理带 WorldPositionCm 的实体，避免影响输入/相机等锚点实体。
    /// </summary>
    public sealed class TerrainHeightSyncSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly IReadOnlyDictionary<string, object> _globals;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<WorldPositionCm, VisualTransform, VisualHeightmapSampleState>()
            .WithNone<PresentationStaticTransform>();
        private static readonly QueryDescription _staticPendingQuery = new QueryDescription()
            .WithAll<WorldPositionCm, VisualTransform, PresentationStaticTransform, PresentationStaticHeightPending>();
        private static readonly QueryDescription _frameStateQuery = new QueryDescription()
            .WithAll<PresentationFrameState>();
        private float[] _projectedXs = Array.Empty<float>();
        private float[] _projectedYs = Array.Empty<float>();
        private float[] _projectedHeights = Array.Empty<float>();
        private Entity[] _staticPendingEntities = Array.Empty<Entity>();
        private readonly CommandBuffer _commandBuffer = new();
        private bool _warnedMissingHeightmap;
        private int _sampledThisFrame;

        /// <summary>地形高度缩放（米/高度单位），需与地形渲染器一致，默认 2.0。</summary>
        public float HeightScale { get; set; } = 2.0f;

        public TerrainHeightSyncSystem(
            World world,
            IReadOnlyDictionary<string, object> globals,
            PresentationTimingDiagnostics? timingDiagnostics = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _timingDiagnostics = timingDiagnostics;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            _sampledThisFrame = 0;
            IVisualHeightmap? heightmap =
                _globals.TryGetValue(CoreServiceKeys.VisualHeightmap.Name, out var heightmapObj)
                    ? heightmapObj as IVisualHeightmap
                    : null;
            if (heightmap is null)
            {
                WarnMissingHeightmap();
                SyncStaticPendingToZeroHeight();
                ObserveTiming(start);
                return;
            }

            ReadFrameState(out float alpha, out int frameId);
            TrySyncFromHeightmap(heightmap, alpha, frameId, in _query);
            TrySyncStaticPendingFromHeightmap(heightmap, alpha, frameId);
            ObserveTiming(start);
        }

        public void AfterUpdate(in float dt) { }
        public void BeforeUpdate(in float dt) { }
        public void Dispose()
        {
            _commandBuffer.Dispose();
        }

        private void ReadFrameState(out float alpha, out int frameId)
        {
            var job = new ReadFrameStateJob();
            _world.InlineQuery<ReadFrameStateJob, PresentationFrameState>(in _frameStateQuery, ref job);
            alpha = job.Alpha;
            frameId = job.FrameId;
        }

        private struct ReadFrameStateJob : IForEach<PresentationFrameState>
        {
            public float Alpha;
            public int FrameId;

            public ReadFrameStateJob()
            {
                Alpha = 1f;
                FrameId = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref PresentationFrameState state)
            {
                Alpha = state.Enabled ? state.InterpolationAlpha : 1f;
                FrameId = state.FrameId;
            }
        }

        private bool TrySyncFromHeightmap(IVisualHeightmap heightmap, float alpha, int frameId, in QueryDescription query)
        {
            bool any = false;
            Fix64 fixedAlpha = Fix64.FromFloat(alpha);
            foreach (ref var chunk in _world.Query(in query))
            {
                int count = chunk.Count;
                EnsureProjectionCapacity(count);

                Span<WorldPositionCm> currentPositions = chunk.GetSpan<WorldPositionCm>();
                Span<VisualTransform> visuals = chunk.GetSpan<VisualTransform>();
                Span<PreviousWorldPositionCm> previousPositions = chunk.Has<PreviousWorldPositionCm>()
                    ? chunk.GetSpan<PreviousWorldPositionCm>()
                    : Span<PreviousWorldPositionCm>.Empty;
                Span<VisualHeightmapSampleState> heightSampleStates = chunk.Has<VisualHeightmapSampleState>()
                    ? chunk.GetSpan<VisualHeightmapSampleState>()
                    : Span<VisualHeightmapSampleState>.Empty;

                if (previousPositions.Length == 0)
                {
                    for (int index = 0; index < count; index++)
                    {
                        Vector2 worldCm = currentPositions[index].Value.ToVector2();
                        _projectedXs[index] = worldCm.X;
                        _projectedYs[index] = worldCm.Y;
                    }
                }
                else
                {
                    for (int index = 0; index < count; index++)
                    {
                        Vector2 worldCm = Fix64Vec2.Lerp(previousPositions[index].Value, currentPositions[index].Value, fixedAlpha).ToVector2();
                        _projectedXs[index] = worldCm.X;
                        _projectedYs[index] = worldCm.Y;
                    }
                }

                if (!heightmap.SampleHeightsCm(
                        _projectedXs.AsSpan(0, count),
                        _projectedYs.AsSpan(0, count),
                        _projectedHeights.AsSpan(0, count)))
                {
                    continue;
                }

                any = true;
                _sampledThisFrame += count;
                if (heightSampleStates.IsEmpty)
                {
                    for (int index = 0; index < count; index++)
                    {
                        float heightCm = _projectedHeights[index];
                        if (float.IsNaN(heightCm) || float.IsInfinity(heightCm))
                        {
                            continue;
                        }

                        visuals[index].Position.Y = WorldUnits.CmToM(heightCm);
                    }
                }
                else
                {
                    for (int index = 0; index < count; index++)
                    {
                        float heightCm = _projectedHeights[index];
                        if (!float.IsNaN(heightCm) && !float.IsInfinity(heightCm))
                        {
                            visuals[index].Position.Y = WorldUnits.CmToM(heightCm);
                        }

                        heightSampleStates[index] = new VisualHeightmapSampleState
                        {
                            FrameId = frameId,
                            Sampled = 1,
                        };
                    }
                }
            }

            return any;
        }

        private void TrySyncStaticPendingFromHeightmap(IVisualHeightmap heightmap, float alpha, int frameId)
        {
            int count = 0;
            if (TrySyncFromHeightmap(heightmap, alpha, frameId, in _staticPendingQuery, ref count))
            {
                RemoveStaticPendingMarkers(_staticPendingEntities.AsSpan(0, count));
            }
        }

        private void SyncStaticPendingToZeroHeight()
        {
            int count = 0;
            foreach (ref var chunk in _world.Query(in _staticPendingQuery))
            {
                Span<VisualTransform> visuals = chunk.GetSpan<VisualTransform>();
                foreach (int index in chunk)
                {
                    visuals[index].Position.Y = 0f;
                    AddStaticPendingEntity(ref count, chunk.Entity(index));
                }
            }

            if (count > 0)
            {
                RemoveStaticPendingMarkers(_staticPendingEntities.AsSpan(0, count));
            }
        }

        private bool TrySyncFromHeightmap(
            IVisualHeightmap heightmap,
            float alpha,
            int frameId,
            in QueryDescription query,
            ref int staticPendingCount)
        {
            bool any = false;
            Fix64 fixedAlpha = Fix64.FromFloat(alpha);
            foreach (ref var chunk in _world.Query(in query))
            {
                int count = chunk.Count;
                EnsureProjectionCapacity(count);

                Span<WorldPositionCm> currentPositions = chunk.GetSpan<WorldPositionCm>();
                Span<VisualTransform> visuals = chunk.GetSpan<VisualTransform>();
                Span<PreviousWorldPositionCm> previousPositions = chunk.Has<PreviousWorldPositionCm>()
                    ? chunk.GetSpan<PreviousWorldPositionCm>()
                    : Span<PreviousWorldPositionCm>.Empty;
                Span<VisualHeightmapSampleState> heightSampleStates = chunk.Has<VisualHeightmapSampleState>()
                    ? chunk.GetSpan<VisualHeightmapSampleState>()
                    : Span<VisualHeightmapSampleState>.Empty;

                if (previousPositions.Length == 0)
                {
                    for (int index = 0; index < count; index++)
                    {
                        Vector2 worldCm = currentPositions[index].Value.ToVector2();
                        _projectedXs[index] = worldCm.X;
                        _projectedYs[index] = worldCm.Y;
                    }
                }
                else
                {
                    for (int index = 0; index < count; index++)
                    {
                        Vector2 worldCm = Fix64Vec2.Lerp(previousPositions[index].Value, currentPositions[index].Value, fixedAlpha).ToVector2();
                        _projectedXs[index] = worldCm.X;
                        _projectedYs[index] = worldCm.Y;
                    }
                }

                if (!heightmap.SampleHeightsCm(
                        _projectedXs.AsSpan(0, count),
                        _projectedYs.AsSpan(0, count),
                        _projectedHeights.AsSpan(0, count)))
                {
                    continue;
                }

                any = true;
                _sampledThisFrame += count;
                if (heightSampleStates.IsEmpty)
                {
                    for (int index = 0; index < count; index++)
                    {
                        float heightCm = _projectedHeights[index];
                        visuals[index].Position.Y = float.IsNaN(heightCm) || float.IsInfinity(heightCm)
                            ? 0f
                            : WorldUnits.CmToM(heightCm);
                        AddStaticPendingEntity(ref staticPendingCount, chunk.Entity(index));
                    }
                }
                else
                {
                    for (int index = 0; index < count; index++)
                    {
                        float heightCm = _projectedHeights[index];
                        visuals[index].Position.Y = float.IsNaN(heightCm) || float.IsInfinity(heightCm)
                            ? 0f
                            : WorldUnits.CmToM(heightCm);
                        heightSampleStates[index] = new VisualHeightmapSampleState
                        {
                            FrameId = frameId,
                            Sampled = 1,
                        };
                        AddStaticPendingEntity(ref staticPendingCount, chunk.Entity(index));
                    }
                }
            }

            return any;
        }

        private void RemoveStaticPendingMarkers(ReadOnlySpan<Entity> entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (_world.IsAlive(entity) && _world.Has<PresentationStaticHeightPending>(entity))
                {
                    _commandBuffer.Remove<PresentationStaticHeightPending>(in entity);
                }
            }

            if (entities.Length > 0)
            {
                _commandBuffer.Playback(_world);
            }
        }

        private void AddStaticPendingEntity(ref int count, Entity entity)
        {
            EnsureStaticPendingCapacity(count + 1);
            _staticPendingEntities[count++] = entity;
        }

        private void WarnMissingHeightmap()
        {
            if (_warnedMissingHeightmap)
            {
                return;
            }

            Log.Warn(in LogChannels.Presentation, "Terrain height sync requested VisualHeightmap, but none is registered; static projection writes height 0.");
            _warnedMissingHeightmap = true;
        }

        private void ObserveTiming(long startTimestamp)
        {
            if (_timingDiagnostics == null)
            {
                return;
            }

            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            _timingDiagnostics.ObserveTerrainHeightSync(elapsedMs, _sampledThisFrame);
        }

        private void EnsureProjectionCapacity(int required)
        {
            if (required <= _projectedXs.Length)
            {
                return;
            }

            int capacity = Math.Max(required, Math.Max(4, _projectedXs.Length * 2));
            Array.Resize(ref _projectedXs, capacity);
            Array.Resize(ref _projectedYs, capacity);
            Array.Resize(ref _projectedHeights, capacity);
        }

        private void EnsureStaticPendingCapacity(int required)
        {
            if (required <= _staticPendingEntities.Length)
            {
                return;
            }

            int capacity = Math.Max(required, Math.Max(256, _staticPendingEntities.Length * 2));
            Array.Resize(ref _staticPendingEntities, capacity);
        }
    }
}
