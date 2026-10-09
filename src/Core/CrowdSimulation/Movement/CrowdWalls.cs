using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 盘面接触(walls.js 移植,Fix64):净空侵蚀把圆盘取整成整格环后,可走格中心只保证半格自由,
/// 这里精确解掉剩余的亚格重叠。被挡格 = 轴对齐盒。开放格缓存:3×3 邻域全走的格,
/// r ≤ 格尺寸的圆盘够不到任何被挡格,直接跳过。
/// </summary>
public static class CrowdWalls
{
    /// <summary>开放格缓存:按 (navId, walk 版本) 懒填;0 未知 / 1 开放 / 2 非开放。</summary>
    public sealed class OpenCellCache
    {
        private sealed class Entry
        {
            public int Version = -1;
            public byte[]? Walk;
            public byte[]? UpWalk; // 桥面层以 UpPass 为 walk 口径
            public byte[]? Grid;
        }

        private readonly Dictionary<int, Entry> _byNavId = new();

        public byte[] GridFor(NavContext nav)
        {
            int n2 = nav.CellCount * nav.CellCount;
            if (!_byNavId.TryGetValue(nav.Id, out var e))
            {
                e = new Entry();
                _byNavId[nav.Id] = e;
            }

            if (e.Version != nav.Version || !ReferenceEquals(e.Walk, nav.Walk) || !ReferenceEquals(e.UpWalk, nav.UpPass))
            {
                // 重烘对 Walk 是原地腐蚀(引用不变),失效必须看 NavContext.Version——与参考端
                // openCells 的版本失效同款;只认引用会让放置当帧马达读到旧开放表。
                e.Grid ??= new byte[2 * n2];
                Array.Clear(e.Grid);
                e.Version = nav.Version;
                e.Walk = nav.Walk;
                e.UpWalk = nav.UpPass;
            }

            return e.Grid!;
        }
    }

    /// <summary>该格的 3×3 邻域(界内)是否全可走。</summary>
    public static bool IsOpen(byte[] grid, byte[] walk, int n, int cell, int levelOffset)
    {
        byte v = grid[levelOffset + cell];
        if (v != 0) return v == 1;
        int cy = cell / n, cx = cell - cy * n;
        int open = 1;
        for (int y = Math.Max(0, cy - 1); open != 0 && y <= Math.Min(n - 1, cy + 1); y++)
        {
            for (int x = Math.Max(0, cx - 1); x <= Math.Min(n - 1, cx + 1); x++)
            {
                if (walk[y * n + x] == 0) { open = 0; break; }
            }
        }

        grid[levelOffset + cell] = open != 0 ? (byte)1 : (byte)2;
        return open == 1;
    }

    /// <summary>把圆盘推出所有相交的被挡格;返回 true 并写出修正位置与接触法线。</summary>
    public static bool ResolveWalls(
        byte[] walk, int n, int cellSizeCm,
        Fix64 pxCm, Fix64 pyCm, Fix64 radiusCm,
        out Fix64 outX, out Fix64 outY, out Fix64 normalX, out Fix64 normalY)
    {
        outX = pxCm;
        outY = pyCm;
        normalX = Fix64.Zero;
        normalY = Fix64.Zero;
        Fix64 cs = Fix64.FromInt(cellSizeCm);
        int x0 = Math.Max(0, (int)Fix64.Floor((pxCm - radiusCm) / cs).ToLong());
        int x1 = Math.Min(n - 1, (int)Fix64.Floor((pxCm + radiusCm) / cs).ToLong());
        int y0 = Math.Max(0, (int)Fix64.Floor((pyCm - radiusCm) / cs).ToLong());
        int y1 = Math.Min(n - 1, (int)Fix64.Floor((pyCm + radiusCm) / cs).ToLong());
        if (x0 == x1 && y0 == y1) return false; // 圆盘整个在自格(可走)内
        bool moved = false;
        Fix64 r2 = radiusCm * radiusCm;
        Fix64 px = pxCm, py = pyCm;
        for (int cy = y0; cy <= y1; cy++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                if (walk[cy * n + cx] != 0) continue;
                Fix64 bx = Fix64.FromInt(cx) * cs, by = Fix64.FromInt(cy) * cs;
                Fix64 qx = Fix64.Min(bx + cs, Fix64.Max(bx, px));
                Fix64 qy = Fix64.Min(by + cs, Fix64.Max(by, py));
                Fix64 dx = px - qx, dy = py - qy;
                Fix64 d2 = dx * dx + dy * dy;
                // 参考端 1e-12 是双精度域守卫,Q31.32 下 FromFloat(1e-12f) 截断为 0——最小正 raw 量即等价语义
                if (d2 >= r2 || d2 <= Fix64.FromRaw(1)) continue;
                Fix64 d = Fix64Math.Sqrt(d2);
                dx /= d;
                dy /= d;
                px = qx + dx * radiusCm;
                py = qy + dy * radiusCm;
                normalX = dx;
                normalY = dy;
                moved = true;
            }
        }

        outX = px;
        outY = py;
        return moved;
    }
}
