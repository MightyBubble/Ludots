using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace MassNavPlaygroundMod.Runtime;

public sealed class MassNavFormationRuntime
{
    private const float SquareSpacingCm = 80f;
    private const float PullThresholdCm = 50f;
    private const float PullCapCm = 2000f;
    private const float ArrivedDistanceCm = 150f;

    private readonly List<FormationGroup?> _groups = new();
    private readonly HashSet<int> _visitedGroupIds = new();
    private int[] _formationIdsByControllableIndex = Array.Empty<int>();
    private bool _dirty = true;

    public int ActiveGroupCount { get; private set; }
    public float SelectedRotationRadians { get; private set; }

    public void Reset()
    {
        _groups.Clear();
        if (_formationIdsByControllableIndex.Length > 0)
        {
            Array.Fill(_formationIdsByControllableIndex, -1);
        }

        _dirty = true;
        ActiveGroupCount = 0;
        SelectedRotationRadians = 0f;
    }

    public int AssignSquareFormation(World world, MassNavAgentState agentState, ReadOnlySpan<Entity> selected, Vector2 destinationCm, int goalRadiusCm)
    {
        if (selected.Length <= 0)
        {
            return 0;
        }

        EnsureMembershipCapacity(agentState.ControllableCount);
        float previousRotation = ResolvePreviousRotation(agentState, selected);

        int assignedCount = 0;
        int[] controllableIndices = new int[selected.Length];
        for (int i = 0; i < selected.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(selected[i], out int controllableIndex))
            {
                continue;
            }

            RemoveFromExistingFormation(controllableIndex);
            controllableIndices[assignedCount++] = controllableIndex;
        }

        if (assignedCount <= 0)
        {
            return 0;
        }

        if (assignedCount == 1)
        {
            WritePointGoal(world, agentState.ControllableAgents[controllableIndices[0]], destinationCm, goalRadiusCm);
            _dirty = true;
            return 1;
        }

        int groupId = AllocateGroupId();
        int[] memberIndices = new int[assignedCount];
        Vector2[] baseOffsets = new Vector2[assignedCount];
        Vector2[] offsets = new Vector2[assignedCount];
        Array.Copy(controllableIndices, memberIndices, assignedCount);
        BuildSquareOffsets(baseOffsets, offsets, assignedCount, previousRotation);

        _groups[groupId] = new FormationGroup(
            memberIndices,
            baseOffsets,
            offsets,
            destinationCm,
            previousRotation);
        ActiveGroupCount++;

        for (int i = 0; i < memberIndices.Length; i++)
        {
            _formationIdsByControllableIndex[memberIndices[i]] = groupId;
            WritePointGoal(world, agentState.ControllableAgents[memberIndices[i]], destinationCm, goalRadiusCm);
        }

        _dirty = true;
        return assignedCount;
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
            if (!agentState.TryGetControllableIndex(selected[i], out int controllableIndex))
            {
                continue;
            }

            int groupId = GetFormationId(controllableIndex);
            if (groupId < 0 || !_visitedGroupIds.Add(groupId))
            {
                continue;
            }

            FormationGroup? group = _groups[groupId];
            if (group == null)
            {
                continue;
            }

