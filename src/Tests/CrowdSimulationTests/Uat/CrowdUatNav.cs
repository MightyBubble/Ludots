using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Nav;

namespace CrowdSimulationTests.Uat;

/// <summary>导航格与流场掩码的取格辅助。</summary>
internal static class CrowdUatNav
{
    public static IEnumerable<int> PassableCells(NavContext nav)
    {
        for (int c = 0; c < nav.Passable.Length; c++)
        {
            if (nav.Passable[c] != 0) yield return c;
        }
    }

    /// <summary>掩码外、紧贴掩码的第一个 tile 的角格；掩码已满返回 -1。</summary>
    public static int CellOutside(NavContext nav, byte[] mask)
    {
        int perTile = nav.Hpa!.NavCellsPerTile, tilesPerSide = nav.Hpa.NavTilesPerSide;
        for (int t = 0; t < mask.Length; t++)
        {
            if (mask[t] != 0) continue;
            int tx = t % tilesPerSide, ty = t / tilesPerSide;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = tx + dx, ny = ty + dy;
                    if (nx >= 0 && ny >= 0 && nx < tilesPerSide && ny < tilesPerSide && mask[ny * tilesPerSide + nx] != 0)
                    {
                        return ty * perTile * nav.NavCellsPerSide + tx * perTile;
                    }
                }
            }
        }

        return -1;
    }
}
