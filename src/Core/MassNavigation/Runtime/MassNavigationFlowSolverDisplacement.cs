using System;
using Arch.Core;
using Ludots.Core.Components;

namespace Ludots.Core.MassNavigation.Runtime;

/// <summary>
/// displaced 态：PoseAuthority != Nav 的 agent 由外部写权持有者
/// （当前增量为 GAS 位移窗口）驱动 WorldPositionCm。求解器跳过其积分与硬解析，
/// 但保留其在分离哈希中的存在，使邻居持续避让；每个 entity-sync 节拍把已提交的
/// WorldPositionCm 回灌进求解器内部 SoA（不平移单位目标，交还后继续原目标）。
/// 标记使用预分配数组与紧凑索引表，Step 热路径零 ECS 访问、零分配。
/// </summary>
public sealed partial class MassNavigationFlowSolverState
{
    private byte[] _displacedAgentFlags = Array.Empty<byte>();
    private int[] _displacedAgents = Array.Empty<int>();
    private int _displacedAgentCount;
    private int _displacedAgentCapacity;

    public int DisplacedAgentCount => _displacedAgentCount;
    public int DisplacedAgentCapacity => _displacedAgentCapacity;

    internal void PreallocateDisplacedAgentCapacity(int displacedAgentCapacity)
    {
        if (displacedAgentCapacity <= 0)
        {
            throw new InvalidOperationException(
                "MassNavigationFlowSolverState displaced-agent preallocation requires scenarioRuntime.runtimeCapacity.displacedAgentCapacity > 0.");
        }

        if (_displacedAgents.Length != displacedAgentCapacity)
        {
            _displacedAgents = new int[displacedAgentCapacity];
        }

        _displacedAgentCapacity = displacedAgentCapacity;
        _displacedAgentCount = 0;
    }

    public bool IsAgentDisplaced(int index)
    {
        return (uint)index < (uint)UnitCount && _displacedAgentFlags[index] != 0;
    }

