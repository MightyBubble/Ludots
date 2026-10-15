using System.Linq;
using CrowdSimulationTests.Uat;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>领队阻挡判定只认新阻挡：规划时路线已经擦过的墙格不算受阻，路线上新放的建筑仍算。</summary>
public sealed class CrowdLeaderWallBaselineTests
{
    [Test]
    public void RouteThroughWallCorners_IsNotBlockedUntilANewBuildingLandsOnIt()
    {
        using var scene = CrowdUatKit.Scene();
        var s = scene.Session;
        int w = s.Config.NavCellsPerSide * s.Config.NavCellSizeCm;
        CrowdUatKit.SpawnAt(s, Fix64.FromInt(w * 19 / 100), Fix64.FromInt(w / 2), 60, rIdx: 0);
        CrowdUatKit.Order(s, Fix64.FromInt(w * 81 / 100), Fix64.FromInt(w / 2));
        CrowdNavGroupSet.Group? g = null;
        for (int t = 0; t < 130 && g == null; t++)
        {
            CrowdUatKit.Step(s);
            g = s.Groups.Groups.FirstOrDefault(x => x != null && x.Count > 0 && x.Leader != null && !x.Planning);
        }

        Assert.That(g, Is.Not.Null, "指令未产生领队");
        var leader = g!.Leader!;
        var truth = s.ResolveNavContext(g.BodyNavId);
        Assert.That(CrossedWallSamples(s, leader, truth), Is.GreaterThan(0), "场景无效：路线没有擦过任何墙格");
        Assert.That(CrowdSimPlanner.LeaderPathBlocked(leader, truth, s), Is.False);
        Assert.That(CrowdSimPlanner.LeaderPathBlocked(leader, s.ResolveNavContext(g.NavId), s), Is.False);

        int size = 5 * s.Config.NavCellSizeCm;
        bool placed = false;
        for (int k = leader.Seg; k < leader.PathCount && !placed; k++)
        {
            var at = leader.PathAt(k - 1) + (leader.PathAt(k) - leader.PathAt(k - 1)) / 2;
            int before = s.Structures!.EntityIds.Count;
            s.Commands.Submit<object?>(s, new CrowdSimCommand
            {
                Kind = CrowdSimCommandKind.PlaceStructure,
                TemplateId = "building",
                XCm = at.X.ToInt(),
                YCm = at.Y.ToInt(),
                SizeCm = size,
            }, CrowdSimCommands.Exec);
            placed = s.Structures.EntityIds.Count > before;
        }

        Assert.That(placed, Is.True, "场景无效：路线上没有能放建筑的位置");
        Assert.That(s.LastPlacementReject, Is.EqualTo(CrowdPlacementReject.None));
        Assert.That(CrowdSimPlanner.LeaderPathBlocked(leader, s.ResolveNavContext(g.BodyNavId), s), Is.True);
    }

    private static int CrossedWallSamples(CrowdSimSession s, CrowdLeader leader, NavContext nav)
    {
        int cellCm = s.Config.NavCellSizeCm, side = s.Config.NavCellsPerSide, hits = 0;
        Fix64 step = Fix64.FromInt(cellCm) / 2, e = Fix64.FromInt(cellCm) / 4;
        bool Bad(Fix64 x, Fix64 y)
        {
            int c = CrowdSimulationSpace.CellAt(x, y, cellCm, side);
            return nav.Passable[c] == 0 && nav.UpPass[c] == 0;
        }

        Fix64 ax = leader.XCm, ay = leader.YCm;
        for (int k = leader.Seg; k < leader.PathCount; k++)
        {
            var b = leader.PathAt(k);
            int m = (int)Fix64.Ceiling(CrowdFix.Hypot(b.X - ax, b.Y - ay) / step).ToLong();
            for (int i = 1; i <= m; i++)
            {
                Fix64 x = ax + (b.X - ax) * i / m, y = ay + (b.Y - ay) * i / m;
                if (Bad(x - e, y - e) && Bad(x + e, y - e) && Bad(x - e, y + e) && Bad(x + e, y + e)) hits++;
            }

            ax = b.X;
            ay = b.Y;
        }

        return hits;
    }
}
