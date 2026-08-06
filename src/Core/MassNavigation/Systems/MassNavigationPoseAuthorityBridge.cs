using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Movement;

namespace Ludots.Core.MassNavigation.Systems;

/// <summary>
/// MassNavigation 对位姿写权切换（issue #643）的消费端。
/// 在固定步边界的写权结算后，把 nav agent 的 displaced 态同步进求解器：
/// Nav→Displacement 标记 displaced（求解器跳过其积分/硬解析，邻居持续避让）；
/// Displacement→Nav 先回灌最终已提交位姿，再清除 displaced 并执行带 resetRecovery 的
/// 到达恢复，使 agent 继续原目标。非 nav-agent 实体不归本桥处理。
/// </summary>
internal sealed class MassNavigationPoseAuthorityBridge : IPoseAuthorityTransitionListener
{
    private readonly GameEngine _engine;

    public MassNavigationPoseAuthorityBridge(GameEngine engine)
    {
        _engine = engine ?? throw new System.ArgumentNullException(nameof(engine));
    }

    public void OnPoseAuthorityCommitted(World world, Entity entity, PoseAuthorityKind from, PoseAuthorityKind to)
    {
        if (!world.Has<MassNavigationAgentIndex>(entity))
        {
            return;
        }

        if (!MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            throw new System.InvalidOperationException(
                $"MassNavigation pose-authority bridge received a {from}->{to} transition for agent entity {entity.Id} without an active navigation runtime.");
        }

        int agentIndex = world.Get<MassNavigationAgentIndex>(entity).Value;
        if (from == PoseAuthorityKind.Nav && to == PoseAuthorityKind.Displacement)
        {
            simulation.MassNavigationFlow.MarkAgentDisplaced(agentIndex);
            return;
        }

        if (from == PoseAuthorityKind.Displacement && to == PoseAuthorityKind.Nav)
        {
            // The displacement wrote WorldPositionCm after the last entity-sync feed; ingest the
            // final committed pose before returning integration to the solver so it resumes from
            // where the window left the agent.
            ref WorldPositionCm worldPosition = ref world.Get<WorldPositionCm>(entity);
            simulation.MassNavigationFlow.ApplyCommittedWorldPose(
                agentIndex,
                worldPosition.Value.X.ToFloat(),
                worldPosition.Value.Y.ToFloat());
            simulation.MassNavigationFlow.ClearAgentDisplaced(agentIndex);
            return;
        }

        throw new System.InvalidOperationException(
            $"MassNavigation pose-authority bridge does not support the {from}->{to} transition for agent entity {entity.Id}.");
    }
}
