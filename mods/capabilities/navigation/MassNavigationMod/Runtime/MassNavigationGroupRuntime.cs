using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;

namespace MassNavigationMod.Runtime;

public sealed class MassNavigationGroupRuntime
{
    private readonly MassNavigationFormationRuntime _formationLayout;
    private readonly List<NavGroupState?> _groups = new();
    private readonly HashSet<int> _visitedGroupIds = new();
    private readonly Dictionary<int, int> _orderTokenToGroupId = new();
    private readonly List<int> _orderTokensToRemove = new();
    private int[] _groupIdsByControllableIndex = Array.Empty<int>();
    private int[] _selectionMemberScratch = Array.Empty<int>();
    private float[] _looseBaseOffsetX = Array.Empty<float>();
    private float[] _looseBaseOffsetY = Array.Empty<float>();
    private float[] _looseOffsetX = Array.Empty<float>();
    private float[] _looseOffsetY = Array.Empty<float>();

    public MassNavigationGroupRuntime(MassNavigationFormationRuntime formationLayout)
    {
        _formationLayout = formationLayout ?? throw new ArgumentNullException(nameof(formationLayout));
    }

    public int ActiveGroupCount { get; private set; }
    public int ActiveOrderGroupCount => _orderTokenToGroupId.Count;
    public float SelectedRotationRadians { get; private set; }
    public int PendingTargetRefreshCount { get; private set; }
    public int AppliedTargetRefreshCountFrame { get; private set; }
    public int TargetRefreshBudget { get; private set; } = 192;

    public void Configure(MassNavigationGroupSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        TargetRefreshBudget = Math.Max(1, semantics.TargetRefreshBudgetPerUpdate);
    }

    public void Reset()
    {
        _groups.Clear();
        _orderTokenToGroupId.Clear();
        if (_groupIdsByControllableIndex.Length > 0)
        {
            Array.Fill(_groupIdsByControllableIndex, -1);
        }

        ActiveGroupCount = 0;
        SelectedRotationRadians = 0f;
        PendingTargetRefreshCount = 0;
        AppliedTargetRefreshCountFrame = 0;
    }

    public bool HasGroup(int unitIndex)
    {
        return GetGroupId(unitIndex) >= 0;
    }

    public int IssueSelectionMoveCommand(
        MassFlowSimulationState simulation,
        MassNavigationAgentState agentState,
        ReadOnlySpan<Entity> selected,
        Vector2 destinationWorldCm,
        MassNavigationFormationMode formationMode)
    {
        int count = selected.Length;
        if (count <= 0)
        {
            return 0;
        }

        EnsureMembershipCapacity(agentState.ControllableCount);
        float previousRotation = ResolvePreviousRotation(agentState, selected);

        int assignedCount = 0;
        Span<int> memberIndices = EnsureSelectionMemberScratch(count);
        for (int i = 0; i < count; i++)
        {
            if (!agentState.TryGetControllableIndex(selected[i], out int unitIndex))
            {
                continue;
            }

            RemoveFromExistingGroup(simulation, unitIndex);
            memberIndices[assignedCount++] = unitIndex;
            simulation.ReleaseUnitToTeamTarget(unitIndex);
        }

        if (assignedCount <= 0)
        {
            return 0;
        }

        if (formationMode == MassNavigationFormationMode.None || assignedCount == 1)
        {
            AssignLooseTargets(simulation, memberIndices, assignedCount, destinationWorldCm, previousRotation);
            ActiveGroupCount = CountActiveGroups();
            RefreshSelectedRotation(agentState, selected);
            return assignedCount;
        }

        int teamId = simulation.GetTeam(memberIndices[0]);
        Vector2 destinationLocalCm = simulation.WorldToLocalCm(destinationWorldCm);
        Vector2 resolvedDestination = simulation.ResolveNavigableTarget(
            destinationLocalCm.X,
            destinationLocalCm.Y,
            0f,
            0f,
            simulation.Semantics.TargetProjection.GroupCenterClearanceCm);

        int groupId = AllocateGroupId();
        var group = CreateGroup(memberIndices[..assignedCount], teamId, formationMode, previousRotation);
        Vector2 resolvedWorldDestination = simulation.LocalToWorldCm(resolvedDestination);
        group.DestinationWorldX = resolvedWorldDestination.X;
        group.DestinationWorldY = resolvedWorldDestination.Y;
        _groups[groupId] = group;
        AssignGroupTargets(simulation, groupId, group, resetRecovery: true);

        ActiveGroupCount = CountActiveGroups();
        RefreshSelectedRotation(agentState, selected);
        return assignedCount;
    }

