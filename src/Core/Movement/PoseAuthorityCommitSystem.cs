using Arch.Buffer;
using Arch.Core;
using Arch.System;

namespace Ludots.Core.Movement
{
    /// <summary>
    /// 固定步边界的位姿写权结算系统（issue #643）。
    /// 运行在 <see cref="Engine.GameEngine.SystemGroup.SchemaUpdate"/>：
    /// 先把上一固定步排队的写权切换经 CommandBuffer 生效并通知监听者，
    /// 再推进已持有窗口的时钟并对超时窗口 fail-fast。
    /// </summary>
    public sealed class PoseAuthorityCommitSystem : BaseSystem<World, float>
    {
        private readonly PoseAuthorityArbiter _arbiter;
        private readonly CommandBuffer _commandBuffer = new();

        public PoseAuthorityCommitSystem(World world, PoseAuthorityArbiter arbiter) : base(world)
        {
            _arbiter = arbiter ?? throw new System.ArgumentNullException(nameof(arbiter));
        }

        public override void Update(in float dt)
        {
            _arbiter.CommitPendingTransitions(World, _commandBuffer);
            _arbiter.AdvanceActiveWindows(World, dt);
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}