    /// <summary>
    /// 进入 displaced 态（写权已在固定步边界切为 Displacement 后调用）。
    /// 超出 displacedAgentCapacity 或重复标记直接抛异常。
    /// </summary>
    public void MarkAgentDisplaced(int index)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigationFlow displaced agent index {index} exceeds current unit count {UnitCount}.");
        }

        if (_displacedAgentCapacity <= 0)
        {
            throw new InvalidOperationException(
                "MassNavigationFlow displaced agents require scenarioRuntime.runtimeCapacity.displacedAgentCapacity to be configured.");
        }

        if (_displacedAgentFlags[index] != 0)
        {
            throw new InvalidOperationException(
                $"MassNavigationFlow agent {index} is already displaced.");
        }

        if (_displacedAgentCount >= _displacedAgentCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigationFlow concurrent displaced agents exceed scenarioRuntime.runtimeCapacity.displacedAgentCapacity {_displacedAgentCapacity}.");
        }

        _displacedAgentFlags[index] = 1;
        _displacedAgents[_displacedAgentCount++] = index;
        int offset = index << 1;
        _velocitiesCm[offset] = 0f;
        _velocitiesCm[offset + 1] = 0f;
    }

    /// <summary>
    /// 窗口取消路径的幂等最小清理：只摘除 displaced 标记与紧凑表项。
    /// 不唤醒、不标脏——取消场景里实体要么已死（标脏会让实体同步撞上死实体），
    /// 要么整个求解器即将被结构重建重置。未标记（已被重建清除）返回 false，
    /// 这是并发生命周期事件的合法结果；正常交还请走 <see cref="ClearAgentDisplaced"/>。
    /// </summary>
    public bool ClearAgentDisplacedIfMarked(int index)
    {
        if ((uint)index >= (uint)UnitCount || _displacedAgentFlags[index] == 0)
        {
            return false;
        }

        _displacedAgentFlags[index] = 0;
        for (int i = 0; i < _displacedAgentCount; i++)
        {
            if (_displacedAgents[i] == index)
            {
                _displacedAgents[i] = _displacedAgents[--_displacedAgentCount];
                break;
            }
        }

        return true;
    }

    /// <summary>
    /// 交还写权：退出 displaced 态并执行带 resetRecovery 的到达恢复
    /// （复用 ResetUnitArrivalState 唤醒路径），使 agent 继续原目标。
    /// </summary>
    public void ClearAgentDisplaced(int index)
    {
        if ((uint)index >= (uint)UnitCount || _displacedAgentFlags[index] == 0)
        {
            throw new InvalidOperationException(
                $"MassNavigationFlow agent {index} is not displaced and cannot be cleared.");
        }

        _displacedAgentFlags[index] = 0;
        for (int i = 0; i < _displacedAgentCount; i++)
        {
            if (_displacedAgents[i] == index)
            {
                _displacedAgents[i] = _displacedAgents[--_displacedAgentCount];
                break;
            }
        }

        ResetUnitArrivalState(index, clearRetryCount: true);
        MarkEntityDirty(index);
    }

    /// <summary>
    /// 把外部写权持有者已提交的世界位姿回灌进求解器（displaced agent 专用）。
    /// 与 <see cref="ApplyExternalDisplacement"/> 共享位移增量施加核心，但不平移单位目标：
    /// 交还后 agent 必须继续原目标。
    /// </summary>
    public void ApplyCommittedWorldPose(int index, float worldXCm, float worldYCm)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigationFlow committed-pose agent index {index} exceeds current unit count {UnitCount}.");
        }

        if (_displacedAgentFlags[index] == 0)
        {
            throw new InvalidOperationException(
                $"MassNavigationFlow agent {index} is not displaced; only the nav solver may write its pose.");
        }

        int offset = index << 1;
        float deltaXCm = (worldXCm - _worldOriginXCm) - _positionsCm[offset];
        float deltaYCm = (worldYCm - _worldOriginYCm) - _positionsCm[offset + 1];
        if (deltaXCm == 0f && deltaYCm == 0f)
        {
            return;
        }

        ApplyDisplacementDelta(index, deltaXCm, deltaYCm, shiftUnitTarget: false);
    }

    /// <summary>
    /// 每个 entity-sync 节拍调用：读取所有 displaced agent 已提交的 WorldPositionCm，
    /// 与求解器内部位置求差回灌，保证邻居持续避让移动中的 displaced agent。
    /// </summary>
    internal void SyncDisplacedAgentPoses(World world, MassNavigationAgentState agentState)
    {
        ArgumentNullException.ThrowIfNull(agentState);
        for (int i = 0; i < _displacedAgentCount; i++)
        {
            int index = _displacedAgents[i];
            if (!agentState.TryGetAgentEntity(index, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigationFlow cannot sync displaced agent {index} because no tracked agent entity is registered.");
            }

            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigationFlow cannot sync displaced agent {index} because tracked entity {entity.Id} is not alive.");
            }

            ref WorldPositionCm worldPosition = ref world.Get<WorldPositionCm>(entity);
            ApplyCommittedWorldPose(index, worldPosition.Value.X.ToFloat(), worldPosition.Value.Y.ToFloat());
        }
    }

    /// <summary>位移增量施加核心（SSOT）：位置/读位置/进度锚点同步平移，单位目标按语义可选。</summary>
    private void ApplyDisplacementDelta(int index, float deltaXCm, float deltaYCm, bool shiftUnitTarget)
    {
        int offset = index << 1;
        _positionsCm[offset] += deltaXCm;
        _positionsCm[offset + 1] += deltaYCm;
        _readPositionsCm[offset] += deltaXCm;
        _readPositionsCm[offset + 1] += deltaYCm;
        _unitProgressAnchorCm[offset] += deltaXCm;
        _unitProgressAnchorCm[offset + 1] += deltaYCm;
        _unitSettledAnchorCm[offset] += deltaXCm;
        _unitSettledAnchorCm[offset + 1] += deltaYCm;
        if (shiftUnitTarget && _hasUnitTarget[index] != 0)
        {
            _unitTargetsCm[offset] += deltaXCm;
            _unitTargetsCm[offset + 1] += deltaYCm;
        }

        ClampLocalToWorldBounds(ref _positionsCm[offset], ref _positionsCm[offset + 1], _bodyRadiiCm[index]);
        ClampLocalToWorldBounds(ref _readPositionsCm[offset], ref _readPositionsCm[offset + 1], _bodyRadiiCm[index]);
        MarkEntityDirty(index);
    }

    private void ResetDisplacedAgents()
    {
        if (_displacedAgentFlags.Length > 0)
        {
            Array.Clear(_displacedAgentFlags, 0, _displacedAgentFlags.Length);
        }

        _displacedAgentCount = 0;
    }
}
