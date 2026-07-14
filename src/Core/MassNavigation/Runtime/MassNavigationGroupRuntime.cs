using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;

namespace Ludots.Core.MassNavigation.Runtime;

internal sealed class MassNavigationGroupRuntime
{
    internal const float OrderTargetRestoreToleranceCm = 0.001f;

    private readonly List<NavGroupState?> _groups;
    private readonly Dictionary<int, int> _orderTokenToGroupId;
    private readonly List<int> _orderTokensToRemove;
    private readonly int[] _groupIdsByAgentIndex;
    private readonly NavGroupState[] _groupPool;
    private readonly int _groupMemberCapacity;

    public MassNavigationGroupRuntime(
        MassNavigationGroupSemantics groupSemantics,
        MassNavigationRuntimeCapacityConfig capacity)
    {
        ArgumentNullException.ThrowIfNull(groupSemantics);
        ArgumentNullException.ThrowIfNull(capacity);

        _groupMemberCapacity = capacity.GroupMemberCapacity;
        _groups = new List<NavGroupState?>(capacity.NavigationGroupCapacity);
        for (int i = 0; i < capacity.NavigationGroupCapacity; i++)
        {
            _groups.Add(null);
        }

        _groupPool = new NavGroupState[capacity.NavigationGroupCapacity];
        for (int i = 0; i < _groupPool.Length; i++)
        {
            _groupPool[i] = new NavGroupState(capacity.GroupMemberCapacity, teamId: 0);
        }

        _orderTokenToGroupId = new Dictionary<int, int>(capacity.NavigationGroupCapacity);
        _orderTokensToRemove = new List<int>(capacity.NavigationGroupCapacity);
        _groupIdsByAgentIndex = new int[capacity.GroupMembershipAgentCapacity];
        Array.Fill(_groupIdsByAgentIndex, -1);
    }

    public int ActiveGroupCount { get; private set; }
    public int ActiveOrderGroupCount => _orderTokenToGroupId.Count;

    public void Reset()
    {
        for (int i = 0; i < _groups.Count; i++)
        {
            _groups[i] = null;
        }

        _orderTokenToGroupId.Clear();
        _orderTokensToRemove.Clear();
        Array.Fill(_groupIdsByAgentIndex, -1);
        ActiveGroupCount = 0;
    }

    internal AuthoredRebuildSnapshot CaptureAuthoredRebuildSnapshot()
    {
        if (ActiveGroupCount <= 0)
        {
            return new AuthoredRebuildSnapshot(Array.Empty<AuthoredRebuildGroupSnapshot>());
        }

        var groups = new AuthoredRebuildGroupSnapshot[ActiveGroupCount];
        int capturedCount = 0;
        for (int groupId = 0; groupId < _groups.Count; groupId++)
        {
            NavGroupState? group = _groups[groupId];
            if (group == null || group.MemberCount <= 0)
            {
                continue;
            }

            var members = new AuthoredRebuildMemberSnapshot[group.MemberCount];
            for (int memberIndex = 0; memberIndex < group.MemberCount; memberIndex++)
            {
                members[memberIndex] = new AuthoredRebuildMemberSnapshot(
                    group.MemberEntities[memberIndex],
                    group.MemberOrderTargetWorldX[memberIndex],
                    group.MemberOrderTargetWorldY[memberIndex]);
            }

            groups[capturedCount++] = new AuthoredRebuildGroupSnapshot(
                groupId,
                group.CommandToken,
                group.RequestedDestinationWorldX,
                group.RequestedDestinationWorldY,
                group.DestinationWorldX,
                group.DestinationWorldY,
                group.CenterX,
                group.CenterY,
                group.Arrived,
                members);
        }

        if (capturedCount != groups.Length)
        {
            Array.Resize(ref groups, capturedCount);
        }

        return new AuthoredRebuildSnapshot(groups);
    }

