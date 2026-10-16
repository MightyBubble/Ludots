using System;
using System.Linq;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.Mathematics.FixedPoint;
using CrowdSimulationTests.Parity;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public sealed class CrowdNavQueryMemoTests
{
    [Test]
    public void StitchMemo_PerNavContext_DoesNotGrowAcrossRepeatedAssemble()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        var nav = session.Navs.Values.First();
        int cellsPerTile = runtime.Hpa.NavCellsPerTile;
        int tilesPerSide = nav.NavCellsPerSide / cellsPerTile;
        int maxPairs = 0;
        for (int i = 0; i < 24; i++)
        {
            NavMeshAssembler.Assemble(nav, nav.Tiles!, nav.Cost, nav.NavCellsPerSide, cellsPerTile);
            maxPairs = Math.Max(maxPairs, nav.StitchMemo.Count);
        }

        Assert.That(maxPairs, Is.LessThanOrEqualTo(tilesPerSide * tilesPerSide * 8));
        nav.ClearStitchMemo();
        Assert.That(nav.StitchMemo.Count, Is.Zero);
    }

    [Test]
    public void TileCostMemo_StaysBoundedAndFresh_AcrossRebakesAndCostChanges()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        var nav = session.Navs.Values.First();
        int cellsPerTile = runtime.Hpa.NavCellsPerTile;
        int tilesPerSide = nav.NavCellsPerSide / cellsPerTile;
        int tileCount = tilesPerSide * tilesPerSide;
        var rect = (0, 0, runtime.NavCellsPerSide - 1, runtime.NavCellsPerSide - 1);
        var kept = new TilePathQuery.Scratch();
        kept.Prepare(cellsPerTile * cellsPerTile);
        var (start, goal) = FindConnectedPair(nav, runtime, kept);

        for (int i = 0; i < 12; i++)
        {
            CrowdNavRebake.RebakeNav(nav, session.RebakeSources!, session.Structures!, rect, session.NavTileCache!);
            Assert.That(TilePathQuery.FindTilePath(nav, runtime, start, goal, layers: false, kept), Is.Not.Null);
            Assert.That(kept.BaseLayerCostMemo.Count, Is.LessThanOrEqualTo(tileCount), $"第 {i} 次重烘后代价备忘超过 tile 数");
        }

        Fix64 before = TilePathQuery.FindTilePath(nav, runtime, start, goal, layers: false, kept)!.Cost;
        for (int c = 0; c < nav.Cost.Length; c++)
        {
            if (nav.Cost[c] > Fix64.Zero) nav.Cost[c] *= Fix64.FromInt(3);
        }

        nav.Version++;
        Fix64 afterKept = TilePathQuery.FindTilePath(nav, runtime, start, goal, layers: false, kept)!.Cost;
        var fresh = new TilePathQuery.Scratch();
        fresh.Prepare(cellsPerTile * cellsPerTile);
        Fix64 afterFresh = TilePathQuery.FindTilePath(nav, runtime, start, goal, layers: false, fresh)!.Cost;
        Assert.That(afterKept, Is.EqualTo(afterFresh), "导航版本变了,备忘仍给出旧代价");
        Assert.That(afterKept, Is.Not.EqualTo(before));

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8; i++)
        {
            TilePathQuery.FindTilePath(nav, runtime, start, goal, layers: false, kept);
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - allocBefore, Is.Zero, "稳态备忘命中不应分配");
    }

    private static (int Start, int Goal) FindConnectedPair(NavContext nav, Ludots.Core.CrowdSimulation.Config.CrowdSimulationRuntimeConfig runtime, TilePathQuery.Scratch scratch)
    {
        int start = nav.Cells[0];
        for (int i = nav.CellCount - 1; i > 0; i--)
        {
            int goal = nav.Cells[i];
            if (TilePathQuery.FindTilePath(nav, runtime, start, goal, layers: false, scratch) is { PolyCount: > 1 }) return (start, goal);
        }

        throw new InvalidOperationException("s7 真相导航找不到跨多边形的连通格对。");
    }
}
