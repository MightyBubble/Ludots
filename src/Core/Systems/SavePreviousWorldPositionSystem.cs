using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Components;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// 在每个 FixedUpdate 开始时保存当前位置到 PreviousWorldPositionCm。
    /// 
    /// 执行时序：必须在所有修改 WorldPositionCm 的系统之前运行。
    /// 用途：为渲染帧插值提供上一帧位置。
    /// 
    /// 数据流：
    ///   FixedUpdate 开始
    ///     ↓
    ///   SavePreviousWorldPositionSystem: Previous = Current
    ///     ↓
    ///   物理/导航/其他系统: 更新 Current
    ///     ↓
    ///   渲染帧: Lerp(Previous, Current, alpha) → VisualTransform
    /// </summary>
    public sealed class SavePreviousWorldPositionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<WorldPositionCm, PreviousWorldPositionCm>();

        public SavePreviousWorldPositionSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            var job = new SaveJob();
            World.InlineQuery<SaveJob, WorldPositionCm, PreviousWorldPositionCm>(in _query, ref job);
        }

        private struct SaveJob : IForEach<WorldPositionCm, PreviousWorldPositionCm>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref WorldPositionCm current, ref PreviousWorldPositionCm previous)
            {
                previous.Value = current.Value;
            }
        }
    }
}
