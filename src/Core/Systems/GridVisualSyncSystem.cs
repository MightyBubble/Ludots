using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// [已废弃] 从 Grid 坐标同步到 VisualTransform。
    /// 
    /// 推荐使用 WorldToVisualSyncSystem，它从 WorldPositionCm (SSOT) 同步并支持插值。
    /// 
    /// 此系统保留用于：
    /// - 纯网格定位的实体（如棋盘游戏的格子）
    /// - 不需要插值的静态网格实体
    /// 
    /// 新实体应使用 WorldPositionCm + PreviousWorldPositionCm + VisualTransform 组合，
    /// 并由 WorldToVisualSyncSystem 处理同步。
    /// </summary>
    [Obsolete("推荐使用 WorldToVisualSyncSystem。此系统仅用于纯网格定位的实体。")]
    public class GridVisualSyncSystem : BaseSystem<World, float>
    {
        private readonly ICoordinateMapper _mapper;

        private QueryDescription _visibleWithoutCullQuery = new QueryDescription()
            .WithAll<Position, VisualTransform>()
            .WithNone<CullState>();

        private QueryDescription _visibleWithCullQuery = new QueryDescription()
            .WithAll<Position, VisualTransform, CullState>();

        public GridVisualSyncSystem(World world, ICoordinateMapper mapper) : base(world)
        {
            _mapper = mapper;
        }

        public override void Update(in float dt)
        {
            var jobNoCull = new VisualSyncNoCullJob { Mapper = _mapper };
            World.InlineParallelQuery<VisualSyncNoCullJob, Position, VisualTransform>(in _visibleWithoutCullQuery, ref jobNoCull);

            var jobWithCull = new VisualSyncWithCullJob { Mapper = _mapper };
            World.InlineParallelQuery<VisualSyncWithCullJob, Position, VisualTransform, CullState>(in _visibleWithCullQuery, ref jobWithCull);
        }

        struct VisualSyncNoCullJob : IForEach<Position, VisualTransform>
        {
            public ICoordinateMapper Mapper;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref Position pos, ref VisualTransform visual)
            {
                visual.Position = Mapper.LogicToVisual(pos.GridPos, 0);
            }
        }

        struct VisualSyncWithCullJob : IForEach<Position, VisualTransform, CullState>
        {
            public ICoordinateMapper Mapper;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref Position pos, ref VisualTransform visual, ref CullState cull)
            {
                if (!cull.IsVisible) return;
                visual.Position = Mapper.LogicToVisual(pos.GridPos, 0);
            }
        }
    }
}
