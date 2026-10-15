using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace CrowdSimulationTests.Uat;

/// <summary>一座地图桥的过桥路线：桥实体足迹、单位方向，以及两端外侧各一个可走地面点。</summary>
internal sealed record CrowdUatBridgeRoute(
    int Id,
    CrowdStructureFootprint Footprint,
    Fix64 Ux,
    Fix64 Uy,
    Fix64 LengthCm,
    (Fix64 X, Fix64 Y) A,
    (Fix64 X, Fix64 Y) B);

/// <summary>桥类 UAT 的公用工具：列出地图上的分层实体，给每座桥找过桥路线。</summary>
internal static class CrowdUatBridges
{
    /// <summary>结构仓里模板为分层（桥）的实体 id，按 id 序。</summary>
    public static List<int> LayeredIds(CrowdSimSession session)
    {
        var store = session.Structures ?? throw new InvalidOperationException("场景无效：会话没有结构仓。");
        var templates = session.Config.Structures.Templates;
        var ids = new List<int>();
        foreach (int id in store.EntityIds)
        {
            if (store.TryGetTemplate(id, out int tpl) && templates[tpl].Layered) ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// 桥两端各往外走 <paramref name="beyondCells"/> 格，取第一个在 <paramref name="nav"/> 上可走的地面点，
    /// 最多找到 3 × beyondCells 格。崎岖小图的桥头地面可能陡或被腐蚀掉。任一端找不到返回 null。
    /// </summary>
    public static CrowdUatBridgeRoute? Route(CrowdSimSession session, NavContext nav, int id, int beyondCells = 4)
    {
        var store = session.Structures!;
        var fp = store.FootprintOf(id);
        if (fp.Shape != CrowdStructureShape.Path) return null;
        Fix64 dx = fp.X1 - fp.X0, dy = fp.Y1 - fp.Y0;
        Fix64 length = CrowdFix.Hypot(dx, dy);
        Fix64 ux = dx / length, uy = dy / length;
        var a = EndPoint(session, nav, fp.X0, fp.Y0, -ux, -uy, beyondCells);
        var b = EndPoint(session, nav, fp.X1, fp.Y1, ux, uy, beyondCells);
        return a is { } pa && b is { } pb ? new CrowdUatBridgeRoute(id, fp, ux, uy, length, pa, pb) : null;
    }

    public static int CellOf(CrowdSimSession session, Fix64 x, Fix64 y)
        => CrowdSimulationSpace.CellAtStrict(x, y, session.Config.NavCellSizeCm, session.Config.NavCellsPerSide);

    private static (Fix64 X, Fix64 Y)? EndPoint(
        CrowdSimSession session, NavContext nav, Fix64 x, Fix64 y, Fix64 sx, Fix64 sy, int beyondCells)
    {
        Fix64 cs = Fix64.FromInt(session.Config.NavCellSizeCm);
        Fix64 size = Fix64.FromInt(session.Config.NavCellsPerSide) * cs;
        for (int k = beyondCells; k <= 3 * beyondCells; k++)
        {
            Fix64 step = Fix64.FromInt(k) * cs;
            Fix64 px = x + sx * step, py = y + sy * step;
            if (px < Fix64.Zero || py < Fix64.Zero || px >= size || py >= size) return null;
            if (nav.Passable[CellOf(session, px, py)] != 0) return (px, py);
        }

        return null;
    }
}
