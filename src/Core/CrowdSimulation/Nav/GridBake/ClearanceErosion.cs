using System;

namespace Ludots.Core.CrowdSimulation.Nav.GridBake;

/// <summary>
/// 整图切比雪夫净空腐蚀。半径 = clearance - 1 格；clearance ≤ 1 时原样复制。
/// 先行扫描再列扫描，两遍各计连续可走段。行缓冲只写下标 ≥ 0 的格，没写到的格保持 0，地图边缘外视为不可走。
/// </summary>
internal static class ClearanceErosion
{
    internal static void Erode(byte[] walk, int navCellsPerSide, int clearance, byte[] output)
    {
        int r = clearance - 1;
        if (r <= 0)
        {
            Array.Copy(walk, output, walk.Length);
            return;
        }

        int need = 2 * r + 1;
        var row = new byte[navCellsPerSide * navCellsPerSide];
        for (int y = 0; y < navCellsPerSide; y++)
        {
            int baseIdx = y * navCellsPerSide;
            int run = 0;
            for (int c = 0; c < navCellsPerSide; c++)
            {
                run = walk[baseIdx + c] != 0 ? run + 1 : 0;
                int x = c - r;
                if (x >= 0) row[baseIdx + x] = run >= need ? (byte)1 : (byte)0;
            }
        }

        for (int x = 0; x < navCellsPerSide; x++)
        {
            int run = 0;
            for (int yy = 0; yy < navCellsPerSide; yy++)
            {
                run = row[yy * navCellsPerSide + x] != 0 ? run + 1 : 0;
                int y = yy - r;
                if (y >= 0) output[y * navCellsPerSide + x] = run >= need ? (byte)1 : (byte)0;
            }

            for (int y = Math.Max(0, navCellsPerSide - r); y < navCellsPerSide; y++) output[y * navCellsPerSide + x] = 0;
        }
    }
}
