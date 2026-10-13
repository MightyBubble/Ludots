using System;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Vision;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 本机座位看见的单位和地图实体。调试叠加层不走这里。
/// 遮挡已经记在迷雾格的可见性上，这里不再另做射线。
/// </summary>
public static class CrowdPlayerVisibility
{
    public static bool UnitShown(CrowdSimSession session, int viewerGroup, int dense, bool hideFog)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!hideFog) return true;
        var fog = session.Fog;
        if (fog == null) return true;
        int playerId = session.World.Get<PlayerOwner>(session.Units.EntityAt(dense)).PlayerId;
        int index = session.Config.PairRules.IndexByPlayerId[playerId];
        if ((uint)index >= (uint)fog.GroupOf.Length) return false;
        if (fog.GroupOf[index] == viewerGroup) return true;
        if (!TryFogCell(fog, Position(session, dense), out int cell)) return false;
        return fog.CellVisibilityOf(viewerGroup, cell) == CellVisibility.Visible;
    }

    public static bool StructureShown(CrowdSimSession session, int viewerGroup, int structureId, bool hideFog)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!hideFog) return true;
        var fog = session.Fog;
        if (fog == null || (uint)viewerGroup >= (uint)fog.Belief.Length) return false;
        return fog.Belief[viewerGroup].ContainsKey(structureId);
    }

    private static Fix64Vec2 Position(CrowdSimSession session, int dense)
    {
        if (session.Movement != null) return session.Movement.Positions[dense];
        return session.World.Get<WorldPositionCm>(session.Units.EntityAt(dense)).Value;
    }

    private static bool TryFogCell(CrowdFog fog, Fix64Vec2 position, out int cell)
    {
        var cellCm = Fix64.FromInt(fog.FogCellSizeCm);
        int x = (int)(position.X / cellCm).ToLong();
        int y = (int)(position.Y / cellCm).ToLong();
        if (x < 0 || y < 0 || x >= fog.FogCellsPerSide || y >= fog.FogCellsPerSide)
        {
            cell = -1;
            return false;
        }

        cell = y * fog.FogCellsPerSide + x;
        return true;
    }
}
