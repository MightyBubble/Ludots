using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavGroupRuntime
{
    private readonly MassNavFormationRuntime _formationLayout;
    private readonly List<NavGroupState?> _groups = new();
    private readonly HashSet<int> _visitedGroupIds = new();
    private readonly Dictionary<int, int> _orderTokenToGroupId = new();
    private readonly List<int> _orderTokensToRemove = new();
    private int[] _groupIdsByControllableIndex = Array.Empty<int>();

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
        MassNavWebParitySimState simulation,
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
        int[] memberIndices = new int[count];
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
        int[] exactMembers = new int[assignedCount];
        Array.Copy(memberIndices, exactMembers, assignedCount);
        var group = CreateGroup(exactMembers, teamId, formationMode, previousRotation);
        group.DestinationX = resolvedDestination.X;
        group.DestinationY = resolvedDestination.Y;
        _groups[groupId] = group;
        AssignGroupTargets(simulation, groupId, group, resetRecovery: true);

        ActiveGroupCount = CountActiveGroups();
        RefreshSelectedRotation(agentState, selected);
        return assignedCount;
    }

    public int UpsertOrderMoveCommand(
        MassNavWebParitySimState simulation,
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
            int[] exactMembers = memberIndices.ToArray();
            DetachMembersFromOtherGroups(simulation, exactMembers, keepGroupId: -1);
            EnsureMembershipCapacityForMembers(exactMembers);
            groupId = AllocateGroupId();
            var created = CreateGroup(exactMembers, teamId, formationMode, rotationRadians);
            created.CommandToken = orderToken;
            created.DestinationX = resolvedDestination.X;
            created.DestinationY = resolvedDestination.Y;
            _groups[groupId] = created;
            _orderTokenToGroupId[orderToken] = groupId;
            AssignGroupTargets(simulation, groupId, created, resetRecovery: true);
            ActiveGroupCount = CountActiveGroups();
            return exactMembers.Length;
        }

        NavGroupState group = _groups[groupId]!;
        bool rebuildLayout = group.FormationMode != formationMode || !HaveSameMembers(group.MemberIndices, memberIndices);
        if (rebuildLayout)
        {
            int[] exactMembers = memberIndices.ToArray();
            DetachMembersFromOtherGroups(simulation, exactMembers, groupId);
            EnsureMembershipCapacityForMembers(exactMembers);
            ReplaceGroupMembers(simulation, groupId, group, exactMembers, teamId, formationMode, rotationRadians);
        }
        group.TeamId = teamId;
        group.CommandToken = orderToken;
        group.DestinationX = resolvedDestination.X;
        group.DestinationY = resolvedDestination.Y;
        _groups[groupId] = group;
        AssignGroupTargets(simulation, groupId, group, resetRecovery: rebuildLayout);
        ActiveGroupCount = CountActiveGroups();
        return group.MemberIndices.Length;
    }

    public bool TryGetOrderGroup(int orderToken, out bool arrived, out int[] members)
    {
        arrived = false;
        members = Array.Empty<int>();
        if (!_orderTokenToGroupId.TryGetValue(orderToken, out int groupId) ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            return false;
        }

        NavGroupState group = _groups[groupId]!;
        arrived = group.Arrived;
        members = group.MemberIndices;
        return true;
    }

    public void CompleteOrderGroup(MassNavWebParitySimState simulation, int orderToken)
    {
        if (!_orderTokenToGroupId.TryGetValue(orderToken, out int groupId) ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            return;
        }

        NavGroupState group = _groups[groupId]!;
        for (int i = 0; i < group.MemberIndices.Length; i++)
        {
            simulation.ClearUnitTarget(group.MemberIndices[i]);
        }

        DissolveGroup(groupId, group);
        ActiveGroupCount = CountActiveGroups();
    }

    public void PruneInactiveOrderGroups(MassNavWebParitySimState simulation, HashSet<int> activeTokens)
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
            for (int memberIndex = 0; memberIndex < group.MemberIndices.Length; memberIndex++)
            {
                simulation.ClearUnitTarget(group.MemberIndices[memberIndex]);
            }

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
            _formationLayout.RecomputeOffsets(group.OffsetX, group.OffsetY, group.BaseOffsetX, group.BaseOffsetY, group.RotationRadians);
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
        MassNavWebParitySimState simulation,
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
            for (int i = 0; i < group.MemberIndices.Length; i++)
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

            for (int i = 0; i < group.MemberIndices.Length; i++)
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

    private void EnsureMembershipCapacityForMembers(int[] members)
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

    private void DetachMembersFromOtherGroups(MassNavWebParitySimState simulation, ReadOnlySpan<int> members, int keepGroupId)
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

    private void RemoveFromExistingGroup(MassNavWebParitySimState simulation, int unitIndex)
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

        int memberPosition = Array.IndexOf(group.MemberIndices, unitIndex);
        if (memberPosition < 0)
        {
            return;
        }

        if (group.MemberIndices.Length <= 1)
        {
            DissolveGroup(groupId, group);
            return;
        }

        int nextLength = group.MemberIndices.Length - 1;
        int[] nextMembers = new int[nextLength];
        float[] nextBaseX = new float[nextLength];
        float[] nextBaseY = new float[nextLength];
        float[] nextOffsetX = new float[nextLength];
        float[] nextOffsetY = new float[nextLength];
        for (int source = 0, target = 0; source < group.MemberIndices.Length; source++)
        {
            if (source == memberPosition)
            {
                continue;
            }

            nextMembers[target] = group.MemberIndices[source];
            nextBaseX[target] = group.BaseOffsetX[source];
            nextBaseY[target] = group.BaseOffsetY[source];
            nextOffsetX[target] = group.OffsetX[source];
            nextOffsetY[target] = group.OffsetY[source];
            target++;
        }

        group.MemberIndices = nextMembers;
        group.BaseOffsetX = nextBaseX;
        group.BaseOffsetY = nextBaseY;
        group.OffsetX = nextOffsetX;
        group.OffsetY = nextOffsetY;

        if (group.MemberIndices.Length <= 1)
        {
            for (int i = 0; i < group.MemberIndices.Length; i++)
            {
                simulation.ClearUnitTarget(group.MemberIndices[i]);
            }

            DissolveGroup(groupId, group);
        }
    }

    private void DissolveGroup(int groupId, NavGroupState group)
    {
        if (group.CommandToken > 0)
        {
            _orderTokenToGroupId.Remove(group.CommandToken);
        }

        for (int i = 0; i < group.MemberIndices.Length; i++)
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
        MassNavWebParitySimState simulation,
        int[] memberIndices,
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

    private NavGroupState CreateGroup(int[] exactMembers, int teamId, MassNavFormationMode formationMode, float rotationRadians)
    {
        float[] baseOffsetX = new float[exactMembers.Length];
        float[] baseOffsetY = new float[exactMembers.Length];
        float[] offsetX = new float[exactMembers.Length];
        float[] offsetY = new float[exactMembers.Length];
        _formationLayout.BuildOffsets(baseOffsetX, baseOffsetY, offsetX, offsetY, exactMembers.Length, formationMode, rotationRadians);
        return new NavGroupState(exactMembers, baseOffsetX, baseOffsetY, offsetX, offsetY, teamId)
        {
            RotationRadians = rotationRadians,
            FormationMode = formationMode,
        };
    }

    private void ReplaceGroupMembers(
        MassNavWebParitySimState simulation,
        int groupId,
        NavGroupState group,
        int[] nextMembers,
        int teamId,
        MassNavFormationMode formationMode,
        float rotationRadians)
    {
        for (int i = 0; i < group.MemberIndices.Length; i++)
        {
            int unitIndex = group.MemberIndices[i];
            if ((uint)unitIndex < (uint)_groupIdsByControllableIndex.Length &&
                _groupIdsByControllableIndex[unitIndex] == groupId)
            {
                _groupIdsByControllableIndex[unitIndex] = -1;
                simulation.ClearUnitTarget(unitIndex);
            }
        }

        group.MemberIndices = nextMembers;
        group.TeamId = teamId;
        group.FormationMode = formationMode;
        group.RotationRadians = rotationRadians;
        group.BaseOffsetX = new float[nextMembers.Length];
        group.BaseOffsetY = new float[nextMembers.Length];
        group.OffsetX = new float[nextMembers.Length];
        group.OffsetY = new float[nextMembers.Length];
        _formationLayout.BuildOffsets(group.BaseOffsetX, group.BaseOffsetY, group.OffsetX, group.OffsetY, nextMembers.Length, formationMode, rotationRadians);
    }

    private void AssignGroupTargets(MassNavWebParitySimState simulation, int groupId, NavGroupState group, bool resetRecovery)
    {
        for (int i = 0; i < group.MemberIndices.Length; i++)
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

    private static bool HaveSameMembers(int[] left, ReadOnlySpan<int> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private sealed class NavGroupState
    {
        public NavGroupState(int[] memberIndices, float[] baseOffsetX, float[] baseOffsetY, float[] offsetX, float[] offsetY, int teamId)
        {
            MemberIndices = memberIndices;
            BaseOffsetX = baseOffsetX;
            BaseOffsetY = baseOffsetY;
            OffsetX = offsetX;
            OffsetY = offsetY;
            TeamId = teamId;
        }

        public int TeamId { get; set; }
        public int CommandToken { get; set; }
        public MassNavFormationMode FormationMode { get; set; }
        public int[] MemberIndices { get; set; }
        public float[] BaseOffsetX { get; set; }
        public float[] BaseOffsetY { get; set; }
        public float[] OffsetX { get; set; }
        public float[] OffsetY { get; set; }
        public float DestinationX { get; set; }
        public float DestinationY { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float RotationRadians { get; set; }
        public bool Arrived { get; set; }
    }
}