    public int UpsertOrderMoveCommand(
        MassFlowSimulationState simulation,
        int orderToken,
        ReadOnlySpan<int> memberIndices,
        int teamId,
        Vector2 destinationWorldCm,
        MassNavigationFormationMode formationMode,
        float rotationRadians)
    {
        if (orderToken <= 0 || memberIndices.Length <= 0)
        {
            return 0;
        }

        Vector2 destinationLocalCm = simulation.WorldToLocalCm(destinationWorldCm);
        Vector2 resolvedDestination = simulation.ResolveNavigableTarget(
            destinationLocalCm.X,
            destinationLocalCm.Y,
            0f,
            0f,
            simulation.Semantics.TargetProjection.GroupCenterClearanceCm);
        Vector2 resolvedWorldDestination = simulation.LocalToWorldCm(resolvedDestination);

        if (!_orderTokenToGroupId.TryGetValue(orderToken, out int groupId) ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            DetachMembersFromOtherGroups(simulation, memberIndices, keepGroupId: -1);
            EnsureMembershipCapacityForMembers(memberIndices);
            groupId = AllocateGroupId();
            var created = CreateGroup(memberIndices, teamId, formationMode, rotationRadians);
            created.CommandToken = orderToken;
            created.DestinationWorldX = resolvedWorldDestination.X;
            created.DestinationWorldY = resolvedWorldDestination.Y;
            _groups[groupId] = created;
            _orderTokenToGroupId[orderToken] = groupId;
            AssignGroupTargets(simulation, groupId, created, resetRecovery: true);
            ActiveGroupCount = CountActiveGroups();
            return memberIndices.Length;
        }

        NavGroupState group = _groups[groupId]!;
        int memberSignature = ComputeMemberSignature(memberIndices);
        bool sameMembers = group.MemberCount == memberIndices.Length && group.MemberSignature == memberSignature;
        bool sameOrderShape = sameMembers &&
            group.FormationMode == formationMode &&
            group.TeamId == teamId &&
            MathF.Abs(group.RotationRadians - rotationRadians) <= 0.0001f &&
            MathF.Abs(group.DestinationWorldX - resolvedWorldDestination.X) <= 0.5f &&
            MathF.Abs(group.DestinationWorldY - resolvedWorldDestination.Y) <= 0.5f;
        if (sameOrderShape)
        {
            return group.MemberCount;
        }

        bool rebuildLayout = group.FormationMode != formationMode || !sameMembers || !HaveSameMembers(group, memberIndices);
        if (rebuildLayout)
        {
            DetachMembersFromOtherGroups(simulation, memberIndices, groupId);
            EnsureMembershipCapacityForMembers(memberIndices);
            ReplaceGroupMembers(simulation, groupId, group, memberIndices, teamId, formationMode, rotationRadians);
        }

        group.TeamId = teamId;
        group.CommandToken = orderToken;
        group.DestinationWorldX = resolvedWorldDestination.X;
        group.DestinationWorldY = resolvedWorldDestination.Y;
        _groups[groupId] = group;
        AssignGroupTargets(simulation, groupId, group, resetRecovery: rebuildLayout);
        ActiveGroupCount = CountActiveGroups();
        return group.MemberCount;
    }

    public bool TryGetOrderGroup(int orderToken, out bool arrived)
    {
        arrived = false;
        if (!_orderTokenToGroupId.TryGetValue(orderToken, out int groupId) ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            return false;
        }

        arrived = _groups[groupId]!.Arrived;
        return true;
    }

