using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavFormationRuntime
{
    private readonly List<FormationGroup?> _groups = new();
    private readonly HashSet<int> _visitedGroupIds = new();
    private int[] _formationIdsByControllableIndex = Array.Empty<int>();

    public int ActiveGroupCount { get; private set; }
    public float SelectedRotationRadians { get; private set; }

    public void Reset()
    {
        _groups.Clear();
        if (_formationIdsByControllableIndex.Length > 0)
        {
            Array.Fill(_formationIdsByControllableIndex, -1);
        }

        ActiveGroupCount = 0;
        SelectedRotationRadians = 0f;
    }

    public bool IsInFormation(int unitIndex)
    {
        return GetFormationId(unitIndex) >= 0;
    }

    public int AssignFormation(
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

            RemoveFromExistingFormation(simulation, unitIndex);
            memberIndices[assignedCount++] = unitIndex;
            simulation.ClearUnitTarget(unitIndex);
        }

        if (assignedCount <= 0)
        {
            return 0;
        }

        if (formationMode == MassNavFormationMode.None || assignedCount == 1)
        {
            for (int i = 0; i < assignedCount; i++)
            {
                simulation.SetUnitTarget(memberIndices[i], destinationCm.X, destinationCm.Y);
            }

            ActiveGroupCount = CountActiveGroups();
            RefreshSelectedRotation(agentState, selected);
            return assignedCount;
        }

        int groupId = AllocateGroupId();
        int[] exactMembers = new int[assignedCount];
        Array.Copy(memberIndices, exactMembers, assignedCount);

        float[] baseOffsetX = new float[assignedCount];
        float[] baseOffsetY = new float[assignedCount];
        float[] offsetX = new float[assignedCount];
        float[] offsetY = new float[assignedCount];
        BuildOffsets(baseOffsetX, baseOffsetY, offsetX, offsetY, assignedCount, formationMode, previousRotation);

        var group = new FormationGroup(exactMembers, baseOffsetX, baseOffsetY, offsetX, offsetY)
        {
            DestinationX = destinationCm.X,
            DestinationY = destinationCm.Y,
            RotationRadians = previousRotation,
        };
        _groups[groupId] = group;

        for (int i = 0; i < exactMembers.Length; i++)
        {
            _formationIdsByControllableIndex[exactMembers[i]] = groupId;
            simulation.SetUnitTarget(exactMembers[i], destinationCm.X, destinationCm.Y);
        }

        ActiveGroupCount = CountActiveGroups();
        RefreshSelectedRotation(agentState, selected);
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
            if (!agentState.TryGetControllableIndex(selected[i], out int unitIndex))
            {
                continue;
            }

            int groupId = GetFormationId(unitIndex);
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

            int groupId = GetFormationId(unitIndex);
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
            FormationGroup? group = _groups[groupId];
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
            float pullStrength = distance > 50f ? MathF.Min(distance, 2_000f) : 0f;
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
                float targetX = centerX + group.OffsetX[i] + pullX;
                float targetY = centerY + group.OffsetY[i] + pullY;
                simulation.SetUnitTarget(unitIndex, targetX, targetY);
            }

            group.CenterX = centerX;
            group.CenterY = centerY;
            group.Arrived = distance < 150f;
        }

        ActiveGroupCount = CountActiveGroups();
        RefreshSelectedRotation(agentState, selected);
    }

    private void EnsureMembershipCapacity(int count)
    {
        if (count <= _formationIdsByControllableIndex.Length)
        {
            return;
        }

        int previousLength = _formationIdsByControllableIndex.Length;
        Array.Resize(ref _formationIdsByControllableIndex, count);
        for (int i = previousLength; i < _formationIdsByControllableIndex.Length; i++)
        {
            _formationIdsByControllableIndex[i] = -1;
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

            int groupId = GetFormationId(unitIndex);
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

    private void RemoveFromExistingFormation(MassNavWebParitySimState simulation, int unitIndex)
    {
        int groupId = GetFormationId(unitIndex);
        if (groupId < 0)
        {
            return;
        }

        FormationGroup? group = _groups[groupId];
        _formationIdsByControllableIndex[unitIndex] = -1;
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

    private void DissolveGroup(int groupId, FormationGroup group)
    {
        for (int i = 0; i < group.MemberIndices.Length; i++)
        {
            int unitIndex = group.MemberIndices[i];
            if ((uint)unitIndex < (uint)_formationIdsByControllableIndex.Length)
            {
                _formationIdsByControllableIndex[unitIndex] = -1;
            }
        }

        _groups[groupId] = null;
    }

    private int GetFormationId(int unitIndex)
    {
        if ((uint)unitIndex >= (uint)_formationIdsByControllableIndex.Length)
        {
            return -1;
        }

        return _formationIdsByControllableIndex[unitIndex];
    }

    private static void BuildOffsets(
        float[] baseOffsetX,
        float[] baseOffsetY,
        float[] offsetX,
        float[] offsetY,
        int count,
        MassNavFormationMode mode,
        float rotationRadians)
    {
        const float lineSpacingCm = 180f;
        const float squareSpacingCm = 80f;
        const float wedgeSpacingCm = 180f;

        switch (mode)
        {
            case MassNavFormationMode.Line:
                for (int i = 0; i < count; i++)
                {
                    baseOffsetX[i] = (i - ((count - 1) * 0.5f)) * lineSpacingCm;
                    baseOffsetY[i] = 0f;
                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = 0f;
                }
                break;

            case MassNavFormationMode.Circle:
                float radius = MathF.Max(200f, count * lineSpacingCm / (MathF.PI * 2f));
                for (int i = 0; i < count; i++)
                {
                    float angle = (i / (float)count) * MathF.PI * 2f;
                    baseOffsetX[i] = MathF.Cos(angle) * radius;
                    baseOffsetY[i] = MathF.Sin(angle) * radius;
                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = baseOffsetY[i];
                }
                break;

            case MassNavFormationMode.Wedge:
                for (int i = 0; i < count; i++)
                {
                    if (i == 0)
                    {
                        baseOffsetX[i] = 0f;
                        baseOffsetY[i] = 0f;
                    }
                    else
                    {
                        int row = (int)Math.Ceiling(i / 2f);
                        int side = (i & 1) == 1 ? 1 : -1;
                        baseOffsetX[i] = side * row * wedgeSpacingCm;
                        baseOffsetY[i] = row * wedgeSpacingCm;
                    }

                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = baseOffsetY[i];
                }
                break;

            case MassNavFormationMode.Square:
            default:
                int cols = (int)Math.Ceiling(Math.Sqrt(count));
                int rows = (int)Math.Ceiling(count / (double)cols);
                float rowCenter = (rows - 1) * 0.5f;
                float colCenter = (cols - 1) * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;
                    baseOffsetX[i] = (col - colCenter) * squareSpacingCm;
                    baseOffsetY[i] = (row - rowCenter) * squareSpacingCm;
                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = baseOffsetY[i];
                }
                break;
        }

        if (MathF.Abs(rotationRadians) > 1e-5f)
        {
            ApplyRotation(offsetX, offsetY, baseOffsetX, baseOffsetY, rotationRadians);
        }
    }

    private static void RecomputeOffsets(FormationGroup group)
    {
        ApplyRotation(group.OffsetX, group.OffsetY, group.BaseOffsetX, group.BaseOffsetY, group.RotationRadians);
    }

    private static void ApplyRotation(float[] offsetX, float[] offsetY, float[] baseOffsetX, float[] baseOffsetY, float rotationRadians)
    {
        float cos = MathF.Cos(rotationRadians);
        float sin = MathF.Sin(rotationRadians);
        for (int i = 0; i < baseOffsetX.Length; i++)
        {
            float x = baseOffsetX[i];
            float y = baseOffsetY[i];
            offsetX[i] = (x * cos) - (y * sin);
            offsetY[i] = (x * sin) + (y * cos);
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

    private sealed class FormationGroup
    {
        public FormationGroup(int[] memberIndices, float[] baseOffsetX, float[] baseOffsetY, float[] offsetX, float[] offsetY)
        {
            MemberIndices = memberIndices;
            BaseOffsetX = baseOffsetX;
            BaseOffsetY = baseOffsetY;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

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