    internal void RestoreAuthoredRebuildSnapshot(
        World world,
        MassNavigationFlowSolverState simulation,
        MassNavigationAgentState agentState,
        AuthoredRebuildSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(agentState);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Groups.Length <= 0)
        {
            return;
        }

        for (int i = 0; i < snapshot.Groups.Length; i++)
        {
            AuthoredRebuildGroupSnapshot captured = snapshot.Groups[i];
            if ((uint)captured.GroupId >= (uint)_groups.Count)
            {
                throw new InvalidOperationException(
                    $"MassNavigation authored group restore references group id {captured.GroupId}, exceeding configured scenarioRuntime.runtimeCapacity.navigationGroupCapacity {_groups.Count}.");
            }

            int[] memberIndices = new int[captured.Members.Length];
            int[] memberSnapshotIndices = new int[captured.Members.Length];
            int restoredMemberCount = 0;
            for (int memberIndex = 0; memberIndex < captured.Members.Length; memberIndex++)
            {
                if (!TryResolveAuthoredRebuildMemberIndex(
                    world,
                    simulation,
                    captured.Members[memberIndex].Entity,
                    out int remappedAgentIndex))
                {
                    continue;
                }

                EnsureMembershipCapacity(remappedAgentIndex + 1);
                if (_groupIdsByAgentIndex[remappedAgentIndex] >= 0)
                {
                    throw new InvalidOperationException(
                        $"MassNavigation authored group restore resolved agent index {remappedAgentIndex} into more than one active group.");
                }

                memberIndices[restoredMemberCount] = remappedAgentIndex;
                memberSnapshotIndices[restoredMemberCount] = memberIndex;
                restoredMemberCount++;
            }

            if (restoredMemberCount <= 0)
            {
                continue;
            }

            if (restoredMemberCount == 1 && captured.CommandToken <= 0)
            {
                simulation.HoldUnitAtCurrentPosition(memberIndices[0]);
                continue;
            }

            if (captured.CommandToken > 0 && _orderTokenToGroupId.ContainsKey(captured.CommandToken))
            {
                throw new InvalidOperationException(
                    $"MassNavigation authored group restore found duplicate order token {captured.CommandToken}.");
            }

            int currentTeamId = simulation.GetTeam(memberIndices[0]);
            NavGroupState group = _groupPool[captured.GroupId];
            group.Reset(currentTeamId);
            CopyMembersAndRebuildOffsets(
                simulation,
                agentState,
                group,
                memberIndices.AsSpan(0, restoredMemberCount));
            group.CommandToken = captured.CommandToken;
            group.RequestedDestinationWorldX = captured.RequestedDestinationWorldX;
            group.RequestedDestinationWorldY = captured.RequestedDestinationWorldY;
            group.DestinationWorldX = captured.DestinationWorldX;
            group.DestinationWorldY = captured.DestinationWorldY;
            group.CenterX = captured.CenterX;
            group.CenterY = captured.CenterY;
            _groups[captured.GroupId] = group;
            if (captured.CommandToken > 0)
            {
                _orderTokenToGroupId[captured.CommandToken] = captured.GroupId;
            }

            if (group.MemberCount == 1)
            {
                AssignLooseOrderTargets(simulation, captured.GroupId, group, resetRecovery: true);
            }
            else
            {
                AssignGroupTargets(simulation, captured.GroupId, group, resetRecovery: true);
            }

            bool orderTargetsPreserved = HaveSameRestoredOrderTargets(group, captured, memberSnapshotIndices, restoredMemberCount);
            group.Arrived = captured.Arrived &&
                restoredMemberCount == captured.Members.Length &&
                orderTargetsPreserved;
        }

