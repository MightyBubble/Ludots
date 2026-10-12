using System;
using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Nav.Recast;

/// <summary>
/// 轮廓追踪（buildContours 移植）:沿区域边界走格角点,标签 = 跨过该段的邻域
/// （0 = 墙,-1 - dir = tile 外该侧）。tile 边界段永不简化成墙、tile 角必保留,
/// 跨 tile 拼接的边界因此逐位精确。Douglas-Peucker 用整数叉积 + 坐标并列决胜,
/// 共享边界从两侧简化结果一致。
/// </summary>
public static class ContourTracer
{
    private static readonly int[] OX = { -1, 0, 1, 0 };
    private static readonly int[] OY = { 0, 1, 0, -1 };

    internal static void BuildContours(TileBakeScratch scratch, int regionCount, int navCellsPerSide, double maxError, double maxEdgeLen)
    {
        var reg = scratch.Reg;
        var flags = scratch.Flags;
        Array.Clear(flags, 0, navCellsPerSide * navCellsPerSide);
        scratch.NoteLoops(regionCount);
        for (int y = 0; y < navCellsPerSide; y++)
        {
            for (int x = 0; x < navCellsPerSide; x++)
            {
                int i = y * navCellsPerSide + x, r = reg[i];
                if (r == 0) continue;
                int f = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + OX[d], ny = y + OY[d];
                    int q = nx < 0 || ny < 0 || nx >= navCellsPerSide || ny >= navCellsPerSide ? 0 : reg[ny * navCellsPerSide + nx];
                    if (q != r) f |= 1 << d;
                }

                flags[i] = (byte)f;
            }
        }

        for (int i = 0; i < navCellsPerSide * navCellsPerSide; i++)
        {
            while (flags[i] != 0)
            {
                scratch.Raw.Clear();
                Walk(i % navCellsPerSide, i / navCellsPerSide, flags, reg, navCellsPerSide, scratch.Raw);
                var pts = scratch.Simplify(scratch.Raw, maxError, maxEdgeLen);
                if (pts.Count >= 6) scratch.Loops[reg[i]].Add(pts);
                else scratch.ReturnLoop(pts);
            }
        }
    }

    private static void Walk(int sx, int sy, byte[] flags, int[] reg, int navCellsPerSide, List<int> output)
    {
        int x = sx, y = sy, i = y * navCellsPerSide + x;
        int dir = 0;
        while ((flags[i] & (1 << dir)) == 0) dir++;
        int startDir = dir, startI = i;
        for (int iter = 0; iter < 4 * navCellsPerSide * navCellsPerSide; iter++)
        {
            if ((flags[i] & (1 << dir)) != 0)
            {
                int px = x, py = y;
                if (dir == 0) py++;
                else if (dir == 1) { px++; py++; }
                else if (dir == 2) px++;
                int nx = x + OX[dir], ny = y + OY[dir];
                output.Add(px);
                output.Add(py);
                output.Add(nx < 0 || ny < 0 || nx >= navCellsPerSide || ny >= navCellsPerSide ? -1 - dir : reg[ny * navCellsPerSide + nx]);
                flags[i] &= (byte)~(1 << dir);
                dir = (dir + 1) & 3;
            }
            else
            {
                x += OX[dir]; y += OY[dir]; i = y * navCellsPerSide + x;
                dir = (dir + 3) & 3;
            }

            if (i == startI && dir == startDir) break;
        }
    }

}
