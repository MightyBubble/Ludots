using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
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
        private const float CmToM = 0.01f;
        private readonly World _world;
        private readonly IReadOnlyDictionary<string, object> _globals;
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<WorldPositionCm, VisualTransform>();
        private static readonly QueryDescription _frameStateQuery = new QueryDescription()
            .WithAll<PresentationFrameState>();
        private Entity[] _projectedEntities = Array.Empty<Entity>();
        private float[] _projectedXs = Array.Empty<float>();
        private float[] _projectedYs = Array.Empty<float>();
        private float[] _projectedHeights = Array.Empty<float>();

        /// <summary>地形高度缩放（米/高度单位），需与地形渲染器一致，默认 2.0。</summary>
        public float HeightScale { get; set; } = 2.0f;

        public TerrainHeightSyncSystem(World world, IReadOnlyDictionary<string, object> globals)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            IVisualHeightmap? heightmap =
                _globals.TryGetValue(CoreServiceKeys.VisualHeightmap.Name, out var heightmapObj)
                    ? heightmapObj as IVisualHeightmap
                    : null;
            if (heightmap is null)
            {
                return;
            }

            float alpha = ReadInterpolationAlpha();
            TrySyncFromHeightmap(heightmap, alpha);
        }

        public void AfterUpdate(in float dt) { }
        public void BeforeUpdate(in float dt) { }
        public void Dispose() { }

        private float ReadInterpolationAlpha()
        {
            float alpha = 1f;
            _world.Query(in _frameStateQuery, (ref PresentationFrameState state) =>
            {
                alpha = state.Enabled ? state.InterpolationAlpha : 1f;
            });
            return alpha;
        }

        private bool TrySyncFromHeightmap(IVisualHeightmap heightmap, float alpha)
        {
            int count = 0;
            _world.Query(in _query, (Entity entity, ref WorldPositionCm current, ref VisualTransform visual) =>
            {
                EnsureProjectionCapacity(count + 1);
                Vector2 worldCm = ResolveWorldCm(entity, current.Value, alpha);
                _projectedEntities[count] = entity;
                _projectedXs[count] = worldCm.X;
                _projectedYs[count] = worldCm.Y;
                count++;
            });

            if (count <= 0 ||
                !heightmap.SampleHeightsCm(
                    _projectedXs.AsSpan(0, count),
                    _projectedYs.AsSpan(0, count),
                    _projectedHeights.AsSpan(0, count)))
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                Entity entity = _projectedEntities[i];
                if (!_world.IsAlive(entity) || !_world.Has<VisualTransform>(entity))
                {
                    continue;
                }

                float heightCm = _projectedHeights[i];
                if (float.IsNaN(heightCm) || float.IsInfinity(heightCm))
                {
                    continue;
                }

                ref var visual = ref _world.Get<VisualTransform>(entity);
                Vector3 position = visual.Position;
                position.Y = heightCm * CmToM;
                visual.Position = position;
            }

            return true;
        }

        private Vector2 ResolveWorldCm(Entity entity, in Fix64Vec2 current, float alpha)
        {
            if (_world.TryGet(entity, out PreviousWorldPositionCm previous))
            {
                return Fix64Vec2.Lerp(previous.Value, current, Fix64.FromFloat(alpha)).ToVector2();
            }

            return current.ToVector2();
        }

        private void EnsureProjectionCapacity(int required)
        {
            if (required <= _projectedEntities.Length)
            {
                return;
            }

            int capacity = Math.Max(required, Math.Max(4, _projectedEntities.Length * 2));
            Array.Resize(ref _projectedEntities, capacity);
            Array.Resize(ref _projectedXs, capacity);
            Array.Resize(ref _projectedYs, capacity);
            Array.Resize(ref _projectedHeights, capacity);
        }
    }
}
