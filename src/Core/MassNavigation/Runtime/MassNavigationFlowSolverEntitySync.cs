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
            // Displaced agents: pose authority belongs to an external writer for
            // the duration of the window, so the solver must not write WorldPositionCm back.
            // The committed pose is re-ingested via SyncDisplacedAgentPoses instead.
            if (_displacedAgentFlags[i] != 0)
            {
                continue;
            }

            if (!agentState.TryGetAgentEntity(i, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"MassNavigationFlowSolverState cannot sync unit {i} because no tracked agent entity is registered.");
            }
            // #region agent log
            { float _sx = _worldOriginXCm + _positionsCm[i << 1]; float _sy = _worldOriginYCm + _positionsCm[(i << 1) + 1]; if ((_frameCount & 15) == 0 && (_sx < 400000f || _sx > 700000f)) { try { var _n = world.TryGet(entity, out Name _nm) ? _nm.Value : null; System.IO.File.AppendAllText("/opt/cursor/logs/debug.log", System.Text.Json.JsonSerializer.Serialize(new { hypothesisId = "C", location = "MassNavigationFlowSolverEntitySync.cs:SyncEntities", message = "sync WorldPositionCm", data = new { unitIndex = i, entityId = entity.Id, name = _n, worldX = _sx, worldY = _sy, hasUnitTarget = _hasUnitTarget[i] }, timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { } } }
            // #endregion

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
            Fix64Vec2 worldValue = Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm));
            ref WorldPositionCm worldPosition = ref world.Get<WorldPositionCm>(entity);
            worldPosition.Value = worldValue;
        }

        _entitySyncDirtyCount = 0;
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
