using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationGroupRuntime
{
    internal const float OrderPathRestoreTargetToleranceCm = 0.001f;

    private readonly MassNavigationFormationRuntime _formationLayout;
    private readonly List<NavGroupState?> _groups;
    private readonly HashSet<int> _visitedGroupIds;
    private readonly Dictionary<int, int> _orderTokenToGroupId;
    private readonly List<int> _orderTokensToRemove;
    private readonly int[] _groupIdsByAgentIndex;
    private readonly int[] _commandActorMemberScratch;
    private readonly float[] _looseBaseOffsetX;
    private readonly float[] _looseBaseOffsetY;
    private readonly float[] _looseOffsetX;
    private readonly float[] _looseOffsetY;
    private readonly NavGroupState[] _groupPool;
    private readonly int _groupMemberCapacity;

    public MassNavigationGroupRuntime(
        MassNavigationGroupSemantics groupSemantics,
        MassNavigationRuntimeCapacityConfig capacity)
        : this(new MassNavigationFormationRuntime(groupSemantics), capacity)
    {
    }

    public MassNavigationGroupRuntime(
        MassNavigationFormationRuntime formationLayout,
        MassNavigationRuntimeCapacityConfig capacity)
    {
        _formationLayout = formationLayout ?? throw new ArgumentNullException(nameof(formationLayout));
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

        _visitedGroupIds = new HashSet<int>(capacity.NavigationGroupCapacity);
        _orderTokenToGroupId = new Dictionary<int, int>(capacity.NavigationGroupCapacity);
        _orderTokensToRemove = new List<int>(capacity.NavigationGroupCapacity);
        _groupIdsByAgentIndex = new int[capacity.GroupMembershipAgentCapacity];
        Array.Fill(_groupIdsByAgentIndex, -1);
        _commandActorMemberScratch = new int[capacity.CommandActorScratchCapacity];
        _looseBaseOffsetX = new float[capacity.GroupMemberCapacity];
        _looseBaseOffsetY = new float[capacity.GroupMemberCapacity];
        _looseOffsetX = new float[capacity.GroupMemberCapacity];
        _looseOffsetY = new float[capacity.GroupMemberCapacity];
    }

    public int ActiveGroupCount { get; private set; }
    public int ActiveOrderGroupCount => _orderTokenToGroupId.Count;
    public float CommandActorRotationRadians { get; private set; }

    public void Reset()
    {
        for (int i = 0; i < _groups.Count; i++)
        {
            _groups[i] = null;
        }

        _orderTokenToGroupId.Clear();
        _orderTokensToRemove.Clear();
        _visitedGroupIds.Clear();
        Array.Fill(_groupIdsByAgentIndex, -1);
        ActiveGroupCount = 0;
        CommandActorRotationRadians = 0f;
    }

    internal AuthoredRebuildSnapshot CaptureAuthoredRebuildSnapshot()
    {
        if (ActiveGroupCount <= 0)
        {
            return new AuthoredRebuildSnapshot(Array.Empty<AuthoredRebuildGroupSnapshot>(), CommandActorRotationRadians);
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
                    group.MemberOrderTargetWorldY[memberIndex],
                    group.MemberOrderPathStartWorldX[memberIndex],
                    group.MemberOrderPathStartWorldY[memberIndex],
                    group.MemberOrderPathAnchorWorldX[memberIndex],
                    group.MemberOrderPathAnchorWorldY[memberIndex],
                    group.MemberOrderPathAnchorRevision[memberIndex],
                    group.MemberOrderPathAnchorInitialized[memberIndex]);
            }

            groups[capturedCount++] = new AuthoredRebuildGroupSnapshot(
                groupId,
                group.CommandToken,
                group.FormationMode,
                group.RequestedDestinationWorldX,
                group.RequestedDestinationWorldY,
                group.DestinationWorldX,
                group.DestinationWorldY,
                group.CenterX,
                group.CenterY,
                group.RotationRadians,
                group.Arrived,
                members);
        }

        if (capturedCount != groups.Length)
        {
            Array.Resize(ref groups, capturedCount);
        }

        return new AuthoredRebuildSnapshot(groups, CommandActorRotationRadians);
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

        CommandActorRotationRadians = snapshot.CommandActorRotationRadians;
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
            CopyMembersAndRebuildLayout(
                simulation,
                agentState,
                group,
                memberIndices.AsSpan(0, restoredMemberCount),
                captured.FormationMode,
                captured.RotationRadians);
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

            bool orderTargetsPreserved = RestoreCompatibleOrderPathState(group, captured, memberSnapshotIndices, restoredMemberCount);
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

    public bool TryUpdateGroupMemberOrderPathAnchor(
        MassNavigationFlowSolverState simulation,
        int unitIndex,
        float lookaheadCm,
        float updateEpsilonCm,
        out float anchorWorldX,
        out float anchorWorldY,
        out int anchorRevision)
    {
        if (!(lookaheadCm > 0f))
        {
            throw new InvalidOperationException("MassNavigation order path anchor requires lookaheadCm > 0.");
        }

        if (!(updateEpsilonCm > 0f))
        {
            throw new InvalidOperationException("MassNavigation order path anchor requires updateEpsilonCm > 0.");
        }

        return TryUpdateGroupMemberOrderPathAnchorCore(
            simulation,
            unitIndex,
            lookaheadCm,
            updateEpsilonCm,
            out anchorWorldX,
            out anchorWorldY,
            out anchorRevision,
            out _,
            out _);
    }

    public bool TryUpdateGroupMemberFollowerAnchor(
        MassNavigationFlowSolverState simulation,
        int unitIndex,
        float lookaheadCm,
        float updateEpsilonCm,
        out float anchorWorldX,
        out float anchorWorldY,
        out int anchorRevision)
    {
        if (!(lookaheadCm > 0f))
        {
            throw new InvalidOperationException("MassNavigation group member follower anchor requires lookaheadCm > 0.");
        }

        if (!(updateEpsilonCm > 0f))
        {
            throw new InvalidOperationException("MassNavigation group member follower anchor requires updateEpsilonCm > 0.");
        }

        if (!TryUpdateGroupMemberOrderPathAnchorCore(
                simulation,
                unitIndex,
                lookaheadCm,
                updateEpsilonCm,
                out float pathAnchorWorldX,
                out float pathAnchorWorldY,
                out anchorRevision,
                out float passiveOffsetLocalX,
                out float passiveOffsetLocalY))
        {
            anchorWorldX = 0f;
            anchorWorldY = 0f;
            return false;
        }

        Vector2 pathAnchorLocal = simulation.WorldToLocalCm(new Vector2(pathAnchorWorldX, pathAnchorWorldY));
        Vector2 followerAnchorWorld = simulation.LocalToWorldCm(new Vector2(
            pathAnchorLocal.X + passiveOffsetLocalX,
            pathAnchorLocal.Y + passiveOffsetLocalY));
        anchorWorldX = followerAnchorWorld.X;
        anchorWorldY = followerAnchorWorld.Y;
        return true;
    }

    private bool TryUpdateGroupMemberOrderPathAnchorCore(
        MassNavigationFlowSolverState simulation,
        int unitIndex,
        float lookaheadCm,
        float updateEpsilonCm,
        out float anchorWorldX,
        out float anchorWorldY,
        out int anchorRevision,
        out float passiveOffsetLocalX,
        out float passiveOffsetLocalY)
    {
        if (!TryResolveGroupMember(unitIndex, out NavGroupState group, out int memberOffset))
        {
            anchorWorldX = 0f;
            anchorWorldY = 0f;
            anchorRevision = 0;
            passiveOffsetLocalX = 0f;
            passiveOffsetLocalY = 0f;
            return false;
        }

        Vector2 finalTargetLocalCm = simulation.WorldToLocalCm(new Vector2(
            group.MemberOrderTargetWorldX[memberOffset],
            group.MemberOrderTargetWorldY[memberOffset]));
        Vector2 orderStartLocalCm = simulation.WorldToLocalCm(new Vector2(
            group.MemberOrderPathStartWorldX[memberOffset],
            group.MemberOrderPathStartWorldY[memberOffset]));
        float currentLocalX = simulation.GetPositionX(unitIndex);
        float currentLocalY = simulation.GetPositionY(unitIndex);
        ResolveOrderPathLookaheadAnchor(
            orderStartLocalCm.X,
            orderStartLocalCm.Y,
            currentLocalX,
            currentLocalY,
            finalTargetLocalCm.X,
            finalTargetLocalCm.Y,
            lookaheadCm,
            out float anchorLocalX,
            out float anchorLocalY);
        ResolveOrderPathPassiveOffset(
            orderStartLocalCm.X,
            orderStartLocalCm.Y,
            currentLocalX,
            currentLocalY,
            finalTargetLocalCm.X,
            finalTargetLocalCm.Y,
            out passiveOffsetLocalX,
            out passiveOffsetLocalY);
        Vector2 nextAnchorWorldCm = simulation.LocalToWorldCm(new Vector2(anchorLocalX, anchorLocalY));
        float updateEpsilonSq = updateEpsilonCm * updateEpsilonCm;
        float deltaX = nextAnchorWorldCm.X - group.MemberOrderPathAnchorWorldX[memberOffset];
        float deltaY = nextAnchorWorldCm.Y - group.MemberOrderPathAnchorWorldY[memberOffset];
        if (group.MemberOrderPathAnchorInitialized[memberOffset] == 0 ||
            (deltaX * deltaX) + (deltaY * deltaY) >= updateEpsilonSq)
        {
            group.MemberOrderPathAnchorWorldX[memberOffset] = nextAnchorWorldCm.X;
            group.MemberOrderPathAnchorWorldY[memberOffset] = nextAnchorWorldCm.Y;
            group.MemberOrderPathAnchorInitialized[memberOffset] = 1;
            group.MemberOrderPathAnchorRevision[memberOffset]++;
        }

        anchorWorldX = group.MemberOrderPathAnchorWorldX[memberOffset];
        anchorWorldY = group.MemberOrderPathAnchorWorldY[memberOffset];
        anchorRevision = group.MemberOrderPathAnchorRevision[memberOffset];
        return true;
    }

    public int IssueCommandActorMoveCommand(
        MassNavigationFlowSolverState simulation,
        World world,
        MassNavigationAgentState agentState,
        ReadOnlySpan<Entity> commandActors,
        Vector2 destinationWorldCm,
        MassNavigationFormationMode formationMode)
    {
        int count = commandActors.Length;
        if (count <= 0)
        {
            return 0;
        }

        float previousRotation = ResolvePreviousRotation(world, agentState, commandActors);

        int assignedCount = 0;
        Span<int> memberIndices = EnsureCommandActorMemberScratch(count);
        for (int i = 0; i < count; i++)
        {
            if (!agentState.TryGetControllableIndex(commandActors[i], out int unitIndex))
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

        EnsureMembershipCapacityForMembers(memberIndices[..assignedCount]);
        if (assignedCount == 1)
        {
            AssignLooseTargets(simulation, memberIndices, assignedCount, destinationWorldCm, previousRotation);
            ActiveGroupCount = CountActiveGroups();
            RefreshCommandActorRotation(world, agentState, commandActors);
            return assignedCount;
        }

        int teamId = simulation.GetTeam(memberIndices[0]);
        Vector2 destinationLocalCm = simulation.WorldToLocalCm(destinationWorldCm);
        float centerClearanceCm = ResolveMemberMaxClearanceCm(
            simulation,
            memberIndices,
            assignedCount,
            simulation.Semantics.TargetProjection.GroupCenterClearanceCm);
        Vector2 resolvedDestination = simulation.ResolveNavigableTarget(
            destinationLocalCm.X,
            destinationLocalCm.Y,
            0f,
            0f,
            centerClearanceCm);

        int groupId = AllocateGroupId();
        var group = CreateGroup(groupId, simulation, agentState, memberIndices[..assignedCount], teamId, formationMode, previousRotation);
        Vector2 resolvedWorldDestination = simulation.LocalToWorldCm(resolvedDestination);
        group.DestinationWorldX = resolvedWorldDestination.X;
        group.DestinationWorldY = resolvedWorldDestination.Y;
        _groups[groupId] = group;
        AssignGroupTargets(simulation, groupId, group, resetRecovery: true);

        ActiveGroupCount = CountActiveGroups();
        RefreshCommandActorRotation(world, agentState, commandActors);
        return assignedCount;
    }

    public int UpsertOrderMoveCommand(
        MassNavigationFlowSolverState simulation,
        MassNavigationAgentState agentState,
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

        bool singleMemberOrder = memberIndices.Length == 1;
        if (!_orderTokenToGroupId.TryGetValue(orderToken, out int groupId) ||
            (uint)groupId >= (uint)_groups.Count ||
            _groups[groupId] == null)
        {
            Vector2 resolvedWorldDestination = singleMemberOrder
                ? ResolveSingleMemberWorldDestination(simulation, memberIndices[0], destinationWorldCm)
                : ResolveGroupWorldDestination(simulation, memberIndices, memberIndices.Length, destinationWorldCm);
            DetachMembersFromOtherGroups(simulation, memberIndices, keepGroupId: -1);
            EnsureMembershipCapacityForMembers(memberIndices);
            groupId = AllocateGroupId();
            var created = CreateGroup(groupId, simulation, agentState, memberIndices, teamId, formationMode, rotationRadians);
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
            return memberIndices.Length;
        }

        NavGroupState group = _groups[groupId]!;
        bool rotationChanged =
            formationMode != MassNavigationFormationMode.None &&
            MathF.Abs(NormalizeAngleRadians(group.RotationRadians - rotationRadians)) >= _formationLayout.RotationEpsilonRadians;
        bool rebuildLayout = group.FormationMode != formationMode || rotationChanged || !HaveSameMembers(group, memberIndices);
        bool retarget =
            group.TeamId != teamId ||
            group.RequestedDestinationWorldX != destinationWorldCm.X ||
            group.RequestedDestinationWorldY != destinationWorldCm.Y ||
            rebuildLayout;
        if (!retarget)
        {
            return group.MemberCount;
        }

        Vector2 nextResolvedWorldDestination = singleMemberOrder
            ? ResolveSingleMemberWorldDestination(simulation, memberIndices[0], destinationWorldCm)
            : ResolveGroupWorldDestination(simulation, memberIndices, memberIndices.Length, destinationWorldCm);
        if (rebuildLayout)
        {
            DetachMembersFromOtherGroups(simulation, memberIndices, groupId);
            EnsureMembershipCapacityForMembers(memberIndices);
            ReplaceGroupMembers(simulation, agentState, groupId, group, memberIndices, teamId, formationMode, rotationRadians);
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
            AssignLooseOrderTargets(simulation, groupId, group, resetRecovery: rebuildLayout);
        }
        else
        {
            AssignGroupTargets(simulation, groupId, group, resetRecovery: rebuildLayout);
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

    public void RotateCommandActors(World world, MassNavigationAgentState agentState, ReadOnlySpan<Entity> commandActors, float deltaRadians)
    {
        if (!(MathF.Abs(deltaRadians) > _formationLayout.RotationEpsilonRadians))
        {
            return;
        }

        Span<int> memberIndices = EnsureCommandActorMemberScratch(commandActors.Length);
        int memberCount = 0;
        for (int i = 0; i < commandActors.Length; i++)
        {
            if (agentState.TryGetControllableIndex(commandActors[i], out int unitIndex))
            {
                memberIndices[memberCount++] = unitIndex;
            }
        }

        if (memberCount <= 0)
        {
            return;
        }

        EnsureMembershipCapacityForMembers(memberIndices[..memberCount]);
        _visitedGroupIds.Clear();
        for (int i = 0; i < commandActors.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(commandActors[i], out int unitIndex))
            {
                continue;
            }

            int groupId = GetGroupId(unitIndex);
            if (groupId < 0 || !_visitedGroupIds.Add(groupId))
            {
                if (groupId < 0)
                {
                    RotateUngroupedCommandActor(world, commandActors[i], deltaRadians);
                }

                continue;
            }

            NavGroupState? group = _groups[groupId];
            if (group == null)
            {
                continue;
            }

            group.RotationRadians = NormalizeAngleRadians(group.RotationRadians + deltaRadians);
            _formationLayout.RecomputeOffsets(
                group.OffsetX,
                group.OffsetY,
                group.BaseOffsetX,
                group.BaseOffsetY,
                group.MemberCount,
                group.RotationRadians);
            WriteMemberFacing(world, group, group.RotationRadians);
        }

        RefreshCommandActorRotation(world, agentState, commandActors);
    }

    public void RefreshCommandActorRotation(World world, MassNavigationAgentState agentState, ReadOnlySpan<Entity> commandActors)
    {
        for (int i = 0; i < commandActors.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(commandActors[i], out int unitIndex))
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
                CommandActorRotationRadians = group.RotationRadians;
                return;
            }
        }

        for (int i = 0; i < commandActors.Length; i++)
        {
            Entity entity = commandActors[i];
            if (!world.IsAlive(entity))
            {
                continue;
            }

            if (!world.Has<FacingDirection>(entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigation command actor {entity.Id} requires {nameof(FacingDirection)} for explicit formation rotation.");
            }

            CommandActorRotationRadians = world.Get<FacingDirection>(entity).AngleRad;
            return;
        }
    }

    public void UpdateTargets(
        MassNavigationFlowSolverState simulation,
        int frameIndex)
    {
        if ((frameIndex & 1) == 0)
        {
            RefreshActiveGroupRotation();
        }

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
        RefreshActiveGroupRotation();
    }

    private void RefreshActiveGroupRotation()
    {
        for (int groupId = 0; groupId < _groups.Count; groupId++)
        {
            NavGroupState? group = _groups[groupId];
            if (group == null || group.MemberCount <= 0)
            {
                continue;
            }

            CommandActorRotationRadians = group.RotationRadians;
            return;
        }
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

    private Span<int> EnsureCommandActorMemberScratch(int count)
    {
        if (_commandActorMemberScratch.Length < count)
        {
            throw new InvalidOperationException(
                $"MassNavigation command actor scratch required {count} entries, exceeding configured scenarioRuntime.runtimeCapacity.commandActorScratchCapacity {_commandActorMemberScratch.Length}.");
        }

        return _commandActorMemberScratch.AsSpan(0, count);
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

    private float ResolvePreviousRotation(World world, MassNavigationAgentState agentState, ReadOnlySpan<Entity> commandActors)
    {
        for (int i = 0; i < commandActors.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(commandActors[i], out int unitIndex))
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

        for (int i = 0; i < commandActors.Length; i++)
        {
            Entity entity = commandActors[i];
            if (!world.IsAlive(entity))
            {
                continue;
            }

            if (!world.Has<FacingDirection>(entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigation command actor {entity.Id} requires {nameof(FacingDirection)} for explicit formation rotation.");
            }

            return world.Get<FacingDirection>(entity).AngleRad;
        }

        return CommandActorRotationRadians;
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

        throw new InvalidOperationException(
            $"MassNavigation navigation group allocation exceeded configured scenarioRuntime.runtimeCapacity.navigationGroupCapacity {_groups.Count}.");
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
            group.MemberOrderPathStartWorldX[target] = group.MemberOrderPathStartWorldX[source];
            group.MemberOrderPathStartWorldY[target] = group.MemberOrderPathStartWorldY[source];
            group.MemberOrderPathAnchorWorldX[target] = group.MemberOrderPathAnchorWorldX[source];
            group.MemberOrderPathAnchorWorldY[target] = group.MemberOrderPathAnchorWorldY[source];
            group.MemberOrderPathAnchorRevision[target] = group.MemberOrderPathAnchorRevision[source];
            group.MemberOrderPathAnchorInitialized[target] = group.MemberOrderPathAnchorInitialized[source];
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

        RebuildGroupLayout(simulation, group);
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

    private void AssignLooseTargets(
        MassNavigationFlowSolverState simulation,
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
            Vector2 singleTarget = simulation.ResolveUnitNavigableTarget(
                memberIndices[0],
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
        ScaleLooseLayoutForMemberBodyRadius(simulation, memberIndices, count, MassNavigationFormationMode.Square);
        Vector2 destinationLocalCm = simulation.WorldToLocalCm(destinationWorldCm);
        float centerClearanceCm = ResolveMemberMaxClearanceCm(
            simulation,
            memberIndices,
            count,
            simulation.Semantics.TargetProjection.GroupCenterClearanceCm);
        Vector2 resolvedCenter = simulation.ResolveNavigableTarget(
            destinationLocalCm.X,
            destinationLocalCm.Y,
            0f,
            0f,
            centerClearanceCm);
        for (int i = 0; i < count; i++)
        {
            int unitIndex = memberIndices[i];
            Vector2 resolvedTarget = simulation.ResolveUnitNavigableTarget(
                unitIndex,
                resolvedCenter.X + _looseOffsetX[i],
                resolvedCenter.Y + _looseOffsetY[i],
                _looseOffsetX[i],
                _looseOffsetY[i],
                simulation.Semantics.TargetProjection.LooseTargetClearanceCm);
            simulation.SetUnitTarget(unitIndex, resolvedTarget.X, resolvedTarget.Y, resetRecovery: true);
        }
    }

    private void EnsureLooseOffsetCapacity(int required)
    {
        if (_looseOffsetX.Length >= required)
        {
            return;
        }

        throw new InvalidOperationException(
            $"MassNavigation loose group layout required {required} members, exceeding configured scenarioRuntime.runtimeCapacity.groupMemberCapacity {_looseOffsetX.Length}.");
    }

    private void ScaleLooseLayoutForMemberBodyRadius(
        MassNavigationFlowSolverState simulation,
        ReadOnlySpan<int> memberIndices,
        int count,
        MassNavigationFormationMode layoutMode)
    {
        float spacingScale = ResolveBodyRadiusSpacingScale(simulation, memberIndices, count, layoutMode);
        if (spacingScale == 1f)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            _looseBaseOffsetX[i] *= spacingScale;
            _looseBaseOffsetY[i] *= spacingScale;
            _looseOffsetX[i] *= spacingScale;
            _looseOffsetY[i] *= spacingScale;
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

    private NavGroupState CreateGroup(
        int groupId,
        MassNavigationFlowSolverState simulation,
        MassNavigationAgentState agentState,
        ReadOnlySpan<int> members,
        int teamId,
        MassNavigationFormationMode formationMode,
        float rotationRadians)
    {
        if ((uint)groupId >= (uint)_groupPool.Length)
        {
            throw new InvalidOperationException(
                $"MassNavigation group id {groupId} exceeds configured scenarioRuntime.runtimeCapacity.navigationGroupCapacity {_groupPool.Length}.");
        }

        NavGroupState group = _groupPool[groupId];
        group.Reset(teamId);
        CopyMembersAndRebuildLayout(simulation, agentState, group, members, formationMode, rotationRadians);
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
        int teamId,
        MassNavigationFormationMode formationMode,
        float rotationRadians)
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
        CopyMembersAndRebuildLayout(simulation, agentState, group, nextMembers, formationMode, rotationRadians);
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
            Vector2 orderStartWorld = simulation.LocalToWorldCm(new Vector2(
                simulation.GetPositionX(unitIndex),
                simulation.GetPositionY(unitIndex)));
            group.MemberOrderTargetWorldX[i] = resolvedTargetWorld.X;
            group.MemberOrderTargetWorldY[i] = resolvedTargetWorld.Y;
            group.MemberOrderPathStartWorldX[i] = orderStartWorld.X;
            group.MemberOrderPathStartWorldY[i] = orderStartWorld.Y;
            group.MemberOrderPathAnchorInitialized[i] = 0;
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
            Vector2 orderStartWorld = simulation.LocalToWorldCm(new Vector2(
                simulation.GetPositionX(unitIndex),
                simulation.GetPositionY(unitIndex)));
            group.MemberOrderTargetWorldX[i] = resolvedTargetWorld.X;
            group.MemberOrderTargetWorldY[i] = resolvedTargetWorld.Y;
            group.MemberOrderPathStartWorldX[i] = orderStartWorld.X;
            group.MemberOrderPathStartWorldY[i] = orderStartWorld.Y;
            group.MemberOrderPathAnchorInitialized[i] = 0;
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

    private void CopyMembersAndRebuildLayout(
        MassNavigationFlowSolverState simulation,
        MassNavigationAgentState agentState,
        NavGroupState group,
        ReadOnlySpan<int> members,
        MassNavigationFormationMode formationMode,
        float rotationRadians)
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
        group.FormationMode = formationMode;
        group.RotationRadians = rotationRadians;
        RebuildGroupLayout(simulation, group);
    }

    private void RebuildGroupLayout(MassNavigationFlowSolverState simulation, NavGroupState group)
    {
        if (group.MemberCount <= 0)
        {
            return;
        }

        if (group.FormationMode == MassNavigationFormationMode.None)
        {
            BuildCurrentRelativeOffsets(simulation, group);
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
        ScaleGroupLayoutForMemberBodyRadius(simulation, group);
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
            group.BaseOffsetX[i] = offsetX;
            group.BaseOffsetY[i] = offsetY;
            group.OffsetX[i] = offsetX;
            group.OffsetY[i] = offsetY;
        }
    }

    private static void ScaleGroupLayoutForMemberBodyRadius(MassNavigationFlowSolverState simulation, NavGroupState group)
    {
        float spacingScale = ResolveBodyRadiusSpacingScale(
            simulation,
            group.MemberIndices.AsSpan(0, group.MemberCount),
            group.MemberCount,
            group.FormationMode);
        if (spacingScale == 1f)
        {
            return;
        }

        for (int i = 0; i < group.MemberCount; i++)
        {
            group.BaseOffsetX[i] *= spacingScale;
            group.BaseOffsetY[i] *= spacingScale;
            group.OffsetX[i] *= spacingScale;
            group.OffsetY[i] *= spacingScale;
        }
    }

    private static float ResolveBodyRadiusSpacingScale(
        MassNavigationFlowSolverState simulation,
        ReadOnlySpan<int> memberIndices,
        int count,
        MassNavigationFormationMode layoutMode)
    {
        if (count <= 0 || layoutMode == MassNavigationFormationMode.None)
        {
            return 1f;
        }

        float maxBodyRadiusCm = 0f;
        for (int i = 0; i < count; i++)
        {
            maxBodyRadiusCm = MathF.Max(maxBodyRadiusCm, simulation.GetBodyRadiusCm(memberIndices[i]));
        }

        float configuredSpacingCm = ResolveConfiguredFormationSpacing(simulation, layoutMode);
        if (!(configuredSpacingCm > 0f))
        {
            throw new InvalidOperationException($"MassNavigation formation layout {layoutMode} requires a positive configured spacing.");
        }

        float requiredSpacingCm = maxBodyRadiusCm * 2f;
        return requiredSpacingCm <= configuredSpacingCm ? 1f : requiredSpacingCm / configuredSpacingCm;
    }

    private static float ResolveConfiguredFormationSpacing(
        MassNavigationFlowSolverState simulation,
        MassNavigationFormationMode layoutMode)
    {
        return layoutMode switch
        {
            MassNavigationFormationMode.Line => simulation.Semantics.Group.FormationLineSpacingCm,
            MassNavigationFormationMode.Circle => simulation.Semantics.Group.FormationCircleSpacingCm,
            MassNavigationFormationMode.Wedge => simulation.Semantics.Group.FormationWedgeSpacingCm,
            MassNavigationFormationMode.Square => simulation.Semantics.Group.FormationSquareSpacingCm,
            _ => throw new InvalidOperationException($"MassNavigation formation layout {layoutMode} is not supported for spacing resolution."),
        };
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

    private static bool RestoreCompatibleOrderPathState(
        NavGroupState group,
        AuthoredRebuildGroupSnapshot captured,
        int[] memberSnapshotIndices,
        int restoredMemberCount)
    {
        bool allOrderTargetsPreserved = true;
        for (int i = 0; i < restoredMemberCount; i++)
        {
            AuthoredRebuildMemberSnapshot member = captured.Members[memberSnapshotIndices[i]];
            if (MathF.Abs(group.MemberOrderTargetWorldX[i] - member.OrderTargetWorldX) > OrderPathRestoreTargetToleranceCm ||
                MathF.Abs(group.MemberOrderTargetWorldY[i] - member.OrderTargetWorldY) > OrderPathRestoreTargetToleranceCm)
            {
                allOrderTargetsPreserved = false;
                continue;
            }

            group.MemberOrderPathStartWorldX[i] = member.OrderPathStartWorldX;
            group.MemberOrderPathStartWorldY[i] = member.OrderPathStartWorldY;
            group.MemberOrderPathAnchorWorldX[i] = member.OrderPathAnchorWorldX;
            group.MemberOrderPathAnchorWorldY[i] = member.OrderPathAnchorWorldY;
            group.MemberOrderPathAnchorRevision[i] = member.OrderPathAnchorRevision;
            group.MemberOrderPathAnchorInitialized[i] = member.OrderPathAnchorInitialized;
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

    private static void ResolveOrderPathLookaheadAnchor(
        float startLocalXCm,
        float startLocalYCm,
        float currentLocalXCm,
        float currentLocalYCm,
        float finalTargetLocalXCm,
        float finalTargetLocalYCm,
        float lookaheadCm,
        out float anchorLocalXCm,
        out float anchorLocalYCm)
    {
        float pathX = finalTargetLocalXCm - startLocalXCm;
        float pathY = finalTargetLocalYCm - startLocalYCm;
        float pathLengthSq = (pathX * pathX) + (pathY * pathY);
        if (pathLengthSq <= lookaheadCm * lookaheadCm)
        {
            anchorLocalXCm = finalTargetLocalXCm;
            anchorLocalYCm = finalTargetLocalYCm;
            return;
        }

        float currentAlongPath =
            ((currentLocalXCm - startLocalXCm) * pathX) +
            ((currentLocalYCm - startLocalYCm) * pathY);
        float anchorAlongPath = Math.Clamp(currentAlongPath + (lookaheadCm * MathF.Sqrt(pathLengthSq)), 0f, pathLengthSq);
        float scale = anchorAlongPath / pathLengthSq;
        anchorLocalXCm = startLocalXCm + (pathX * scale);
        anchorLocalYCm = startLocalYCm + (pathY * scale);
    }

    private static void ResolveOrderPathPassiveOffset(
        float startLocalXCm,
        float startLocalYCm,
        float currentLocalXCm,
        float currentLocalYCm,
        float finalTargetLocalXCm,
        float finalTargetLocalYCm,
        out float passiveOffsetLocalXCm,
        out float passiveOffsetLocalYCm)
    {
        float pathX = finalTargetLocalXCm - startLocalXCm;
        float pathY = finalTargetLocalYCm - startLocalYCm;
        float pathLengthSq = (pathX * pathX) + (pathY * pathY);
        if (pathLengthSq <= float.Epsilon)
        {
            passiveOffsetLocalXCm = currentLocalXCm - startLocalXCm;
            passiveOffsetLocalYCm = currentLocalYCm - startLocalYCm;
            return;
        }

        float currentAlongPath =
            ((currentLocalXCm - startLocalXCm) * pathX) +
            ((currentLocalYCm - startLocalYCm) * pathY);
        float projectionScale = Math.Clamp(currentAlongPath / pathLengthSq, 0f, 1f);
        float projectedLocalX = startLocalXCm + (pathX * projectionScale);
        float projectedLocalY = startLocalYCm + (pathY * projectionScale);
        passiveOffsetLocalXCm = currentLocalXCm - projectedLocalX;
        passiveOffsetLocalYCm = currentLocalYCm - projectedLocalY;
    }

    private static void WriteMemberFacing(World world, NavGroupState group, float rotationRadians)
    {
        for (int i = 0; i < group.MemberCount; i++)
        {
            Entity entity = group.MemberEntities[i];
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigation group member entity {entity.Id} is not alive while writing explicit formation facing.");
            }

            if (!world.Has<FacingDirection>(entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigation group member entity {entity.Id} requires {nameof(FacingDirection)} for explicit formation rotation.");
            }

            ref FacingDirection facing = ref world.Get<FacingDirection>(entity);
            facing.AngleRad = rotationRadians;
        }
    }

    private static void RotateUngroupedCommandActor(World world, Entity entity, float deltaRadians)
    {
        if (!world.IsAlive(entity))
        {
            return;
        }

        if (!world.Has<FacingDirection>(entity))
        {
            throw new InvalidOperationException(
                $"MassNavigation command actor {entity.Id} requires {nameof(FacingDirection)} for explicit formation rotation.");
        }

        ref FacingDirection facing = ref world.Get<FacingDirection>(entity);
        facing.AngleRad = NormalizeAngleRadians(facing.AngleRad + deltaRadians);
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

    private static float NormalizeAngleRadians(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        while (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }

    internal sealed class AuthoredRebuildSnapshot
    {
        internal AuthoredRebuildSnapshot(AuthoredRebuildGroupSnapshot[] groups, float commandActorRotationRadians)
        {
            Groups = groups;
            CommandActorRotationRadians = commandActorRotationRadians;
        }

        internal AuthoredRebuildGroupSnapshot[] Groups { get; }
        internal float CommandActorRotationRadians { get; }
    }

    internal sealed class AuthoredRebuildGroupSnapshot
    {
        public AuthoredRebuildGroupSnapshot(
            int groupId,
            int commandToken,
            MassNavigationFormationMode formationMode,
            float requestedDestinationWorldX,
            float requestedDestinationWorldY,
            float destinationWorldX,
            float destinationWorldY,
            float centerX,
            float centerY,
            float rotationRadians,
            bool arrived,
            AuthoredRebuildMemberSnapshot[] members)
        {
            GroupId = groupId;
            CommandToken = commandToken;
            FormationMode = formationMode;
            RequestedDestinationWorldX = requestedDestinationWorldX;
            RequestedDestinationWorldY = requestedDestinationWorldY;
            DestinationWorldX = destinationWorldX;
            DestinationWorldY = destinationWorldY;
            CenterX = centerX;
            CenterY = centerY;
            RotationRadians = rotationRadians;
            Arrived = arrived;
            Members = members;
        }

        public int GroupId { get; }
        public int CommandToken { get; }
        public MassNavigationFormationMode FormationMode { get; }
        public float RequestedDestinationWorldX { get; }
        public float RequestedDestinationWorldY { get; }
        public float DestinationWorldX { get; }
        public float DestinationWorldY { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public float RotationRadians { get; }
        public bool Arrived { get; }
        public AuthoredRebuildMemberSnapshot[] Members { get; }
    }

    internal readonly struct AuthoredRebuildMemberSnapshot
    {
        public AuthoredRebuildMemberSnapshot(
            Entity entity,
            float orderTargetWorldX,
            float orderTargetWorldY,
            float orderPathStartWorldX,
            float orderPathStartWorldY,
            float orderPathAnchorWorldX,
            float orderPathAnchorWorldY,
            int orderPathAnchorRevision,
            byte orderPathAnchorInitialized)
        {
            Entity = entity;
            OrderTargetWorldX = orderTargetWorldX;
            OrderTargetWorldY = orderTargetWorldY;
            OrderPathStartWorldX = orderPathStartWorldX;
            OrderPathStartWorldY = orderPathStartWorldY;
            OrderPathAnchorWorldX = orderPathAnchorWorldX;
            OrderPathAnchorWorldY = orderPathAnchorWorldY;
            OrderPathAnchorRevision = orderPathAnchorRevision;
            OrderPathAnchorInitialized = orderPathAnchorInitialized;
        }

        public Entity Entity { get; }
        public float OrderTargetWorldX { get; }
        public float OrderTargetWorldY { get; }
        public float OrderPathStartWorldX { get; }
        public float OrderPathStartWorldY { get; }
        public float OrderPathAnchorWorldX { get; }
        public float OrderPathAnchorWorldY { get; }
        public int OrderPathAnchorRevision { get; }
        public byte OrderPathAnchorInitialized { get; }
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
            MemberOrderPathStartWorldX = new float[capacity];
            MemberOrderPathStartWorldY = new float[capacity];
            MemberOrderPathAnchorWorldX = new float[capacity];
            MemberOrderPathAnchorWorldY = new float[capacity];
            MemberOrderPathAnchorRevision = new int[capacity];
            MemberOrderPathAnchorInitialized = new byte[capacity];
            BaseOffsetX = new float[capacity];
            BaseOffsetY = new float[capacity];
            OffsetX = new float[capacity];
            OffsetY = new float[capacity];
            TeamId = teamId;
        }

        public int MemberCount { get; set; }
        public int TeamId { get; set; }
        public int CommandToken { get; set; }
        public MassNavigationFormationMode FormationMode { get; set; }
        public int[] MemberIndices { get; private set; }
        public Entity[] MemberEntities { get; private set; }
        public float[] MemberOrderTargetWorldX { get; private set; }
        public float[] MemberOrderTargetWorldY { get; private set; }
        public float[] MemberOrderPathStartWorldX { get; private set; }
        public float[] MemberOrderPathStartWorldY { get; private set; }
        public float[] MemberOrderPathAnchorWorldX { get; private set; }
        public float[] MemberOrderPathAnchorWorldY { get; private set; }
        public int[] MemberOrderPathAnchorRevision { get; private set; }
        public byte[] MemberOrderPathAnchorInitialized { get; private set; }
        public float[] BaseOffsetX { get; private set; }
        public float[] BaseOffsetY { get; private set; }
        public float[] OffsetX { get; private set; }
        public float[] OffsetY { get; private set; }
        public float RequestedDestinationWorldX { get; set; }
        public float RequestedDestinationWorldY { get; set; }
        public float DestinationWorldX { get; set; }
        public float DestinationWorldY { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float RotationRadians { get; set; }
        public bool Arrived { get; set; }

        public void Reset(int teamId)
        {
            MemberCount = 0;
            TeamId = teamId;
            CommandToken = 0;
            FormationMode = MassNavigationFormationMode.None;
            RequestedDestinationWorldX = 0f;
            RequestedDestinationWorldY = 0f;
            DestinationWorldX = 0f;
            DestinationWorldY = 0f;
            CenterX = 0f;
            CenterY = 0f;
            RotationRadians = 0f;
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