            group.RotationRadians += deltaRadians;
            RecomputeOffsets(group);
            _dirty = true;
        }

        RefreshSelectedRotation(agentState, selected);
    }

    public void RefreshSelectedRotation(MassNavAgentState agentState, ReadOnlySpan<Entity> selected)
    {
        for (int i = 0; i < selected.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(selected[i], out int controllableIndex))
            {
                continue;
            }

            int groupId = GetFormationId(controllableIndex);
            if (groupId < 0)
            {
                continue;
            }

            FormationGroup? group = _groups[groupId];
            if (group != null)
            {
                SelectedRotationRadians = group.RotationRadians;
                return;
            }
        }

        SelectedRotationRadians = 0f;
    }

    public void UpdateGoals(World world, MassNavAgentState agentState, ReadOnlySpan<Entity> selected, int frameIndex, int goalRadiusCm)
    {
        if (!_dirty && (frameIndex & 1) == 0)
        {
            RefreshSelectedRotation(agentState, selected);
            return;
        }

        for (int groupId = 0; groupId < _groups.Count; groupId++)
        {
            FormationGroup? group = _groups[groupId];
            if (group == null)
            {
                continue;
            }

            float centerX = 0f;
            float centerY = 0f;
            int liveCount = 0;
            for (int member = 0; member < group.MemberIndices.Length; member++)
            {
                Entity entity = agentState.ControllableAgents[group.MemberIndices[member]];
                if (!world.IsAlive(entity) || !world.TryGet(entity, out Position2D position))
                {
                    continue;
                }

                Vector2 current = position.Value.ToVector2();
                centerX += current.X;
                centerY += current.Y;
                liveCount++;
            }

            if (liveCount <= 0)
            {
                DissolveGroup(groupId, group);
                continue;
            }

            centerX /= liveCount;
            centerY /= liveCount;
            Vector2 center = new(centerX, centerY);
            Vector2 delta = group.DestinationCm - center;
            float distance = delta.Length();
            Vector2 pull = Vector2.Zero;
            if (distance > PullThresholdCm)
            {
                pull = Vector2.Normalize(delta) * MathF.Min(distance, PullCapCm);
            }

            for (int member = 0; member < group.MemberIndices.Length; member++)
            {
                Entity entity = agentState.ControllableAgents[group.MemberIndices[member]];
                if (!world.IsAlive(entity))
                {
                    continue;
                }

                Vector2 target = center + group.OffsetsCm[member] + pull;
                WritePointGoal(world, entity, target, goalRadiusCm);
            }

            group.CenterCm = center;
            group.Arrived = distance < ArrivedDistanceCm;
        }

        _dirty = false;
        RefreshSelectedRotation(agentState, selected);
    }

    private void EnsureMembershipCapacity(int controllableCount)
    {
        if (controllableCount <= _formationIdsByControllableIndex.Length)
        {
            return;
        }

        int previousLength = _formationIdsByControllableIndex.Length;
        Array.Resize(ref _formationIdsByControllableIndex, controllableCount);
        for (int i = previousLength; i < _formationIdsByControllableIndex.Length; i++)
        {
            _formationIdsByControllableIndex[i] = -1;
        }
    }

    private float ResolvePreviousRotation(MassNavAgentState agentState, ReadOnlySpan<Entity> selected)
    {
        for (int i = 0; i < selected.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(selected[i], out int controllableIndex))
            {
                continue;
            }

            int groupId = GetFormationId(controllableIndex);
            if (groupId < 0)
            {
                continue;
            }

            FormationGroup? group = _groups[groupId];
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

    private void RemoveFromExistingFormation(int controllableIndex)
    {
        int groupId = GetFormationId(controllableIndex);
        if (groupId < 0)
        {
            return;
        }

        FormationGroup? group = _groups[groupId];
        if (group == null)
        {
            _formationIdsByControllableIndex[controllableIndex] = -1;
            return;
        }

        _formationIdsByControllableIndex[controllableIndex] = -1;
        int memberPosition = Array.IndexOf(group.MemberIndices, controllableIndex);
        if (memberPosition < 0)
        {
            return;
        }

        if (group.MemberIndices.Length <= 2)
        {
            DissolveGroup(groupId, group);
            return;
        }

        int nextLength = group.MemberIndices.Length - 1;
        int[] nextMembers = new int[nextLength];
        Vector2[] nextBaseOffsets = new Vector2[nextLength];
        Vector2[] nextOffsets = new Vector2[nextLength];
        for (int source = 0, target = 0; source < group.MemberIndices.Length; source++)
        {
            if (source == memberPosition)
            {
                continue;
            }

            nextMembers[target] = group.MemberIndices[source];
            nextBaseOffsets[target] = group.BaseOffsetsCm[source];
            nextOffsets[target] = group.OffsetsCm[source];
            target++;
        }

        group.MemberIndices = nextMembers;
        group.BaseOffsetsCm = nextBaseOffsets;
        group.OffsetsCm = nextOffsets;
    }

    private void DissolveGroup(int groupId, FormationGroup group)
    {
        for (int i = 0; i < group.MemberIndices.Length; i++)
        {
            int controllableIndex = group.MemberIndices[i];
            if ((uint)controllableIndex < (uint)_formationIdsByControllableIndex.Length)
            {
                _formationIdsByControllableIndex[controllableIndex] = -1;
            }
        }

        _groups[groupId] = null;
        ActiveGroupCount = Math.Max(0, ActiveGroupCount - 1);
    }

    private int GetFormationId(int controllableIndex)
    {
        if ((uint)controllableIndex >= (uint)_formationIdsByControllableIndex.Length)
        {
            return -1;
        }

        return _formationIdsByControllableIndex[controllableIndex];
    }

    private static void BuildSquareOffsets(Vector2[] baseOffsets, Vector2[] offsets, int count, float rotationRadians)
    {
        int cols = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling(count / (double)cols);
        float rowCenter = (rows - 1) * 0.5f;
        float colCenter = (cols - 1) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            int row = i / cols;
            int col = i % cols;
            baseOffsets[i] = new Vector2(
                (col - colCenter) * SquareSpacingCm,
                (row - rowCenter) * SquareSpacingCm);
            offsets[i] = baseOffsets[i];
        }

        if (MathF.Abs(rotationRadians) > 1e-5f)
        {
            ApplyRotation(offsets, baseOffsets, rotationRadians);
        }
    }

    private static void RecomputeOffsets(FormationGroup group)
    {
        ApplyRotation(group.OffsetsCm, group.BaseOffsetsCm, group.RotationRadians);
    }

    private static void ApplyRotation(Vector2[] offsets, Vector2[] baseOffsets, float rotationRadians)
    {
        float cos = MathF.Cos(rotationRadians);
        float sin = MathF.Sin(rotationRadians);
        for (int i = 0; i < baseOffsets.Length; i++)
        {
            Vector2 source = baseOffsets[i];
            offsets[i] = new Vector2(
                source.X * cos - source.Y * sin,
                source.X * sin + source.Y * cos);
        }
    }

    private static void WritePointGoal(World world, Entity entity, Vector2 targetCm, int goalRadiusCm)
    {
        if (!world.IsAlive(entity) || !world.Has<NavGoal2D>(entity))
        {
            return;
        }

        ref NavGoal2D goal = ref world.Get<NavGoal2D>(entity);
        goal.Kind = NavGoalKind2D.Point;
        goal.TargetCm = Fix64Vec2.FromInt(
            (int)MathF.Round(targetCm.X),
            (int)MathF.Round(targetCm.Y));
        goal.RadiusCm = Fix64.FromInt(goalRadiusCm);
    }

    private sealed class FormationGroup
    {
        public FormationGroup(int[] memberIndices, Vector2[] baseOffsetsCm, Vector2[] offsetsCm, Vector2 destinationCm, float rotationRadians)
        {
            MemberIndices = memberIndices;
            BaseOffsetsCm = baseOffsetsCm;
            OffsetsCm = offsetsCm;
            DestinationCm = destinationCm;
            RotationRadians = rotationRadians;
        }

        public int[] MemberIndices { get; set; }
        public Vector2[] BaseOffsetsCm { get; set; }
        public Vector2[] OffsetsCm { get; set; }
        public Vector2 DestinationCm { get; set; }
        public Vector2 CenterCm { get; set; }
        public float RotationRadians { get; set; }
        public bool Arrived { get; set; }
    }
}