    public void CompleteOrderGroup(MassFlowSimulationState simulation, int orderToken)
    {
        if (!_orderTokenToGroupId.TryGetValue(orderToken, out int groupId) ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            return;
        }

        NavGroupState group = _groups[groupId]!;
        for (int i = 0; i < group.MemberCount; i++)
        {
            simulation.HoldUnitAtCurrentPosition(group.MemberIndices[i]);
        }

        DissolveGroup(groupId, group);
        ActiveGroupCount = CountActiveGroups();
    }

    public void PruneInactiveOrderGroups(MassFlowSimulationState simulation, HashSet<int> activeTokens)
    {
        _orderTokensToRemove.Clear();
        foreach ((int token, int groupId) in _orderTokenToGroupId)
        {
            if (activeTokens.Contains(token) ||
                (uint)groupId >= (uint)_groups.Count ||
                _groups[groupId] == null)
            {
                continue;
            }

            _orderTokensToRemove.Add(token);
        }

        for (int i = 0; i < _orderTokensToRemove.Count; i++)
        {
            int token = _orderTokensToRemove[i];
            if (!_orderTokenToGroupId.TryGetValue(token, out int groupId) ||
                (uint)groupId >= (uint)_groups.Count ||
                _groups[groupId] == null)
            {
                continue;
            }

            NavGroupState group = _groups[groupId]!;
            for (int memberIndex = 0; memberIndex < group.MemberCount; memberIndex++)
            {
                simulation.HoldUnitAtCurrentPosition(group.MemberIndices[memberIndex]);
            }

            DissolveGroup(groupId, group);
        }

        ActiveGroupCount = CountActiveGroups();
    }

    public void RotateSelected(MassNavigationAgentState agentState, ReadOnlySpan<Entity> selected, float deltaRadians)
    {
        if (!(MathF.Abs(deltaRadians) > 1e-5f))
        {
            return;
        }

        EnsureMembershipCapacity(agentState.ControllableCount);
        _visitedGroupIds.Clear();
        for (int i = 0; i < selected.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(selected[i], out int unitIndex))
            {
                continue;
            }

            int groupId = GetGroupId(unitIndex);
            if (groupId < 0 || !_visitedGroupIds.Add(groupId))
            {
                continue;
            }

            NavGroupState? group = _groups[groupId];
            if (group == null)
            {
                continue;
            }

            group.RotationRadians += deltaRadians;
            _formationLayout.RecomputeOffsets(
                group.OffsetX,
                group.OffsetY,
                group.BaseOffsetX,
                group.BaseOffsetY,
                group.MemberCount,
                group.RotationRadians);
            QueueGroupTargetRefresh(group);
        }

