using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Runs after WorldToVisualSyncSystem and writes the visual ground height into VisualTransform.Position.Y.
    /// The generic visual-ground projector service is preferred when available; VertexMap remains the generic fallback.
    /// </summary>
    public sealed class TerrainHeightSyncSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly IReadOnlyDictionary<string, object> _globals;
        private float[] _worldXCmScratch = Array.Empty<float>();
        private float[] _worldYCmScratch = Array.Empty<float>();
        private float[] _heightCmScratch = Array.Empty<float>();

        private static readonly QueryDescription _stateQuery = new QueryDescription()
            .WithAll<PresentationFrameState>();

        private static readonly QueryDescription _interpolatedQuery = new QueryDescription()
            .WithAll<WorldPositionCm, PreviousWorldPositionCm, VisualTransform>();

        private static readonly QueryDescription _vertexMapFallbackQuery = new QueryDescription()
            .WithAll<WorldPositionCm, VisualTransform>();

        /// <summary>
        /// Scale applied only for the legacy VertexMap fallback.
        /// The projector path returns centimeters and is converted directly to meters.
        /// </summary>
        public float HeightScale { get; set; } = 2.0f;

        public TerrainHeightSyncSystem(World world, IReadOnlyDictionary<string, object> globals)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (_TryProjectVisualGroundHeights())
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.VertexMap.Name, out var vtxObj) || vtxObj is not VertexMap vertexMap)
            {
                return;
            }

            var job = new SyncJob { VertexMap = vertexMap, HeightScale = HeightScale };
            _world.InlineQuery<SyncJob, WorldPositionCm, VisualTransform>(in _vertexMapFallbackQuery, ref job);
        }

        public void AfterUpdate(in float dt) { }
        public void BeforeUpdate(in float dt) { }
        public void Dispose() { }

        private bool _TryProjectVisualGroundHeights()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.VisualGroundProjector.Name, out var projectorObj) ||
                projectorObj is not IVisualGroundProjector projector)
            {
                return false;
            }

            int totalCount = _CountEntities(in _interpolatedQuery);
            if (totalCount <= 0)
            {
                return false;
            }

            _EnsureScratchCapacity(totalCount);
            Fix64 alpha = Fix64.FromFloat(_ResolveInterpolationAlpha());
            int gatheredCount = _GatherInterpolatedWorldPositions(alpha);
            if (gatheredCount <= 0)
            {
                return false;
            }

            if (!projector.TryProjectHeights(
                    _worldXCmScratch.AsSpan(0, gatheredCount),
                    _worldYCmScratch.AsSpan(0, gatheredCount),
                    _heightCmScratch.AsSpan(0, gatheredCount)))
            {
                return false;
            }

            _ApplyProjectedHeights(gatheredCount);
            return true;
        }

        private int _CountEntities(in QueryDescription queryDescription)
        {
            int totalCount = 0;
            var query = _world.Query(in queryDescription);
            foreach (var chunk in query)
            {
                totalCount += chunk.Count;
            }

            return totalCount;
        }

        private float _ResolveInterpolationAlpha()
        {
            var query = _world.Query(in _stateQuery);
            foreach (var chunk in query)
            {
                if (chunk.Count <= 0)
                {
                    continue;
                }

                var states = chunk.GetArray<PresentationFrameState>();
                PresentationFrameState state = states[0];
                return state.Enabled ? state.InterpolationAlpha : 1f;
            }

            return 1f;
        }

        private void _EnsureScratchCapacity(int requiredCount)
        {
            if (_worldXCmScratch.Length >= requiredCount)
            {
                return;
            }

            int newCapacity = Math.Max(requiredCount, Math.Max(64, _worldXCmScratch.Length * 2));
            Array.Resize(ref _worldXCmScratch, newCapacity);
            Array.Resize(ref _worldYCmScratch, newCapacity);
            Array.Resize(ref _heightCmScratch, newCapacity);
        }

        private int _GatherInterpolatedWorldPositions(Fix64 alpha)
        {
            int offset = 0;
            var query = _world.Query(in _interpolatedQuery);
            foreach (ref var chunk in query)
            {
                var currentPositions = chunk.GetSpan<WorldPositionCm>();
                var previousPositions = chunk.GetSpan<PreviousWorldPositionCm>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Fix64Vec2 interpolated = Fix64Vec2.Lerp(previousPositions[i].Value, currentPositions[i].Value, alpha);
                    _worldXCmScratch[offset] = interpolated.X.ToFloat();
                    _worldYCmScratch[offset] = interpolated.Y.ToFloat();
                    offset++;
                }
            }

            return offset;
        }

        private void _ApplyProjectedHeights(int gatheredCount)
        {
            int offset = 0;
            var query = _world.Query(in _interpolatedQuery);
            foreach (ref var chunk in query)
            {
                var visuals = chunk.GetSpan<VisualTransform>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (offset >= gatheredCount)
                    {
                        return;
                    }

                    var position = visuals[i].Position;
                    position.Y = _heightCmScratch[offset] * 0.01f;
                    visuals[i].Position = position;
                    offset++;
                }
            }
        }

        private struct SyncJob : IForEach<WorldPositionCm, VisualTransform>
        {
            public VertexMap VertexMap;
            public float HeightScale;

            public void Update(ref WorldPositionCm _, ref VisualTransform visual)
            {
                var pos = visual.Position;
                float rawHeight;
                try
                {
                    rawHeight = VertexMap.GetLogicHeight(pos);
                }
                catch
                {
                    return;
                }

                if (float.IsNaN(rawHeight) || float.IsInfinity(rawHeight))
                {
                    return;
                }

                pos.Y = rawHeight * HeightScale;
                visual.Position = pos;
            }
        }
    }
}
