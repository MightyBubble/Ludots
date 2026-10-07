using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>阻挡物盒的逐格 CSR 索引(S5 静态建;S7 动态地图再谈增量)。</summary>
public sealed class CrowdBlockerColliders
{
    private readonly int[] _cellStart;
    private readonly int[] _cellItems;
    private readonly (Fix64 X, Fix64 Y, Fix64 Hx, Fix64 Hy)[] _colliders;

    private CrowdBlockerColliders(int[] cellStart, int[] cellItems, (Fix64, Fix64, Fix64, Fix64)[] colliders)
    {
        _cellStart = cellStart;
        _cellItems = cellItems;
        _colliders = colliders;
    }

    public int Count => _colliders.Length;

    /// <summary>阻挡足迹(方块)按覆盖格登记;格内圆盘推出在查询时精确解。</summary>
    public static CrowdBlockerColliders Build(IReadOnlyList<BlockerFootprint> blockers, int n, int cellSizeCm)
    {
        var perCell = new List<int>[n * n + 1];
        var colliders = new List<(Fix64, Fix64, Fix64, Fix64)>();
        foreach (var b in blockers)
        {
            int id = colliders.Count;
            colliders.Add((Fix64.FromInt(b.XCm), Fix64.FromInt(b.YCm), Fix64.FromInt(b.HalfSizeCm), Fix64.FromInt(b.HalfSizeCm)));
            int x0 = Math.Max(0, (b.XCm - b.HalfSizeCm) / cellSizeCm), x1 = Math.Min(n - 1, (b.XCm + b.HalfSizeCm) / cellSizeCm);
            int y0 = Math.Max(0, (b.YCm - b.HalfSizeCm) / cellSizeCm), y1 = Math.Min(n - 1, (b.YCm + b.HalfSizeCm) / cellSizeCm);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    (perCell[y * n + x] ??= new List<int>()).Add(id);
                }
            }
        }

        var cellStart = new int[n * n + 1];
        int total = 0;
        for (int c = 0; c < n * n; c++)
        {
            cellStart[c] = total;
            total += perCell[c]?.Count ?? 0;
        }

        cellStart[n * n] = total;
        var cellItems = new int[total];
        for (int c = 0; c < n * n; c++)
        {
            if (perCell[c] == null) continue;
            perCell[c].CopyTo(cellItems, cellStart[c]);
        }

        return new CrowdBlockerColliders(cellStart, cellItems, colliders.ToArray());
    }

    /// <summary>把圆盘推出该格登记的每个阻挡盒(resolveColliders 移植);返回 true 并写出修正位置与法线。</summary>
    public bool Resolve(int cell, Fix64 pxCm, Fix64 pyCm, Fix64 radiusCm,
        out Fix64 outX, out Fix64 outY, out Fix64 normalX, out Fix64 normalY)
    {
        bool moved = false;
        Fix64 px = pxCm, py = pyCm;
        Fix64 nx = Fix64.Zero, ny = Fix64.Zero;
        for (int k = _cellStart[cell], e = _cellStart[cell + 1]; k < e; k++)
        {
            var o = _colliders[_cellItems[k]];
            Fix64 qx = Fix64.Min(o.X + o.Hx, Fix64.Max(o.X - o.Hx, px));
            Fix64 qy = Fix64.Min(o.Y + o.Hy, Fix64.Max(o.Y - o.Hy, py));
            Fix64 dx = px - qx, dy = py - qy;
            Fix64 d2 = dx * dx + dy * dy;
            if (d2 >= radiusCm * radiusCm) continue;
            if (d2 > Fix64.FromFloat(1e-8f))
            {
                Fix64 d = Fix64Math.Sqrt(d2);
                dx /= d;
                dy /= d;
                px = qx + dx * radiusCm;
                py = qy + dy * radiusCm;
            }
            else
            {
                // 圆心进盒:沿最浅轴退出
                Fix64 ex = o.Hx - Fix64.Abs(px - o.X), ey = o.Hy - Fix64.Abs(py - o.Y);
                if (ex < ey)
                {
                    dx = px >= o.X ? Fix64.OneValue : -Fix64.OneValue;
                    dy = Fix64.Zero;
                    px = o.X + dx * (o.Hx + radiusCm);
                }
                else
                {
                    dx = Fix64.Zero;
                    dy = py >= o.Y ? Fix64.OneValue : -Fix64.OneValue;
                    py = o.Y + dy * (o.Hy + radiusCm);
                }
            }

            nx = dx;
            ny = dy;
            moved = true;
        }

        outX = px;
        outY = py;
        normalX = nx;
        normalY = ny;
        return moved;
    }
}
