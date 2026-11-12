using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Nav;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>从地面不可走（桥下是水）的桥面格出发、带着桥面层号的走廊查询，能查到通往远处地面目标的路，且走廊覆盖起点所在 tile。</summary>
[TestFixture]
public sealed class DeckStartUatTests
{
    private const int MaxTested = 20, StartStride = 7, GoalStride = 97;

    [Test]
    public void CorridorFromWaterDeck_FindsRouteCoveringStartTile()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession(CrowdUatKit.DefaultSeed);
        var nav = session.NavFor(0, 0);
        int n = nav.NavCellsPerSide, s = nav.Hpa!.NavCellsPerTile, c = nav.Hpa.NavTilesPerSide;
        var scratch = new CorridorQuery.Scratch(s * s);
        var errors = new List<string>();
        int tested = 0, failed = 0;
        for (int cell = 0; cell < n * n && tested < MaxTested; cell += StartStride)
        {
            if (nav.UpPass[cell] == 0 || nav.Passable[cell] != 0) continue;
            int comp = nav.CompAt(cell, 1);
            if (comp < 0) continue;
            int goal = -1;
            for (int k = 0; k < nav.CellCount && goal < 0; k += GoalStride)
            {
                int g = nav.Cells[k], dx = g % n - cell % n, dy = g / n - cell / n;
                if (dx * dx + dy * dy > 4 * s * s && nav.CanReach(comp, nav.CompAt(g))) goal = g;
            }

            if (goal < 0) continue;
            tested++;
            var mask = new byte[c * c];
            var r = CorridorQuery.CorridorTo(nav, runtime, cell, goal, mask, 1, scratch);
            if (r.Points == null || mask[NavGridSteps.TileOf(cell, n, s, c)] == 0)
            {
                failed++;
                if (errors.Count < 3) errors.Add($"桥面格 {cell} → {goal}：走廊{(r.Points != null ? "未覆盖起点 tile" : "查询失败")}");
            }
        }

        TestContext.Out.WriteLine($"tested={tested} failed={failed}");
        Assert.That(tested, Is.GreaterThan(0), "地图上没有可测试的水上桥面格");
        Assert.That(errors, Is.Empty);
    }
}
