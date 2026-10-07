using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 简单愚蠢漏斗(navquery.js funnel 移植):输入逐 portal 的左右端点(每 4 个值一组:
/// lx, ly, rx, ry;首末 portal 是起终点退化点),输出拉直后的折线顶点(x, y 平铺)。
/// 与参考实现共用同一收紧语义:右链越界收右点、左链越界收左点,越界即换 apex。
/// </summary>
public static class NavFunnel
{
    public static List<Fix64> Funnel(List<Fix64> portals)
    {
        int n = portals.Count / 4;
        var pts = new List<Fix64>();
        Fix64 ax = portals[0], ay = portals[1], lx = ax, ly = ay, rx = portals[2], ry = portals[3];
        int apex = 0, li = 0, ri = 0, guard = 0;
        pts.Add(ax); pts.Add(ay);
        for (int i = 1; i < n && guard++ < n * 8; i++)
        {
            Fix64 Lx = portals[i * 4], Ly = portals[i * 4 + 1], Rx = portals[i * 4 + 2], Ry = portals[i * 4 + 3];
            if (Tri(ax, ay, rx, ry, Rx, Ry) <= Fix64.Zero)
            {
                if ((ax == rx && ay == ry) || Tri(ax, ay, lx, ly, Rx, Ry) > Fix64.Zero) { rx = Rx; ry = Ry; ri = i; }
                else
                {
                    ax = lx; ay = ly; apex = li; pts.Add(ax); pts.Add(ay);
                    lx = rx = ax; ly = ry = ay; li = ri = apex; i = apex; continue;
                }
            }

            if (Tri(ax, ay, lx, ly, Lx, Ly) >= Fix64.Zero)
            {
                if ((ax == lx && ay == ly) || Tri(ax, ay, rx, ry, Lx, Ly) < Fix64.Zero) { lx = Lx; ly = Ly; li = i; }
                else
                {
                    ax = rx; ay = ry; apex = ri; pts.Add(ax); pts.Add(ay);
                    lx = rx = ax; ly = ry = ay; li = ri = apex; i = apex; continue;
                }
            }
        }

        Fix64 ex = portals[(n - 1) * 4], ey = portals[(n - 1) * 4 + 1];
        if (pts[pts.Count - 2] != ex || pts[pts.Count - 1] != ey) { pts.Add(ex); pts.Add(ey); }
        return pts;
    }

    private static Fix64 Tri(Fix64 ax, Fix64 ay, Fix64 bx, Fix64 by, Fix64 cx, Fix64 cy)
        => (cx - ax) * (by - ay) - (bx - ax) * (cy - ay);
}
