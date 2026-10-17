using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Structures;

/// <summary>
/// 放置被拒绝的原因。数值越大越优先；预览和指令执行用同一套。
/// 越界与重叠不再产出（Web 参考接受它们，见 <see cref="CrowdStructurePlacement"/> 类注释），
/// 枚举值保留给呈现合同的既有文案面。
/// </summary>
public enum CrowdPlacementReject : byte
{
    None = 0,
    InteriorArea = 1,
    EndpointArea = 2,
    OverlapsBlocked = 3,
    OverlapsStructure = 4,
    OutOfBounds = 5,
}

/// <summary>
/// 放置合法性与足迹栅格化。输入是结构仓、模板和足迹，输出原因以及足迹实际盖住的导航格。
/// 越界与重叠不拒绝：Web 参考的放置接受它们，栅格按 <see cref="CrowdStructureFootprint.CellRectOf"/> 夹回网格，
/// 本仓库与其对齐（行为对拍三种子实锤：洪水盘越界、建筑压既有结构、桥跨非深水区，Web 均已放置）。
/// spanEnds 模板声明了 endpointAreas/interiorAreas 时仍按区域拒：两个端点格的静态地形区域必须在
/// endpointAreas 里，其余盖住的格必须在 interiorAreas 里，且两端锚格必须被条带实际覆盖；
/// 未声明区域表的模板（演示桥）无地形限制，与 Web 一致。
/// </summary>
public static class CrowdStructurePlacement
{
    public const byte FlagIllegal = 1;
    public const byte FlagPortal = 2;
    public const byte FlagEndpoint = 4;

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
        if (template.Placement == CrowdStructurePlacementKind.SpanEnds)
        {
            return JudgeSpanEnds(store, template, footprint, ref cells, ref flags, out count);
        }

        int side = store.NavCellsPerSide;
        int cellCm = store.NavCellSizeCm;
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
                if (!store.BaseLayerCellHit(template, footprint, x, y)) continue;
                cells[count] = y * side + x;
                flags[count] = 0;
                count++;
            }
        }

        return CrowdPlacementReject.None;
    }

    private static CrowdPlacementReject JudgeSpanEnds(
        CrowdStructuresStore store,
        RuntimeStructureTemplate template,
        CrowdStructureFootprint footprint,
        ref int[] cells,
        ref byte[] flags,
        out int count)
    {
        int side = store.NavCellsPerSide;
        int cellCm = store.NavCellSizeCm;
        var geo = UpperLayerSpanGeometry.FromCentimeters(
            footprint.X0.ToInt(), footprint.Y0.ToInt(), footprint.X1.ToInt(), footprint.Y1.ToInt(), footprint.WidthCm.ToInt(),
            side, cellCm, template.PortalReachNavCells);
        int spanW = geo.MaxX >= geo.MinX ? geo.MaxX - geo.MinX + 1 : 0;
        int spanH = geo.MaxY >= geo.MinY ? geo.MaxY - geo.MinY + 1 : 0;
        Ensure(ref cells, ref flags, spanW > 0 && spanH > 0 ? spanW * spanH : 0);
        count = 0;
        bool painted0 = false;
        bool painted1 = false;
        var endpointAreas = template.EndpointAreas;
        var interiorAreas = template.InteriorAreas;
        bool areaLimited = endpointAreas != null && interiorAreas != null;
        var reason = CrowdPlacementReject.None;
        for (int y = geo.MinY; y <= geo.MaxY; y++)
        {
            for (int x = geo.MinX; x <= geo.MaxX; x++)
            {
                if (!geo.Hit(x, y, out Fix64 endDist)) continue;
                bool endpointCell = geo.IsAnchorCell(x, y);
                if (x == geo.EndX0 && y == geo.EndY0) painted0 = true;
                if (x == geo.EndX1 && y == geo.EndY1) painted1 = true;
                byte flag = 0;
                if (endpointCell) flag |= FlagEndpoint;
                if (geo.IsPortal(endDist)) flag |= FlagPortal;
                if (areaLimited)
                {
                    int area = store.StaticTerrainArea(y * side + x);
                    bool areaOk = endpointCell ? endpointAreas![area] : interiorAreas![area];
                    if (!areaOk)
                    {
                        flag |= FlagIllegal;
                        reason = Worse(reason, endpointCell ? CrowdPlacementReject.EndpointArea : CrowdPlacementReject.InteriorArea);
                    }
                }

                cells[count] = y * side + x;
                flags[count] = flag;
                count++;
            }
        }

        if (areaLimited && (!painted0 || !painted1))
        {
            reason = Worse(reason, CrowdPlacementReject.EndpointArea);
        }

        return reason;
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