        ActiveGroupCount = CountActiveGroups();
    }

    public bool HasGroup(int unitIndex)
    {
        return GetGroupId(unitIndex) >= 0;
    }

    public bool TryGetGroupMemberOrderTarget(int unitIndex, out float targetWorldX, out float targetWorldY)
    {
        int groupId = GetGroupId(unitIndex);
        if (groupId < 0 ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            targetWorldX = 0f;
            targetWorldY = 0f;
            return false;
        }

        NavGroupState group = _groups[groupId]!;
        int memberOffset = IndexOfMember(group, unitIndex);
        if (memberOffset < 0)
        {
            targetWorldX = 0f;
            targetWorldY = 0f;
            return false;
        }

        targetWorldX = group.MemberOrderTargetWorldX[memberOffset];
        targetWorldY = group.MemberOrderTargetWorldY[memberOffset];
        return true;
    }

    public int UpsertOrderMoveCommand(
        MassNavigationFlowSolverState simulation,
        MassNavigationAgentState agentState,
        int orderToken,
        ReadOnlySpan<int> memberIndices,
        int teamId,
        Vector2 destinationWorldCm)
    {
        return UpsertOrderMoveCommand(
            simulation,
            agentState,
            orderToken,
            memberIndices,
            teamId,
            destinationWorldCm,
            out _);
    }

    public int UpsertOrderMoveCommand(
        MassNavigationFlowSolverState simulation,
        MassNavigationAgentState agentState,
        int orderToken,
        ReadOnlySpan<int> memberIndices,
        int teamId,
        Vector2 destinationWorldCm,
        out bool commandChanged)
    {
        commandChanged = false;
        if (orderToken <= 0 || memberIndices.Length <= 0)
        {
            return 0;
        }

        bool singleMemberOrder = memberIndices.Length == 1;
        if (!_orderTokenToGroupId.TryGetValue(orderToken, out int groupId) ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            EnsureMembershipCapacityForMembers(memberIndices);
            ValidateGroupMembersBound(agentState, memberIndices);
            groupId = AllocateGroupId();
            Vector2 resolvedWorldDestination = singleMemberOrder
                ? ResolveSingleMemberWorldDestination(simulation, memberIndices[0], destinationWorldCm)
                : ResolveGroupWorldDestination(simulation, memberIndices, memberIndices.Length, destinationWorldCm);
            DetachMembersFromOtherGroups(simulation, memberIndices, keepGroupId: -1);
            var created = CreateGroup(groupId, simulation, agentState, memberIndices, teamId);
            created.CommandToken = orderToken;
            created.RequestedDestinationWorldX = destinationWorldCm.X;
            created.RequestedDestinationWorldY = destinationWorldCm.Y;
            created.DestinationWorldX = resolvedWorldDestination.X;
            created.DestinationWorldY = resolvedWorldDestination.Y;
            _groups[groupId] = created;
            _orderTokenToGroupId[orderToken] = groupId;
            if (singleMemberOrder)
            {
                AssignLooseOrderTargets(simulation, groupId, created, resetRecovery: true);
            }
            else
            {
                AssignGroupTargets(simulation, groupId, created, resetRecovery: true);
            }

            ActiveGroupCount = CountActiveGroups();
            commandChanged = true;
            return memberIndices.Length;
        }

        NavGroupState group = _groups[groupId]!;
        bool rebuildOffsets = !HaveSameMembers(group, memberIndices);
        bool retarget =
            group.TeamId != teamId ||
            group.RequestedDestinationWorldX != destinationWorldCm.X ||
            group.RequestedDestinationWorldY != destinationWorldCm.Y ||
            rebuildOffsets;
        if (!retarget)
        {
            return group.MemberCount;
        }

        commandChanged = true;

        if (rebuildOffsets)
        {
            EnsureMembershipCapacityForMembers(memberIndices);
            ValidateGroupMembersBound(agentState, memberIndices);
        }

        Vector2 nextResolvedWorldDestination = singleMemberOrder
            ? ResolveSingleMemberWorldDestination(simulation, memberIndices[0], destinationWorldCm)
            : ResolveGroupWorldDestination(simulation, memberIndices, memberIndices.Length, destinationWorldCm);
        if (rebuildOffsets)
        {
            DetachMembersFromOtherGroups(simulation, memberIndices, groupId);
            ReplaceGroupMembers(simulation, agentState, groupId, group, memberIndices, teamId);
        }

        group.TeamId = teamId;
        group.CommandToken = orderToken;
        group.RequestedDestinationWorldX = destinationWorldCm.X;
        group.RequestedDestinationWorldY = destinationWorldCm.Y;
        group.DestinationWorldX = nextResolvedWorldDestination.X;
        group.DestinationWorldY = nextResolvedWorldDestination.Y;
        _groups[groupId] = group;
        if (singleMemberOrder)
        {
            AssignLooseOrderTargets(simulation, groupId, group, resetRecovery: rebuildOffsets);
        }
        else
        {
            AssignGroupTargets(simulation, groupId, group, resetRecovery: rebuildOffsets);
        }

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

    public void CompleteOrderGroup(MassNavigationFlowSolverState simulation, int orderToken)
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

    public void PruneInactiveOrderGroups(MassNavigationFlowSolverState simulation, HashSet<int> activeTokens)
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

    public void UpdateTargets(
        MassNavigationFlowSolverState simulation,
        int frameIndex)
    {
        for (int groupId = 0; groupId < _groups.Count; groupId++)
        {
            NavGroupState? group = _groups[groupId];
            if (group == null)
            {
                continue;
            }

            if (group.MemberCount == 1)
            {
                UpdateLooseOrderGroupArrival(simulation, group);
                continue;
            }

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
                continue;
            }

            float invLive = 1f / liveCount;
            centerX *= invLive;
            centerY *= invLive;
            Vector2 destinationLocalCm = simulation.WorldToLocalCm(new Vector2(group.DestinationWorldX, group.DestinationWorldY));
            float toDestX = destinationLocalCm.X - centerX;
            float toDestY = destinationLocalCm.Y - centerY;
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

            for (int i = 0; i < group.MemberCount; i++)
            {
                int unitIndex = group.MemberIndices[i];
                float rawTargetX = centerX + group.OffsetX[i] + pullX;
                float rawTargetY = centerY + group.OffsetY[i] + pullY;
                Vector2 resolvedTarget = simulation.ResolveUnitNavigableTarget(
                    unitIndex,
                    rawTargetX,
                    rawTargetY,
                    group.OffsetX[i],
                    group.OffsetY[i],
                    simulation.Semantics.TargetProjection.GroupSlotClearanceCm);
                simulation.SetUnitTarget(unitIndex, resolvedTarget.X, resolvedTarget.Y);
            }

            group.CenterX = centerX;
            group.CenterY = centerY;
            group.Arrived = distance < simulation.Semantics.Group.ArrivedRadiusCm;
        }

        ActiveGroupCount = CountActiveGroups();
    }

    private void EnsureMembershipCapacity(int count)
    {
        if (count <= _groupIdsByAgentIndex.Length)
        {
            return;
        }

        throw new InvalidOperationException(
            $"MassNavigation group membership required {count} agent slots, exceeding configured scenarioRuntime.runtimeCapacity.groupMembershipAgentCapacity {_groupIdsByAgentIndex.Length}.");
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

    private void DetachMembersFromOtherGroups(MassNavigationFlowSolverState simulation, ReadOnlySpan<int> members, int keepGroupId)
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

    private int AllocateGroupId()
    {
        if (TryFindAvailableGroupId(out int groupId))
        {
            return groupId;
        }

        throw new InvalidOperationException(
            $"MassNavigation navigation group allocation exceeded configured scenarioRuntime.runtimeCapacity.navigationGroupCapacity {_groups.Count}.");
    }

    private bool TryFindAvailableGroupId(out int groupId)
    {
        for (int i = 0; i < _groups.Count; i++)
        {
            if (_groups[i] == null)
            {
                groupId = i;
                return true;
            }
        }

        groupId = -1;
        return false;
    }

    private void RemoveFromExistingGroup(MassNavigationFlowSolverState simulation, int unitIndex)
    {
        int groupId = GetGroupId(unitIndex);
        if (groupId < 0)
        {
            return;
        }

        NavGroupState? group = _groups[groupId];
        _groupIdsByAgentIndex[unitIndex] = -1;
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
            group.MemberEntities[target] = group.MemberEntities[source];
            group.MemberOrderTargetWorldX[target] = group.MemberOrderTargetWorldX[source];
            group.MemberOrderTargetWorldY[target] = group.MemberOrderTargetWorldY[source];
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

        BuildCurrentRelativeOffsets(simulation, group);
        AssignGroupTargets(simulation, groupId, group, resetRecovery: true);
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
            if ((uint)unitIndex < (uint)_groupIdsByAgentIndex.Length)
            {
                _groupIdsByAgentIndex[unitIndex] = -1;
            }
        }

        _groups[groupId] = null;
    }

    private int GetGroupId(int unitIndex)
    {
        if ((uint)unitIndex >= (uint)_groupIdsByAgentIndex.Length)
        {
            return -1;
        }

        return _groupIdsByAgentIndex[unitIndex];
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

    private NavGroupState CreateGroup(
        int groupId,
        MassNavigationFlowSolverState simulation,
        MassNavigationAgentState agentState,
        ReadOnlySpan<int> members,
        int teamId)
    {
        if ((uint)groupId >= (uint)_groupPool.Length)
        {
            throw new InvalidOperationException(
                $"MassNavigation group id {groupId} exceeds configured scenarioRuntime.runtimeCapacity.navigationGroupCapacity {_groupPool.Length}.");
        }

        NavGroupState group = _groupPool[groupId];
        group.Reset(teamId);
        CopyMembersAndRebuildOffsets(simulation, agentState, group, members);
        return group;
    }

    private static Vector2 ResolveGroupWorldDestination(
        MassNavigationFlowSolverState simulation,
        ReadOnlySpan<int> memberIndices,
        int count,
        Vector2 destinationWorldCm)
    {
        Vector2 destinationLocalCm = simulation.WorldToLocalCm(destinationWorldCm);
        float centerClearanceCm = ResolveMemberMaxClearanceCm(
            simulation,
            memberIndices,
            count,
            simulation.Semantics.TargetProjection.GroupCenterClearanceCm);
        Vector2 resolvedDestination = simulation.ResolveNavigableTarget(
            destinationLocalCm.X,
            destinationLocalCm.Y,
            0f,
            0f,
            centerClearanceCm);
        return simulation.LocalToWorldCm(resolvedDestination);
    }

    private static Vector2 ResolveSingleMemberWorldDestination(
        MassNavigationFlowSolverState simulation,
        int unitIndex,
        Vector2 destinationWorldCm)
    {
        Vector2 destinationLocalCm = simulation.WorldToLocalCm(destinationWorldCm);
        Vector2 resolvedDestination = simulation.ResolveUnitNavigableTarget(
            unitIndex,
            destinationLocalCm.X,
            destinationLocalCm.Y,
            0f,
            0f,
            simulation.Semantics.TargetProjection.LooseTargetClearanceCm);
        return simulation.LocalToWorldCm(resolvedDestination);
    }

    private void ReplaceGroupMembers(
        MassNavigationFlowSolverState simulation,
        MassNavigationAgentState agentState,
        int groupId,
        NavGroupState group,
        ReadOnlySpan<int> nextMembers,
        int teamId)
    {
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            if ((uint)unitIndex < (uint)_groupIdsByAgentIndex.Length &&
                _groupIdsByAgentIndex[unitIndex] == groupId)
            {
                _groupIdsByAgentIndex[unitIndex] = -1;
                simulation.ReleaseUnitToTeamTarget(unitIndex);
            }
        }

        group.TeamId = teamId;
        CopyMembersAndRebuildOffsets(simulation, agentState, group, nextMembers);
    }

    private void AssignGroupTargets(MassNavigationFlowSolverState simulation, int groupId, NavGroupState group, bool resetRecovery)
    {
        Vector2 destinationLocalCm = simulation.WorldToLocalCm(new Vector2(group.DestinationWorldX, group.DestinationWorldY));
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            _groupIdsByAgentIndex[unitIndex] = groupId;
            Vector2 resolvedTarget = simulation.ResolveUnitNavigableTarget(
                unitIndex,
                destinationLocalCm.X + group.OffsetX[i],
                destinationLocalCm.Y + group.OffsetY[i],
                group.OffsetX[i],
                group.OffsetY[i],
                simulation.Semantics.TargetProjection.GroupSlotClearanceCm);
            simulation.SetUnitTarget(unitIndex, resolvedTarget.X, resolvedTarget.Y, resetRecovery);
            Vector2 resolvedTargetWorld = simulation.LocalToWorldCm(resolvedTarget);
            group.MemberOrderTargetWorldX[i] = resolvedTargetWorld.X;
            group.MemberOrderTargetWorldY[i] = resolvedTargetWorld.Y;
        }
    }

    private void AssignLooseOrderTargets(MassNavigationFlowSolverState simulation, int groupId, NavGroupState group, bool resetRecovery)
    {
        Vector2 destinationLocalCm = simulation.WorldToLocalCm(new Vector2(group.DestinationWorldX, group.DestinationWorldY));
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            _groupIdsByAgentIndex[unitIndex] = groupId;
            Vector2 resolvedTarget = simulation.ResolveUnitNavigableTarget(
                unitIndex,
                destinationLocalCm.X + group.OffsetX[i],
                destinationLocalCm.Y + group.OffsetY[i],
                group.OffsetX[i],
                group.OffsetY[i],
                simulation.Semantics.TargetProjection.LooseTargetClearanceCm);
            simulation.SetUnitTarget(unitIndex, resolvedTarget.X, resolvedTarget.Y, resetRecovery);
            Vector2 resolvedTargetWorld = simulation.LocalToWorldCm(resolvedTarget);
            group.MemberOrderTargetWorldX[i] = resolvedTargetWorld.X;
            group.MemberOrderTargetWorldY[i] = resolvedTargetWorld.Y;
        }
    }

    private static void UpdateLooseOrderGroupArrival(MassNavigationFlowSolverState simulation, NavGroupState group)
    {
        int liveCount = 0;
        float maxDistanceSq = 0f;
        Vector2 destinationLocalCm = simulation.WorldToLocalCm(new Vector2(group.DestinationWorldX, group.DestinationWorldY));
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            if ((uint)unitIndex >= (uint)simulation.UnitCount)
            {
                continue;
            }

            Vector2 targetLocal = simulation.WorldToLocalCm(new Vector2(group.MemberOrderTargetWorldX[i], group.MemberOrderTargetWorldY[i]));
            float targetX = targetLocal.X;
            float targetY = targetLocal.Y;
            float dx = targetX - simulation.GetPositionX(unitIndex);
            float dy = targetY - simulation.GetPositionY(unitIndex);
            maxDistanceSq = MathF.Max(maxDistanceSq, (dx * dx) + (dy * dy));
            liveCount++;
        }

        group.Arrived = liveCount <= 0 ||
            maxDistanceSq <= simulation.Semantics.Group.LooseArriveThresholdCm * simulation.Semantics.Group.LooseArriveThresholdCm;
    }

    private static void CopyMembersAndRebuildOffsets(
        MassNavigationFlowSolverState simulation,
        MassNavigationAgentState agentState,
        NavGroupState group,
        ReadOnlySpan<int> members)
    {
        group.EnsureCapacity(Math.Max(1, members.Length));
        for (int i = 0; i < members.Length; i++)
        {
            int agentIndex = members[i];
            if (!agentState.TryGetAgentEntity(agentIndex, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigation group member agent index {agentIndex} is not bound to a live entity.");
            }

            group.MemberIndices[i] = agentIndex;
            group.MemberEntities[i] = entity;
        }

        group.MemberCount = members.Length;
        BuildCurrentRelativeOffsets(simulation, group);
    }

    private static void ValidateGroupMembersBound(
        MassNavigationAgentState agentState,
        ReadOnlySpan<int> members)
    {
        for (int i = 0; i < members.Length; i++)
        {
            if (!agentState.TryGetAgentEntity(members[i], out _))
            {
                throw new InvalidOperationException(
                    $"MassNavigation group member agent index {members[i]} is not bound to a live entity.");
            }
        }
    }

    private static void BuildCurrentRelativeOffsets(MassNavigationFlowSolverState simulation, NavGroupState group)
    {
        float centerX = 0f;
        float centerY = 0f;
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            centerX += simulation.GetPositionX(unitIndex);
            centerY += simulation.GetPositionY(unitIndex);
        }

        float invCount = 1f / group.MemberCount;
        centerX *= invCount;
        centerY *= invCount;
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            float offsetX = simulation.GetPositionX(unitIndex) - centerX;
            float offsetY = simulation.GetPositionY(unitIndex) - centerY;
            group.OffsetX[i] = offsetX;
            group.OffsetY[i] = offsetY;
        }
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

    private bool TryResolveGroupMember(int unitIndex, out NavGroupState group, out int memberOffset)
    {
        int groupId = GetGroupId(unitIndex);
        if (groupId < 0 ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            group = null!;
            memberOffset = -1;
            return false;
        }

        group = _groups[groupId]!;
        memberOffset = IndexOfMember(group, unitIndex);
        if (memberOffset < 0)
        {
            group = null!;
            return false;
        }

        return true;
    }

    private static bool TryResolveAuthoredRebuildMemberIndex(
        World world,
        MassNavigationFlowSolverState simulation,
        Entity entity,
        out int agentIndex)
    {
        agentIndex = -1;
        if (entity == Entity.Null ||
            !world.IsAlive(entity) ||
            !world.TryGet(entity, out MassNavigationAgentIndex index))
        {
            return false;
        }

        if ((uint)index.Value >= (uint)simulation.UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation authored group restore resolved entity {entity.Id} to agent index {index.Value}, exceeding current MassNavigationFlow unit count {simulation.UnitCount}.");
        }

        agentIndex = index.Value;
        return true;
    }

    private static bool HaveSameRestoredOrderTargets(
        NavGroupState group,
        AuthoredRebuildGroupSnapshot captured,
        int[] memberSnapshotIndices,
        int restoredMemberCount)
    {
        bool allOrderTargetsPreserved = true;
        for (int i = 0; i < restoredMemberCount; i++)
        {
            AuthoredRebuildMemberSnapshot member = captured.Members[memberSnapshotIndices[i]];
            if (MathF.Abs(group.MemberOrderTargetWorldX[i] - member.OrderTargetWorldX) > OrderTargetRestoreToleranceCm ||
                MathF.Abs(group.MemberOrderTargetWorldY[i] - member.OrderTargetWorldY) > OrderTargetRestoreToleranceCm)
            {
                allOrderTargetsPreserved = false;
                continue;
            }

        }

        return allOrderTargetsPreserved;
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

    private static float ResolveMemberClearanceCm(
        MassNavigationFlowSolverState simulation,
        int unitIndex,
        float configuredClearanceCm)
    {
        if ((uint)unitIndex >= (uint)simulation.UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation group member agent index {unitIndex} exceeds MassNavigationFlow unit count {simulation.UnitCount}.");
        }

        return MathF.Max(configuredClearanceCm, simulation.GetBodyRadiusCm(unitIndex));
    }

    private static float ResolveMemberMaxClearanceCm(
        MassNavigationFlowSolverState simulation,
        ReadOnlySpan<int> memberIndices,
        int count,
        float configuredClearanceCm)
    {
        float clearanceCm = configuredClearanceCm;
        for (int i = 0; i < count; i++)
        {
            clearanceCm = MathF.Max(clearanceCm, ResolveMemberClearanceCm(simulation, memberIndices[i], configuredClearanceCm));
        }

        return clearanceCm;
    }

    internal sealed class AuthoredRebuildSnapshot
    {
        internal AuthoredRebuildSnapshot(AuthoredRebuildGroupSnapshot[] groups)
        {
            Groups = groups;
        }

        internal AuthoredRebuildGroupSnapshot[] Groups { get; }
    }

    internal sealed class AuthoredRebuildGroupSnapshot
    {
        public AuthoredRebuildGroupSnapshot(
            int groupId,
            int commandToken,
            float requestedDestinationWorldX,
            float requestedDestinationWorldY,
            float destinationWorldX,
            float destinationWorldY,
            float centerX,
            float centerY,
            bool arrived,
            AuthoredRebuildMemberSnapshot[] members)
        {
            GroupId = groupId;
            CommandToken = commandToken;
            RequestedDestinationWorldX = requestedDestinationWorldX;
            RequestedDestinationWorldY = requestedDestinationWorldY;
            DestinationWorldX = destinationWorldX;
            DestinationWorldY = destinationWorldY;
            CenterX = centerX;
            CenterY = centerY;
            Arrived = arrived;
            Members = members;
        }

        public int GroupId { get; }
        public int CommandToken { get; }
        public float RequestedDestinationWorldX { get; }
        public float RequestedDestinationWorldY { get; }
        public float DestinationWorldX { get; }
        public float DestinationWorldY { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public bool Arrived { get; }
        public AuthoredRebuildMemberSnapshot[] Members { get; }
    }

    internal readonly struct AuthoredRebuildMemberSnapshot
    {
        public AuthoredRebuildMemberSnapshot(
            Entity entity,
            float orderTargetWorldX,
            float orderTargetWorldY)
        {
            Entity = entity;
            OrderTargetWorldX = orderTargetWorldX;
            OrderTargetWorldY = orderTargetWorldY;
        }

        public Entity Entity { get; }
        public float OrderTargetWorldX { get; }
        public float OrderTargetWorldY { get; }
    }

    private sealed class NavGroupState
    {
        public NavGroupState(int initialCapacity, int teamId)
        {
            int capacity = Math.Max(1, initialCapacity);
            MemberIndices = new int[capacity];
            MemberEntities = new Entity[capacity];
            MemberOrderTargetWorldX = new float[capacity];
            MemberOrderTargetWorldY = new float[capacity];
            OffsetX = new float[capacity];
            OffsetY = new float[capacity];
            TeamId = teamId;
        }

        public int MemberCount { get; set; }
        public int TeamId { get; set; }
        public int CommandToken { get; set; }
        public int[] MemberIndices { get; private set; }
        public Entity[] MemberEntities { get; private set; }
        public float[] MemberOrderTargetWorldX { get; private set; }
        public float[] MemberOrderTargetWorldY { get; private set; }
        public float[] OffsetX { get; private set; }
        public float[] OffsetY { get; private set; }
        public float RequestedDestinationWorldX { get; set; }
        public float RequestedDestinationWorldY { get; set; }
        public float DestinationWorldX { get; set; }
        public float DestinationWorldY { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public bool Arrived { get; set; }

        public void Reset(int teamId)
        {
            MemberCount = 0;
            TeamId = teamId;
            CommandToken = 0;
            RequestedDestinationWorldX = 0f;
            RequestedDestinationWorldY = 0f;
            DestinationWorldX = 0f;
            DestinationWorldY = 0f;
            CenterX = 0f;
            CenterY = 0f;
            Arrived = false;
        }

        public void EnsureCapacity(int required)
        {
            if (required <= MemberIndices.Length)
            {
                return;
            }

            throw new InvalidOperationException(
                $"MassNavigation group state required {required} members, exceeding configured scenarioRuntime.runtimeCapacity.groupMemberCapacity {MemberIndices.Length}.");
        }
    }
}
