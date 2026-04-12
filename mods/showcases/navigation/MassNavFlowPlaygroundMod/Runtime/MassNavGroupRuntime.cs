using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;

namespace MassNavFlowPlaygroundMod.Runtime;

public sealed class MassNavGroupRuntime
{
    private readonly MassNavFormationRuntime _formationLayout;
    private readonly List<NavGroupState?> _groups = new();
    private readonly HashSet<int> _visitedGroupIds = new();
    private readonly Dictionary<int, int> _orderTokenToGroupId = new();
    private readonly List<int> _orderTokensToRemove = new();
    private int[] _groupIdsByControllableIndex = Array.Empty<int>();
    private int[] _selectionMemberScratch = Array.Empty<int>();

    public MassNavGroupRuntime(MassNavFormationRuntime formationLayout)
    {
        _formationLayout = formationLayout ?? throw new ArgumentNullException(nameof(formationLayout));
    }

    public int ActiveGroupCount { get; private set; }
    public float SelectedRotationRadians { get; private set; }

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
    }

    public bool HasGroup(int unitIndex)
    {
        return GetGroupId(unitIndex) >= 0;
    }

    public int IssueSelectionMoveCommand(
        MassNavFlowPlaygroundSimState simulation,
        MassNavAgentState agentState,
        ReadOnlySpan<Entity> selected,
        Vector2 destinationCm,
        MassNavFormationMode formationMode)
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
            simulation.ClearUnitTarget(unitIndex);
        }

        if (assignedCount <= 0)
        {
            return 0;
        }

        if (formationMode == MassNavFormationMode.None || assignedCount == 1)
        {
            AssignLooseTargets(simulation, memberIndices, assignedCount, destinationCm, previousRotation);
            ActiveGroupCount = CountActiveGroups();
            RefreshSelectedRotation(agentState, selected);
            return assignedCount;
        }

        int teamId = simulation.GetTeam(memberIndices[0]);
        Vector2 resolvedDestination = simulation.ResolveNavigableTarget(
            destinationCm.X,
            destinationCm.Y,
            0f,
            0f,
            simulation.Semantics.TargetProjection.GroupCenterClearanceCm);

        int groupId = AllocateGroupId();
        var group = CreateGroup(memberIndices[..assignedCount], teamId, formationMode, previousRotation);
        group.DestinationX = resolvedDestination.X;
        group.DestinationY = resolvedDestination.Y;
        _groups[groupId] = group;
        AssignGroupTargets(simulation, groupId, group, resetRecovery: true);

        ActiveGroupCount = CountActiveGroups();
        RefreshSelectedRotation(agentState, selected);
        return assignedCount;
    }

    public int UpsertOrderMoveCommand(
        MassNavFlowPlaygroundSimState simulation,
        int orderToken,
        ReadOnlySpan<int> memberIndices,
        int teamId,
        Vector2 destinationCm,
        MassNavFormationMode formationMode,
        float rotationRadians)
    {
        if (orderToken <= 0 || memberIndices.Length <= 0)
        {
            return 0;
        }

        Vector2 resolvedDestination = simulation.ResolveNavigableTarget(
            destinationCm.X,
            destinationCm.Y,
            0f,
            0f,
            simulation.Semantics.TargetProjection.GroupCenterClearanceCm);

        if (!_orderTokenToGroupId.TryGetValue(orderToken, out int groupId) ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            DetachMembersFromOtherGroups(simulation, memberIndices, keepGroupId: -1);
            EnsureMembershipCapacityForMembers(memberIndices);
            groupId = AllocateGroupId();
            var created = CreateGroup(memberIndices, teamId, formationMode, rotationRadians);
            created.CommandToken = orderToken;
            created.DestinationX = resolvedDestination.X;
            created.DestinationY = resolvedDestination.Y;
            _groups[groupId] = created;
            _orderTokenToGroupId[orderToken] = groupId;
            AssignGroupTargets(simulation, groupId, created, resetRecovery: true);
            ActiveGroupCount = CountActiveGroups();
            return memberIndices.Length;
        }

        NavGroupState group = _groups[groupId]!;
        bool rebuildLayout = group.FormationMode != formationMode || !HaveSameMembers(group, memberIndices);
        if (rebuildLayout)
        {
            DetachMembersFromOtherGroups(simulation, memberIndices, groupId);
            EnsureMembershipCapacityForMembers(memberIndices);
            ReplaceGroupMembers(simulation, groupId, group, memberIndices, teamId, formationMode, rotationRadians);
        }

        group.TeamId = teamId;
        group.CommandToken = orderToken;
        group.DestinationX = resolvedDestination.X;
        group.DestinationY = resolvedDestination.Y;
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

    public void CompleteOrderGroup(MassNavFlowPlaygroundSimState simulation, int orderToken)
    {
        if (!_orderTokenToGroupId.TryGetValue(orderToken, out int groupId) ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            return;
        }

        NavGroupState group = _groups[groupId]!;
        ParkGroupMembers(simulation, group);
        DissolveGroup(groupId, group);
        ActiveGroupCount = CountActiveGroups();
    }

    public void PruneInactiveOrderGroups(MassNavFlowPlaygroundSimState simulation, HashSet<int> activeTokens)
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
            ParkGroupMembers(simulation, group);
            DissolveGroup(groupId, group);
        }

        ActiveGroupCount = CountActiveGroups();
    }

    public void RotateSelected(MassNavAgentState agentState, ReadOnlySpan<Entity> selected, float deltaRadians)
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
        }

        RefreshSelectedRotation(agentState, selected);
    }

    public void RefreshSelectedRotation(MassNavAgentState agentState, ReadOnlySpan<Entity> selected)
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
        MassNavFlowPlaygroundSimState simulation,
        MassNavAgentState agentState,
        ReadOnlySpan<Entity> selected,
        int frameIndex)
    {
        if ((frameIndex & 1) == 0)
        {
            RefreshSelectedRotation(agentState, selected);
            return;
        }

        for (int groupId = 0; groupId < _groups.Count; groupId++)
        {
            NavGroupState? group = _groups[groupId];
            if (group == null)
            {
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
            float toDestX = group.DestinationX - centerX;
            float toDestY = group.DestinationY - centerY;
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
                Vector2 resolvedTarget = simulation.ResolveNavigableTarget(
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

    private void DetachMembersFromOtherGroups(MassNavFlowPlaygroundSimState simulation, ReadOnlySpan<int> members, int keepGroupId)
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

    private float ResolvePreviousRotation(MassNavAgentState agentState, ReadOnlySpan<Entity> selected)
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

    private void RemoveFromExistingGroup(MassNavFlowPlaygroundSimState simulation, int unitIndex)
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
                ParkUnitAtCurrentPosition(simulation, group.MemberIndices[i]);
            }

            DissolveGroup(groupId, group);
            return;
        }

        RebuildGroupLayout(group);
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
        MassNavFlowPlaygroundSimState simulation,
        ReadOnlySpan<int> memberIndices,
        int count,
        Vector2 destinationCm,
        float rotationRadians)
    {
        if (count <= 0)
        {
            return;
        }

        if (count == 1)
        {
            Vector2 singleTarget = simulation.ResolveNavigableTarget(
                destinationCm.X,
                destinationCm.Y,
                0f,
                0f,
                simulation.Semantics.TargetProjection.LooseTargetClearanceCm);
            simulation.SetUnitTarget(memberIndices[0], singleTarget.X, singleTarget.Y, resetRecovery: true);
            return;
        }

        float[] baseOffsetX = new float[count];
        float[] baseOffsetY = new float[count];
        float[] offsetX = new float[count];
        float[] offsetY = new float[count];
        _formationLayout.BuildOffsets(baseOffsetX, baseOffsetY, offsetX, offsetY, count, MassNavFormationMode.Square, rotationRadians);
        Vector2 resolvedCenter = simulation.ResolveNavigableTarget(
            destinationCm.X,
            destinationCm.Y,
            0f,
            0f,
            simulation.Semantics.TargetProjection.GroupCenterClearanceCm);
        for (int i = 0; i < count; i++)
        {
            Vector2 resolvedTarget = simulation.ResolveNavigableTarget(
                resolvedCenter.X + offsetX[i],
                resolvedCenter.Y + offsetY[i],
                offsetX[i],
                offsetY[i],
                simulation.Semantics.TargetProjection.LooseTargetClearanceCm);
            simulation.SetUnitTarget(memberIndices[i], resolvedTarget.X, resolvedTarget.Y, resetRecovery: true);
        }
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

    private NavGroupState CreateGroup(ReadOnlySpan<int> members, int teamId, MassNavFormationMode formationMode, float rotationRadians)
    {
        var group = new NavGroupState(Math.Max(1, members.Length), teamId);
        CopyMembersAndRebuildLayout(group, members, formationMode, rotationRadians);
        return group;
    }

    private void ReplaceGroupMembers(
        MassNavFlowPlaygroundSimState simulation,
        int groupId,
        NavGroupState group,
        ReadOnlySpan<int> nextMembers,
        int teamId,
        MassNavFormationMode formationMode,
        float rotationRadians)
    {
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            if ((uint)unitIndex < (uint)_groupIdsByControllableIndex.Length &&
                _groupIdsByControllableIndex[unitIndex] == groupId)
            {
                _groupIdsByControllableIndex[unitIndex] = -1;
                simulation.ClearUnitTarget(unitIndex);
            }
        }

        group.TeamId = teamId;
        CopyMembersAndRebuildLayout(group, nextMembers, formationMode, rotationRadians);
    }

    private void AssignGroupTargets(MassNavFlowPlaygroundSimState simulation, int groupId, NavGroupState group, bool resetRecovery)
    {
        for (int i = 0; i < group.MemberCount; i++)
        {
            int unitIndex = group.MemberIndices[i];
            _groupIdsByControllableIndex[unitIndex] = groupId;
            Vector2 resolvedTarget = simulation.ResolveNavigableTarget(
                group.DestinationX + group.OffsetX[i],
                group.DestinationY + group.OffsetY[i],
                group.OffsetX[i],
                group.OffsetY[i],
                simulation.Semantics.TargetProjection.GroupSlotClearanceCm);
            simulation.SetUnitTarget(unitIndex, resolvedTarget.X, resolvedTarget.Y, resetRecovery);
        }
    }

    private static void ParkGroupMembers(MassNavFlowPlaygroundSimState simulation, NavGroupState group)
    {
        for (int i = 0; i < group.MemberCount; i++)
        {
            ParkUnitAtCurrentPosition(simulation, group.MemberIndices[i]);
        }
    }

    private static void ParkUnitAtCurrentPosition(MassNavFlowPlaygroundSimState simulation, int unitIndex)
    {
        simulation.SetUnitTarget(
            unitIndex,
            simulation.GetPositionX(unitIndex),
            simulation.GetPositionY(unitIndex),
            resetRecovery: true);
    }

    private void CopyMembersAndRebuildLayout(
        NavGroupState group,
        ReadOnlySpan<int> members,
        MassNavFormationMode formationMode,
        float rotationRadians)
    {
        group.EnsureCapacity(Math.Max(1, members.Length));
        members.CopyTo(group.MemberIndices);
        group.MemberCount = members.Length;
        group.FormationMode = formationMode;
        group.RotationRadians = rotationRadians;
        RebuildGroupLayout(group);
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
        public int TeamId { get; set; }
        public int CommandToken { get; set; }
        public MassNavFormationMode FormationMode { get; set; }
        public int[] MemberIndices { get; private set; }
        public float[] BaseOffsetX { get; private set; }
        public float[] BaseOffsetY { get; private set; }
        public float[] OffsetX { get; private set; }
        public float[] OffsetY { get; private set; }
        public float DestinationX { get; set; }
        public float DestinationY { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float RotationRadians { get; set; }
        public bool Arrived { get; set; }

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