        RefreshSelectedRotation(agentState, selected);
    }

    public void RefreshSelectedRotation(MassNavigationAgentState agentState, ReadOnlySpan<Entity> selected)
    {
        for (int i = 0; i < selected.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(selected[i], out int unitIndex))
            {
                continue;
            }

            int groupId = GetGroupId(unitIndex);
            if (groupId < 0)
            {
                continue;
            }

            NavGroupState? group = _groups[groupId];
            if (group != null)
            {
                SelectedRotationRadians = group.RotationRadians;
                return;
            }
        }

        SelectedRotationRadians = 0f;
    }

    public void UpdateTargets(
        MassFlowSimulationState simulation,
        MassNavigationAgentState agentState,
        ReadOnlySpan<Entity> selected,
        int frameIndex)
    {
        AppliedTargetRefreshCountFrame = 0;
        if ((frameIndex & 1) == 0)
        {
            RefreshSelectedRotation(agentState, selected);
            return;
        }

        int remainingBudget = TargetRefreshBudget;
        for (int groupId = 0; groupId < _groups.Count; groupId++)
        {
            NavGroupState? group = _groups[groupId];
            if (group == null)
            {
                continue;
            }

            if (!RefreshGroupCenter(simulation, groupId, group))
            {
                continue;
            }

            Vector2 destinationLocalCm = simulation.WorldToLocalCm(new Vector2(group.DestinationWorldX, group.DestinationWorldY));
            float toDestX = destinationLocalCm.X - group.CenterX;
            float toDestY = destinationLocalCm.Y - group.CenterY;
            float distSq = (toDestX * toDestX) + (toDestY * toDestY);
            float distance = MathF.Sqrt(distSq);
            float pullStrength = distance > simulation.Semantics.Group.PullDeadZoneCm
                ? MathF.Min(distance, simulation.Semantics.Group.PullClampCm)
                : 0f;
            float pullX = 0f;
            float pullY = 0f;
            if (pullStrength > 0.001f)
            {
                float invDistance = 1f / distance;
                pullX = toDestX * invDistance * pullStrength;
                pullY = toDestY * invDistance * pullStrength;
            }

            if (group.TargetRefreshPendingCount > 0 && remainingBudget > 0)
            {
                int applied = ApplyGroupTargetRefreshBudget(simulation, group, remainingBudget, pullX, pullY, resetRecovery: false);
                remainingBudget -= applied;
                AppliedTargetRefreshCountFrame += applied;
            }

            group.Arrived = distance < simulation.Semantics.Group.ArrivedRadiusCm;
            if (remainingBudget <= 0)
            {
                break;
            }
        }

        ActiveGroupCount = CountActiveGroups();
        PendingTargetRefreshCount = CountPendingTargetRefreshes();
        RefreshSelectedRotation(agentState, selected);
    }

    private void EnsureMembershipCapacity(int count)
    {
        if (count <= _groupIdsByControllableIndex.Length)
        {
            return;
        }

        int previousLength = _groupIdsByControllableIndex.Length;
        Array.Resize(ref _groupIdsByControllableIndex, count);
        for (int i = previousLength; i < _groupIdsByControllableIndex.Length; i++)
        {
            _groupIdsByControllableIndex[i] = -1;
        }
    }

    private void EnsureMembershipCapacityForMembers(ReadOnlySpan<int> members)
    {
        int maxIndex = -1;
        for (int i = 0; i < members.Length; i++)
        {
            if (members[i] > maxIndex)
            {
                maxIndex = members[i];
            }
        }

        EnsureMembershipCapacity(maxIndex + 1);
    }

    private Span<int> EnsureSelectionMemberScratch(int count)
    {
        if (_selectionMemberScratch.Length < count)
        {
            int next = Math.Max(16, _selectionMemberScratch.Length);
            while (next < count)
            {
                next *= 2;
            }

            Array.Resize(ref _selectionMemberScratch, next);
        }

        return _selectionMemberScratch.AsSpan(0, count);
    }

    private void DetachMembersFromOtherGroups(MassFlowSimulationState simulation, ReadOnlySpan<int> members, int keepGroupId)
    {
        for (int i = 0; i < members.Length; i++)
        {
            int memberIndex = members[i];
            if (GetGroupId(memberIndex) == keepGroupId)
            {
                continue;
            }

            RemoveFromExistingGroup(simulation, memberIndex);
        }
    }

    private float ResolvePreviousRotation(MassNavigationAgentState agentState, ReadOnlySpan<Entity> selected)
    {
        for (int i = 0; i < selected.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(selected[i], out int unitIndex))
            {
                continue;
            }

            int groupId = GetGroupId(unitIndex);
            if (groupId < 0)
            {
                continue;
            }

            NavGroupState? group = _groups[groupId];
            if (group != null)
            {
                return group.RotationRadians;
            }
        }

        return 0f;
    }

    private int AllocateGroupId()
    {
        for (int i = 0; i < _groups.Count; i++)
        {
            if (_groups[i] == null)
            {
                return i;
            }
        }

        _groups.Add(null);
        return _groups.Count - 1;
    }

    private void RemoveFromExistingGroup(MassFlowSimulationState simulation, int unitIndex)
    {
        int groupId = GetGroupId(unitIndex);
        if (groupId < 0)
        {
            return;
        }

        NavGroupState? group = _groups[groupId];
        _groupIdsByControllableIndex[unitIndex] = -1;
        if (group == null)
        {
            return;
        }

        int memberPosition = IndexOfMember(group, unitIndex);
        if (memberPosition < 0)
        {
            return;
        }

        if (group.MemberCount <= 1)
        {
            DissolveGroup(groupId, group);
            return;
        }

        int nextCount = group.MemberCount - 1;
        for (int source = memberPosition + 1; source < group.MemberCount; source++)
        {
            int target = source - 1;
            group.MemberIndices[target] = group.MemberIndices[source];
        }

        group.MemberCount = nextCount;
        if (group.MemberCount <= 1)
        {
            for (int i = 0; i < group.MemberCount; i++)
            {
                simulation.HoldUnitAtCurrentPosition(group.MemberIndices[i]);
            }

            DissolveGroup(groupId, group);
            return;
        }

        RebuildGroupLayout(group);
        QueueGroupTargetRefresh(group);
    }

    private void DissolveGroup(int groupId, NavGroupState group)
    {
        if (group.CommandToken > 0)
        {
            _orderTokenToGroupId.Remove(group.CommandToken);
        }

        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            if ((uint)unitIndex < (uint)_groupIdsByControllableIndex.Length)
            {
                _groupIdsByControllableIndex[unitIndex] = -1;
            }
        }

        _groups[groupId] = null;
    }

    private int GetGroupId(int unitIndex)
    {
        if ((uint)unitIndex >= (uint)_groupIdsByControllableIndex.Length)
        {
            return -1;
        }

        return _groupIdsByControllableIndex[unitIndex];
    }

    private void AssignLooseTargets(
        MassFlowSimulationState simulation,
        ReadOnlySpan<int> memberIndices,
        int count,
        Vector2 destinationWorldCm,
        float rotationRadians)
    {
        if (count <= 0)
        {
            return;
        }

        if (count == 1)
        {
            Vector2 singleLocalTarget = simulation.WorldToLocalCm(destinationWorldCm);
            Vector2 singleTarget = simulation.ResolveNavigableTarget(
                singleLocalTarget.X,
                singleLocalTarget.Y,
                0f,
                0f,
                simulation.Semantics.TargetProjection.LooseTargetClearanceCm);
            simulation.SetUnitTarget(memberIndices[0], singleTarget.X, singleTarget.Y, resetRecovery: true);
            return;
        }

        EnsureLooseOffsetCapacity(count);
        _formationLayout.BuildOffsets(_looseBaseOffsetX, _looseBaseOffsetY, _looseOffsetX, _looseOffsetY, count, MassNavigationFormationMode.Square, rotationRadians);
        Vector2 destinationLocalCm = simulation.WorldToLocalCm(destinationWorldCm);
        Vector2 resolvedCenter = simulation.ResolveNavigableTarget(
            destinationLocalCm.X,
            destinationLocalCm.Y,
            0f,
            0f,
            simulation.Semantics.TargetProjection.GroupCenterClearanceCm);
        for (int i = 0; i < count; i++)
        {
            Vector2 resolvedTarget = simulation.ResolveNavigableTarget(
                resolvedCenter.X + _looseOffsetX[i],
                resolvedCenter.Y + _looseOffsetY[i],
                _looseOffsetX[i],
                _looseOffsetY[i],
                simulation.Semantics.TargetProjection.LooseTargetClearanceCm);
            simulation.SetUnitTarget(memberIndices[i], resolvedTarget.X, resolvedTarget.Y, resetRecovery: true);
        }
    }

    private void EnsureLooseOffsetCapacity(int required)
    {
        if (_looseOffsetX.Length >= required)
        {
            return;
        }

        int next = Math.Max(1, _looseOffsetX.Length);
        while (next < required)
        {
            next *= 2;
        }

        Array.Resize(ref _looseBaseOffsetX, next);
        Array.Resize(ref _looseBaseOffsetY, next);
        Array.Resize(ref _looseOffsetX, next);
        Array.Resize(ref _looseOffsetY, next);
    }

    private int CountActiveGroups()
    {
        int count = 0;
        for (int i = 0; i < _groups.Count; i++)
        {
            if (_groups[i] != null)
            {
                count++;
            }
        }

        return count;
    }

    private int CountPendingTargetRefreshes()
    {
        int count = 0;
        for (int i = 0; i < _groups.Count; i++)
        {
            NavGroupState? group = _groups[i];
            if (group != null)
            {
                count += group.TargetRefreshPendingCount;
            }
        }

        return count;
    }

    private NavGroupState CreateGroup(ReadOnlySpan<int> members, int teamId, MassNavigationFormationMode formationMode, float rotationRadians)
    {
        var group = new NavGroupState(Math.Max(1, members.Length), teamId);
        CopyMembersAndRebuildLayout(group, members, formationMode, rotationRadians);
        return group;
    }

    private void ReplaceGroupMembers(
        MassFlowSimulationState simulation,
        int groupId,
        NavGroupState group,
        ReadOnlySpan<int> nextMembers,
        int teamId,
        MassNavigationFormationMode formationMode,
        float rotationRadians)
    {
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            if ((uint)unitIndex < (uint)_groupIdsByControllableIndex.Length &&
                _groupIdsByControllableIndex[unitIndex] == groupId)
            {
                _groupIdsByControllableIndex[unitIndex] = -1;
                simulation.ReleaseUnitToTeamTarget(unitIndex);
            }
        }

        group.TeamId = teamId;
        CopyMembersAndRebuildLayout(group, nextMembers, formationMode, rotationRadians);
        QueueGroupTargetRefresh(group);
    }

    private void AssignGroupTargets(MassFlowSimulationState simulation, int groupId, NavGroupState group, bool resetRecovery)
    {
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            _groupIdsByControllableIndex[unitIndex] = groupId;
        }

        AppliedTargetRefreshCountFrame = 0;
        if (!RefreshGroupCenter(simulation, groupId, group))
        {
            return;
        }

        group.TargetRefreshCursor = 0;
        group.TargetRefreshPendingCount = group.MemberCount;
        int applied = ApplyGroupTargetRefreshBudget(
            simulation,
            group,
            TargetRefreshBudget,
            pullX: 0f,
            pullY: 0f,
            resetRecovery);
        AppliedTargetRefreshCountFrame += applied;
        PendingTargetRefreshCount = CountPendingTargetRefreshes();
    }

    private bool RefreshGroupCenter(MassFlowSimulationState simulation, int groupId, NavGroupState group)
    {
        int liveCount = 0;
        float centerX = 0f;
        float centerY = 0f;
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            if ((uint)unitIndex >= (uint)simulation.UnitCount)
            {
                continue;
            }

            centerX += simulation.GetPositionX(unitIndex);
            centerY += simulation.GetPositionY(unitIndex);
            liveCount++;
        }

        if (liveCount <= 0)
        {
            DissolveGroup(groupId, group);
            return false;
        }

        float invLive = 1f / liveCount;
        group.CenterX = centerX * invLive;
        group.CenterY = centerY * invLive;
        return true;
    }

    private int ApplyGroupTargetRefreshBudget(
        MassFlowSimulationState simulation,
        NavGroupState group,
        int budget,
        float pullX,
        float pullY,
        bool resetRecovery)
    {
        if (budget <= 0 || group.TargetRefreshPendingCount <= 0 || group.MemberCount <= 0)
        {
            return 0;
        }

        int applied = 0;
        int cursor = Math.Clamp(group.TargetRefreshCursor, 0, Math.Max(0, group.MemberCount - 1));
        int remainingMembers = group.MemberCount;
        while (budget > 0 && group.TargetRefreshPendingCount > 0 && remainingMembers > 0)
        {
            if (cursor >= group.MemberCount)
            {
                cursor = 0;
            }

            int unitIndex = group.MemberIndices[cursor];
            if ((uint)unitIndex < (uint)simulation.UnitCount)
            {
                float rawTargetX = group.CenterX + group.OffsetX[cursor] + pullX;
                float rawTargetY = group.CenterY + group.OffsetY[cursor] + pullY;
                Vector2 resolvedTarget = simulation.ResolveNavigableTarget(
                    rawTargetX,
                    rawTargetY,
                    group.OffsetX[cursor],
                    group.OffsetY[cursor],
                    simulation.Semantics.TargetProjection.GroupSlotClearanceCm);
                simulation.SetUnitTarget(unitIndex, resolvedTarget.X, resolvedTarget.Y, resetRecovery);
                group.TargetRefreshPendingCount--;
                applied++;
                budget--;
            }
            else
            {
                group.TargetRefreshPendingCount--;
            }

            cursor++;
            remainingMembers--;
        }

        group.TargetRefreshCursor = cursor >= group.MemberCount ? 0 : cursor;
        return applied;
    }

    private void CopyMembersAndRebuildLayout(
        NavGroupState group,
        ReadOnlySpan<int> members,
        MassNavigationFormationMode formationMode,
        float rotationRadians)
    {
        group.EnsureCapacity(Math.Max(1, members.Length));
        members.CopyTo(group.MemberIndices);
        group.MemberCount = members.Length;
        group.MemberSignature = ComputeMemberSignature(members);
        group.FormationMode = formationMode;
        group.RotationRadians = rotationRadians;
        RebuildGroupLayout(group);
        QueueGroupTargetRefresh(group);
    }

    private static void QueueGroupTargetRefresh(NavGroupState group)
    {
        group.TargetRefreshCursor = 0;
        group.TargetRefreshPendingCount = group.MemberCount;
    }

    private void RebuildGroupLayout(NavGroupState group)
    {
        if (group.MemberCount <= 0)
        {
            return;
        }

        _formationLayout.BuildOffsets(
            group.BaseOffsetX,
            group.BaseOffsetY,
            group.OffsetX,
            group.OffsetY,
            group.MemberCount,
            group.FormationMode,
            group.RotationRadians);
    }

    private static int IndexOfMember(NavGroupState group, int unitIndex)
    {
        for (int i = 0; i < group.MemberCount; i++)
        {
            if (group.MemberIndices[i] == unitIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool HaveSameMembers(NavGroupState left, ReadOnlySpan<int> right)
    {
        if (left.MemberCount != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.MemberCount; i++)
        {
            if (left.MemberIndices[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static int ComputeMemberSignature(ReadOnlySpan<int> members)
    {
        var hash = new HashCode();
        hash.Add(members.Length);
        for (int i = 0; i < members.Length; i++)
        {
            hash.Add(members[i]);
        }

        return hash.ToHashCode();
    }

    private sealed class NavGroupState
    {
        public NavGroupState(int initialCapacity, int teamId)
        {
            int capacity = Math.Max(1, initialCapacity);
            MemberIndices = new int[capacity];
            BaseOffsetX = new float[capacity];
            BaseOffsetY = new float[capacity];
            OffsetX = new float[capacity];
            OffsetY = new float[capacity];
            TeamId = teamId;
        }

        public int MemberCount { get; set; }
        public int MemberSignature { get; set; }
        public int TeamId { get; set; }
        public int CommandToken { get; set; }
        public MassNavigationFormationMode FormationMode { get; set; }
        public int[] MemberIndices { get; private set; }
        public float[] BaseOffsetX { get; private set; }
        public float[] BaseOffsetY { get; private set; }
        public float[] OffsetX { get; private set; }
        public float[] OffsetY { get; private set; }
        public float DestinationWorldX { get; set; }
        public float DestinationWorldY { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float RotationRadians { get; set; }
        public bool Arrived { get; set; }
        public int TargetRefreshCursor { get; set; }
        public int TargetRefreshPendingCount { get; set; }

        public void EnsureCapacity(int required)
        {
            if (required <= MemberIndices.Length)
            {
                return;
            }

            int next = MemberIndices.Length;
            while (next < required)
            {
                next *= 2;
            }

            int[] memberIndices = MemberIndices;
            float[] baseOffsetX = BaseOffsetX;
            float[] baseOffsetY = BaseOffsetY;
            float[] offsetX = OffsetX;
            float[] offsetY = OffsetY;
            Array.Resize(ref memberIndices, next);
            Array.Resize(ref baseOffsetX, next);
            Array.Resize(ref baseOffsetY, next);
            Array.Resize(ref offsetX, next);
            Array.Resize(ref offsetY, next);
            MemberIndices = memberIndices;
            BaseOffsetX = baseOffsetX;
            BaseOffsetY = baseOffsetY;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }
    }
}

