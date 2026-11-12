using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed partial class MassNavigationFlowSolverState
{
    internal void SyncEntities(World world, MassNavigationAgentState agentState)
    {
        if (UnitCount <= 0 || _entitySyncDirtyCount <= 0)
        {
            LastEntitySyncAgentCount = 0;
            return;
        }

        int syncedCount = 0;
        int dirtyCount = _entitySyncDirtyCount;
        for (int dirtyIndex = 0; dirtyIndex < dirtyCount; dirtyIndex++)
        {
            int i = _entitySyncDirtyAgents[dirtyIndex];
            if ((uint)i >= (uint)UnitCount)
            {
                throw new InvalidOperationException(
                    $"MassNavigationFlowSolverState dirty agent index {i} exceeds unit count {UnitCount}.");
            }

            _entitySyncDirtyFlags[i] = 0;
            // Displaced agents: pose authority belongs to an external writer for
            // the duration of the window, so the solver must not write WorldPositionCm back.
            // The committed pose is re-ingested via SyncDisplacedAgentPoses instead.
            if (_displacedAgentFlags[i] != 0)
            {
                continue;
            }

            syncedCount++;
            if (!agentState.TryGetAgentEntity(i, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigationFlowSolverState cannot sync unit {i} because no tracked agent entity is registered.");
            }

            if (!world.IsAlive(entity))
            {
                // Agents destroyed outside the nav runtime's own removal (death rule,
                // two-phase presentation destroy) stay dirty for one removal cycle;
                // skipping the pose write is the guard — the metadata sync drops the
                // slot on its next pass.
                syncedCount--;
                continue;
            }

            int i2 = i << 1;
            float xCm = _positionsCm[i2];
            float yCm = _positionsCm[i2 + 1];
            float worldXCm = _worldOriginXCm + xCm;
            float worldYCm = _worldOriginYcm + yCm;
            Fix64Vec2 worldValue = Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm));
            ref WorldPositionCm worldPosition = ref world.Get<WorldPositionCm>(entity);
            worldPosition.Value = worldValue;
        }

        LastEntitySyncAgentCount = syncedCount;
        _entitySyncDirtyCount = 0;
    }

    internal void EnsureWorldPoseComponents(World world, MassNavigationAgentState agentState)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(agentState);
        for (int i = 0; i < UnitCount; i++)
        {
            if (_displacedAgentFlags[i] != 0)
            {
                continue;
            }

            if (!agentState.TryGetAgentEntity(i, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigationFlowSolverState cannot publish unit {i} because no tracked agent entity is registered.");
            }

            if (!world.IsAlive(entity))
            {
                continue;
            }

            if (!world.Has<WorldPositionCm>(entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigation agent entity {entity.Id} requires WorldPositionCm when the solver window moves.");
            }
        }
    }

    internal void PublishShiftedWorldPoses(World world, MassNavigationAgentState agentState)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(agentState);
        for (int i = 0; i < UnitCount; i++)
        {
            if (_displacedAgentFlags[i] != 0)
            {
                continue;
            }

            if (!agentState.TryGetAgentEntity(i, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigationFlowSolverState cannot publish unit {i} because no tracked agent entity is registered.");
            }

            if (!world.IsAlive(entity))
            {
                continue;
            }

            if (!world.Has<WorldPositionCm>(entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigation agent entity {entity.Id} requires WorldPositionCm when the solver window moves.");
            }

            int i2 = i << 1;
            int publishedX = (int)MathF.Round(_worldOriginXCm + _positionsCm[i2]);
            int publishedY = (int)MathF.Round(_worldOriginYcm + _positionsCm[i2 + 1]);
            Fix64Vec2 published = Fix64Vec2.FromInt(publishedX, publishedY);
            ref WorldPositionCm worldPosition = ref world.Get<WorldPositionCm>(entity);
            Fix64Vec2 correction = published - worldPosition.Value;
            if (world.Has<PreviousWorldPositionCm>(entity))
            {
                // 本帧开头已经把上一帧位置抄走。窗口挪动若改写了当前世界坐标，
                // 上一帧必须加同一差值，插值跨度才仍是这一步真正走出的距离。
                ref PreviousWorldPositionCm previous = ref world.Get<PreviousWorldPositionCm>(entity);
                previous.Value += correction;
            }

            worldPosition.Value = published;
        }
    }

    private void MarkEntityDirty(int index)
    {
        if ((uint)index >= (uint)UnitCount || _entitySyncDirtyFlags[index] != 0)
        {
            return;
        }

        _entitySyncDirtyFlags[index] = 1;
        _entitySyncDirtyAgents[_entitySyncDirtyCount++] = index;
    }

    private void MarkAllEntitiesDirty()
    {
        _entitySyncDirtyCount = 0;
        Array.Clear(_entitySyncDirtyFlags, 0, _entitySyncDirtyFlags.Length);
        if (UnitCount <= 0)
        {
            return;
        }

        for (int i = 0; i < UnitCount; i++)
        {
            _entitySyncDirtyFlags[i] = 1;
            _entitySyncDirtyAgents[_entitySyncDirtyCount++] = i;
        }
    }
}
