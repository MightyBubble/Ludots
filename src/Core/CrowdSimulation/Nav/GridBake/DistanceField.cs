using System;

namespace Ludots.Core.CrowdSimulation.Nav.GridBake;

/// <summary>
/// 2D 格网切比雪夫距离场：可走格到最近阻挡格的距离，格边界视为阻挡。
/// 分水岭分区用它当距离场。
/// </summary>
public static class DistanceField
{
    public static byte[] ComputeClearance(byte[] walk, int navCellsPerSide)
    {
        var d = new byte[navCellsPerSide * navCellsPerSide];
        ComputeClearance(walk, navCellsPerSide, d);
        return d;
    }

    public static void ComputeClearance(byte[] walk, int navCellsPerSide, byte[] d)
    {
        Array.Clear(d, 0, navCellsPerSide * navCellsPerSide);
        for (int y = 0; y < navCellsPerSide; y++)
        {
            for (int x = 0; x < navCellsPerSide; x++)
            {
                int i = y * navCellsPerSide + x;
                if (walk[i] == 0) continue;
                if (x == 0 || y == 0 || x == navCellsPerSide - 1 || y == navCellsPerSide - 1) { d[i] = 1; continue; }
                int m = Math.Min(Math.Min(d[i - 1], d[i - navCellsPerSide]), Math.Min(d[i - navCellsPerSide - 1], d[i - navCellsPerSide + 1])) + 1;
                d[i] = (byte)Math.Min(m, 255);
            }
        }

        for (int y = navCellsPerSide - 2; y >= 1; y--)
        {
            for (int x = navCellsPerSide - 2; x >= 1; x--)
            {
                int i = y * navCellsPerSide + x;
                if (d[i] == 0) continue;
                int m = Math.Min(Math.Min(d[i + 1], d[i + navCellsPerSide]), Math.Min(d[i + navCellsPerSide + 1], d[i + navCellsPerSide - 1])) + 1;
                if (m < d[i]) d[i] = (byte)m;
            }
        }

    }
}
