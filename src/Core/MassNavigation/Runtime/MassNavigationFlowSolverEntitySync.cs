using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed partial class MassNavigationFlowSolverState
{
    public void SyncEntities(World world, MassNavigationAgentState agentState)
    {
        if (UnitCount <= 0 || _entitySyncDirtyCount <= 0)
        {
            return;
        }

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
            if (!agentState.TryGetAgentEntity(i, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigationFlowSolverState cannot sync unit {i} because no tracked agent entity is registered.");
            }

            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigationFlowSolverState cannot sync unit {i} because tracked entity {entity.Id} is not alive.");
            }

            int i2 = i << 1;
            float xCm = _positionsCm[i2];
            float yCm = _positionsCm[i2 + 1];
            float worldXCm = _worldOriginXCm + xCm;
            float worldYCm = _worldOriginYCm + yCm;
            Fix64Vec2 worldValue = Fix64Vec2.FromFloat(worldXCm, worldYCm);
            ref WorldPositionCm worldPosition = ref world.Get<WorldPositionCm>(entity);
            worldPosition.Value = worldValue;

        }

        _entitySyncDirtyCount = 0;
    }

    public void SyncCullStates(
        World world,
        MassNavigationAgentState agentState,
        float localMinXCm,
        float localMaxXCm,
        float localMinYCm,
        float localMaxYCm)
    {
        int count = Math.Min(UnitCount, agentState.ControllableAgentSlotCount);
        for (int i = 0; i < count; i++)
        {
            if (!agentState.TryGetControllableEntity(i, out Entity entity))
            {
                continue;
            }

            if (!world.IsAlive(entity) || !world.Has<CullState>(entity))
            {
                continue;
            }

            int i2 = i << 1;
            float xCm = _positionsCm[i2];
            float yCm = _positionsCm[i2 + 1];
            bool visible = xCm >= localMinXCm &&
                xCm <= localMaxXCm &&
                yCm >= localMinYCm &&
                yCm <= localMaxYCm;
            ref CullState cull = ref world.Get<CullState>(entity);
            cull.IsVisible = visible;
            cull.LOD = visible ? LODLevel.High : LODLevel.Culled;
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
