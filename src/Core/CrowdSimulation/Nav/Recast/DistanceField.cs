using System;

namespace Ludots.Core.CrowdSimulation.Nav.Recast;

/// <summary>
/// 切比雪夫距离场（computeClearance 移植）:格内到最近阻挡格的距离,
/// 格边界视为阻挡。作为分水岭分割的距离场输入。
/// </summary>
public static class DistanceField
{
    public static byte[] ComputeClearance(byte[] walk, int n)
    {
        var d = new byte[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                if (walk[i] == 0) continue;
                if (x == 0 || y == 0 || x == n - 1 || y == n - 1) { d[i] = 1; continue; }
                int m = Math.Min(Math.Min(d[i - 1], d[i - n]), Math.Min(d[i - n - 1], d[i - n + 1])) + 1;
                d[i] = (byte)Math.Min(m, 255);
            }
        }

        for (int y = n - 2; y >= 1; y--)
        {
            for (int x = n - 2; x >= 1; x--)
            {
                int i = y * n + x;
                if (d[i] == 0) continue;
                int m = Math.Min(Math.Min(d[i + 1], d[i + n]), Math.Min(d[i + n + 1], d[i + n - 1])) + 1;
                if (m < d[i]) d[i] = (byte)m;
            }
        }

        return d;
    }
}
