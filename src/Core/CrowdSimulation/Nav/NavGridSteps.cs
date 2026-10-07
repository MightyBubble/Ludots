using System;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>网格方向与步代价(grid.js 移植)。8 连通,对角禁止切角。</summary>
public static class NavGridSteps
{
    public static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    public static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /// <summary>√2 的定点值(与 double 常数同值的最近定点)。</summary>
    public static readonly Fix64 Sqrt2 = Fix64.FromDouble(1.4142135623730951);

    /// <summary>8 连通走步:目标可走;对角时两侧正交格都需可走(禁切角)。</summary>
    public static bool CanStep(byte[] passable, int n, int x, int y, int d)
    {
        int nx = x + DX[d], ny = y + DY[d];
        if (nx < 0 || ny < 0 || nx >= n || ny >= n) return false;
        if (passable[ny * n + nx] == 0) return false;
        if (d >= 4 && (passable[y * n + nx] == 0 || passable[ny * n + x] == 0)) return false;
        return true;
    }

    /// <summary>步代价 = 两端格代价均值 × 方向长度(对角 √2)。</summary>
    public static Fix64 StepCost(Fix64[] cost, int a, int b, int d)
        => (cost[a] + cost[b]) * Fix64.HalfValue * (d >= 4 ? Sqrt2 : Fix64.OneValue);

    /// <summary>八向启发距离(格)。</summary>
    public static Fix64 Octile(int a, int b, int n)
    {
        int dx = Math.Abs(a % n - b % n);
        int dy = Math.Abs(a / n - b / n);
        return Fix64.FromInt(dx + dy) + (Sqrt2 - 2 * Fix64.OneValue) * Math.Min(dx, dy);
    }

    public static int ClusterOf(int cell, int n, int s, int c)
        => (cell / n / s) * c + (cell % n) / s;

    public static int LocalIndex(int cell, int n, int s)
        => ((cell / n) % s) * s + (cell % n) % s;

    /// <summary>两格中心间的超覆盖视线(保守切角;lineOfSight 移植,流场拉直用)。</summary>
    public static bool LineOfSight(byte[] passable, int n, int a, int b)
    {
        int x = a % n, y = a / n;
        int x1 = b % n, y1 = b / n;
        int dx = Math.Abs(x1 - x), dy = Math.Abs(y1 - y);
        int sx = x < x1 ? 1 : -1, sy = y < y1 ? 1 : -1;
        for (int ix = 0, iy = 0; ix < dx || iy < dy;)
        {
            int decision = (1 + 2 * ix) * dy - (1 + 2 * iy) * dx;
            if (decision == 0)
            {
                if (passable[y * n + x + sx] == 0 || passable[(y + sy) * n + x] == 0) return false;
                x += sx; y += sy; ix++; iy++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { y += sy; iy++; }

            if (passable[y * n + x] == 0) return false;
        }

        return true;
    }
}
