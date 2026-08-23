using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Movement;

namespace Ludots.Core.MassNavigation.Systems;

/// <summary>
/// MassNavigation 对位姿写权切换的消费端。
/// 在固定步边界的写权结算后，把 nav agent 的 displaced 态同步进求解器：
/// Nav→Displacement 标记 displaced（求解器跳过其积分/硬解析，邻居持续避让）；
/// Displacement→Nav 先回灌最终已提交位姿，再清除 displaced 并执行带 resetRecovery 的
/// 到达恢复，使 agent 继续原目标。窗口取消（死亡/卸载/重建）做幂等清理，
/// 不依赖实体存活。非 nav-agent 实体不归本桥处理。
/// </summary>
internal sealed class MassNavigationPoseAuthorityBridge : IPoseAuthorityTransitionListener
{
    private readonly System.Func<MassNavigationSimulationRuntime?> _runtimeProvider;

    public MassNavigationPoseAuthorityBridge(System.Func<MassNavigationSimulationRuntime?> runtimeProvider)
    {
        _runtimeProvider = runtimeProvider ?? throw new System.ArgumentNullException(nameof(runtimeProvider));
    }

    public void OnPoseAuthorityCommitted(World world, Entity entity, PoseAuthorityKind from, PoseAuthorityKind to)
    {
        if (!world.Has<MassNavigationAgentIndex>(entity))
        {
            return;
        }

        MassNavigationSimulationRuntime simulation = _runtimeProvider()
            ?? throw new System.InvalidOperationException(
                $"MassNavigation pose-authority bridge received a {from}->{to} transition for agent entity {entity.Id} without an active navigation runtime.");

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

        // Attached 与 Displacement 共用 displaced 求解器状态：外部写权持有者驱动
        // WorldPositionCm（位移窗口每步直写；attachment 由 AttachmentPositionSyncSystem
        // 从父位姿派生），求解器跳过积分、邻居持续避让、每 entity-sync 节拍回灌。
        if (from == PoseAuthorityKind.Nav && to == PoseAuthorityKind.Attached)
        {
            simulation.MassNavigationFlow.MarkAgentDisplaced(agentIndex);
            return;
        }

        if (from == PoseAuthorityKind.Attached && to == PoseAuthorityKind.Nav)
        {
            ref WorldPositionCm attachedPosition = ref world.Get<WorldPositionCm>(entity);
            simulation.MassNavigationFlow.ApplyCommittedWorldPose(
                agentIndex,
                attachedPosition.Value.X.ToFloat(),
                attachedPosition.Value.Y.ToFloat());
            simulation.MassNavigationFlow.ClearAgentDisplaced(agentIndex);
            return;
        }

        throw new System.InvalidOperationException(
            $"MassNavigation pose-authority bridge does not support the {from}->{to} transition for agent entity {entity.Id}.");
    }

    public void OnPoseAuthorityWindowCancelled(World world, Entity entity, PoseAuthorityKind holder)
    {
        // 取消路径不保证实体存活（死亡取消）也不保证运行时仍在（地图卸载后半程）。
        // 清理是幂等的：求解器标记可能已被结构重建清除，运行时可能已释放——都合法。
        MassNavigationSimulationRuntime? simulation = _runtimeProvider();
        if (simulation == null)
        {
            return;
        }

        if (!TryResolveAgentIndex(world, simulation, entity, out int agentIndex))
        {
            return;
        }

        simulation.MassNavigationFlow.ClearAgentDisplacedIfMarked(agentIndex);
    }

    private static bool TryResolveAgentIndex(
        World world,
        MassNavigationSimulationRuntime simulation,
        Entity entity,
        out int agentIndex)
    {
        // 存活实体走组件（权威真相）；死亡实体组件已不可读，退回绑定表按实体身份解析。
        if (world.IsAlive(entity))
        {
            if (world.TryGet(entity, out MassNavigationAgentIndex index))
            {
                agentIndex = index.Value;
                return true;
            }

            agentIndex = -1;
            return false;
        }

        System.Collections.Generic.IReadOnlyList<Entity> agents = simulation.AgentState.AllAgents;
        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i] == entity)
            {
                agentIndex = i;
                return true;
            }
        }

        agentIndex = -1;
        return false;
    }
}
