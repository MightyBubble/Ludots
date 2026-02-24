using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 将物理层的 Position2D 同步到逻辑层的 WorldPositionCm。
    /// 
    /// 执行时序：在物理解算完成后运行。
    /// 
    /// 数据流（定点数统一架构）：
    ///   Physics Integration (Position2D, Fix64Vec2 厘米)
    ///     ↓
    ///   Physics2DToWorldPositionSyncSystem: 直接赋值（无舍入）
    ///     ↓
    ///   WorldPositionCm (逻辑层 SSOT, Fix64Vec2 厘米)
    /// 
    /// 注意：Position2D 和 WorldPositionCm 现在都使用 Fix64Vec2，
    /// 消除了浮点到整数的舍入误差，实现完全确定性。
    /// </summary>
    public sealed class Physics2DToWorldPositionSyncSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<Position2D, WorldPositionCm>();

        public Physics2DToWorldPositionSyncSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            var job = new SyncJob();
            World.InlineQuery<SyncJob, Position2D, WorldPositionCm>(in _query, ref job);
        }

        private struct SyncJob : IForEach<Position2D, WorldPositionCm>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref Position2D physics, ref WorldPositionCm world)
            {
                // 定点数直接赋值，无舍入损失
                world.Value = physics.Value;
            }
        }
    }
}
