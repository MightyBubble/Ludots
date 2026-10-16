using System;
using System.Linq;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
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
    public void TileCostMemo_InvalidatesPerNav_AfterRepeatedRebakes()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        var nav = session.Navs.Values.First();
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        int tileCount = nav.NavCellsPerSide / runtime.Hpa.NavCellsPerTile;
        tileCount *= tileCount;
        var rect = (0, 0, runtime.NavCellsPerSide - 1, runtime.NavCellsPerSide - 1);

        for (int i = 0; i < 12; i++)
        {
            for (int t = 0; t < tileCount; t++)
            {
                service.SeedTileCostMemo(nav.Id, t, 4);
            }

            Assert.That(service.CountTileCostMemos(nav.Id), Is.EqualTo(tileCount));
            CrowdNavRebake.RebakeNav(nav, session.RebakeSources!, session.Structures!, rect, session.NavTileCache!);
            service.InvalidateTileCostMemos(nav.Id);
            Assert.That(service.CountTileCostMemos(nav.Id), Is.Zero);
        }
    }
}
