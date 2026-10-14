using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Structures;

/// <summary>放置被拒绝的原因。数值越大越优先；预览和指令执行用同一套。</summary>
public enum CrowdPlacementReject : byte
{
    None = 0,
    SpanTerrain = 1,
    AnchorTerrain = 2,
    OverlapsBlocked = 3,
    OverlapsStructure = 4,
    OutOfBounds = 5,
}

/// <summary>
/// 放置合法性。输入是结构仓、模板和足迹，输出原因以及足迹实际盖住的导航格。
/// 桥的端点格必须落在 anchorTerrain，其余桥面格必须落在 spanTerrain。
/// 重叠只看同一层。越界按整段足迹的包围盒判定，不把界内仍合法的格标成非法。
/// </summary>
public static class CrowdStructurePlacement
{
    public const byte FlagIllegal = 1;
    public const byte FlagPortal = 2;
    public const byte FlagAnchor = 4;

    public static CrowdPlacementReject Judge(
        CrowdStructuresStore store,
        RuntimeStructureTemplate template,
        CrowdStructureFootprint footprint,
        ref int[] cells,
        ref byte[] flags,
        out int count)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(template);
        int side = store.NavCellsPerSide;
        int cellCm = store.NavCellSizeCm;
        var (minX, minY, maxX, maxY) = footprint.Bbox();
        var sideCm = Fix64.FromInt(side * cellCm);
        bool outside = minX < Fix64.Zero || minY < Fix64.Zero || maxX > sideCm || maxY > sideCm;
        var reason = outside ? CrowdPlacementReject.OutOfBounds : CrowdPlacementReject.None;

        if (template.Layered)
        {
            reason = JudgeBridge(store, template, footprint, ref cells, ref flags, out count, reason);
        }
        else
        {
            var rect = footprint.CellRectOf(cellCm, side);
            int width = rect.X1 - rect.X0;
            int height = rect.Y1 - rect.Y0;
            int cap = width > 0 && height > 0 ? width * height : 0;
            Ensure(ref cells, ref flags, cap);
            count = 0;
            for (int y = rect.Y0; y < rect.Y1; y++)
            {
                for (int x = rect.X0; x < rect.X1; x++)
                {
                    if (!store.GroundCellHit(template, footprint, x, y)) continue;
                    byte flag = GroundFlag(store, template.Layered, x, y, ref reason);
                    cells[count] = y * side + x;
                    flags[count] = flag;
                    count++;
                }
            }
        }

        return reason;
    }

    private static CrowdPlacementReject JudgeBridge(
        CrowdStructuresStore store,
        RuntimeStructureTemplate template,
        CrowdStructureFootprint footprint,
        ref int[] cells,
        ref byte[] flags,
        out int count,
        CrowdPlacementReject reason)
    {
        int side = store.NavCellsPerSide;
        int cellCm = store.NavCellSizeCm;
        var reach = CrowdReferenceMeters.TruncatedCmToM(store.PortalReachNavCells * cellCm);
        var geo = DeckSpanGeometry.FromCentimeters(
            footprint.X0.ToInt(), footprint.Y0.ToInt(), footprint.X1.ToInt(), footprint.Y1.ToInt(), footprint.WidthCm.ToInt(),
            side, cellCm, reach);
        int spanW = geo.MaxX >= geo.MinX ? geo.MaxX - geo.MinX + 1 : 0;
        int spanH = geo.MaxY >= geo.MinY ? geo.MaxY - geo.MinY + 1 : 0;
        Ensure(ref cells, ref flags, spanW > 0 && spanH > 0 ? spanW * spanH : 0);
        count = 0;
        bool painted0 = false;
        bool painted1 = false;
        var anchor = store.AnchorTerrain;
        var span = store.SpanTerrain;
        for (int y = geo.MinY; y <= geo.MaxY; y++)
        {
            for (int x = geo.MinX; x <= geo.MaxX; x++)
            {
                if (!geo.Hit(x, y, out Fix64 endDist)) continue;
                bool anchorCell = geo.IsAnchorCell(x, y);
                if (x == geo.EndX0 && y == geo.EndY0) painted0 = true;
                if (x == geo.EndX1 && y == geo.EndY1) painted1 = true;
                byte flag = 0;
                if (anchorCell) flag |= FlagAnchor;
                if (geo.IsPortal(endDist)) flag |= FlagPortal;
                int terrain = store.TerrainIndex(y * side + x);
                bool terrainOk = anchorCell
                    ? terrain < anchor.Length && anchor[terrain]
                    : terrain < span.Length && span[terrain];
                if (!terrainOk)
                {
                    flag |= FlagIllegal;
                    reason = Worse(reason, anchorCell ? CrowdPlacementReject.AnchorTerrain : CrowdPlacementReject.SpanTerrain);
                }

                flag |= GroundFlag(store, template.Layered, x, y, ref reason);
                cells[count] = y * side + x;
                flags[count] = flag;
                count++;
            }
        }

        if (!painted0 || !painted1)
        {
            reason = Worse(reason, CrowdPlacementReject.AnchorTerrain);
        }

        return reason;
    }

    private static byte GroundFlag(
        CrowdStructuresStore store,
        bool layered,
        int x,
        int y,
        ref CrowdPlacementReject reason)
    {
        byte flag = 0;
        int cell = y * store.NavCellsPerSide + x;
        if (store.Blocked[cell] != 0)
        {
            flag |= FlagIllegal;
            reason = Worse(reason, CrowdPlacementReject.OverlapsBlocked);
        }

        if (OverlapsSameLayer(store, layered, x, y))
        {
            flag |= FlagIllegal;
            reason = Worse(reason, CrowdPlacementReject.OverlapsStructure);
        }

        return flag;
    }

    private static bool OverlapsSameLayer(CrowdStructuresStore store, bool layered, int x, int y)
    {
        int n = store.LiveCount;
        for (int i = 0; i < n; i++)
        {
            int id = store.LiveId(i);
            var existing = store.LiveTemplate(id);
            if (existing.Layered != layered) continue;
            var fp = store.FootprintOf(id);
            if (layered)
            {
                if (BridgePaints(store, fp, x, y)) return true;
            }
            else if (store.GroundCellHit(existing, fp, x, y))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BridgePaints(CrowdStructuresStore store, CrowdStructureFootprint footprint, int x, int y)
    {
        var reach = CrowdReferenceMeters.TruncatedCmToM(store.PortalReachNavCells * store.NavCellSizeCm);
        var geo = DeckSpanGeometry.FromCentimeters(
            footprint.X0.ToInt(), footprint.Y0.ToInt(), footprint.X1.ToInt(), footprint.Y1.ToInt(), footprint.WidthCm.ToInt(),
            store.NavCellsPerSide, store.NavCellSizeCm, reach);
        return geo.Hit(x, y, out _);
    }

    private static CrowdPlacementReject Worse(CrowdPlacementReject current, CrowdPlacementReject next) =>
        (byte)next > (byte)current ? next : current;

    private static void Ensure(ref int[] cells, ref byte[] flags, int needed)
    {
        if (needed < 0) needed = 0;
        if (cells == null || cells.Length < needed) cells = new int[needed];
        if (flags == null || flags.Length < needed) flags = new byte[needed];
    }
}
