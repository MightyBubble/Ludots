using System;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>被后一次刷新取代的答复要取走并归还，在途数不随取代次数上涨。</summary>
public sealed class CrowdRefreshReplyTests
{
    [Test]
    public void SupersededRefreshes_DoNotGrowOutstandingReplies()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        TestDefaults.EnableFog(session);

        NavContext nav = null!;
        foreach (var candidate in session.Navs.Values)
        {
            nav = candidate;
            break;
        }

        int goal = -1;
        for (int i = 0; i < nav.Passable.Length; i++)
        {
            if (nav.Passable[i] != 0)
            {
                goal = i;
                break;
            }
        }

        Assert.That(goal, Is.GreaterThanOrEqualTo(0));
        int navCellsPerTile = session.Config.Hpa.NavCellsPerTile;
        int navTilesPerSide = nav.NavCellsPerSide / navCellsPerTile;
        int tileX = goal % nav.NavCellsPerSide / navCellsPerTile;
        int tileY = goal / nav.NavCellsPerSide / navCellsPerTile;
        var mask = new byte[navTilesPerSide * navTilesPerSide];
        mask[tileY * navTilesPerSide + tileX] = 1;

        var group = session.Groups.Alloc(0, 0, 0, nav.Id);
        group.Count = 1;
        group.Goal = goal;
        group.Planning = false;
        group.Flow = new FlowField
        {
            Integ = Array.Empty<Fix64>(),
            Len = Array.Empty<Fix64>(),
            Wp = Array.Empty<int>(),
            Mask = mask,
        };

        var planner = session.Planner!;
        const int rounds = 6;
        for (int i = 0; i < rounds; i++)
        {
            planner.RefreshFlow(group);
            planner.ProcessRefreshes(i);
        }

        Assert.That(planner.ApplyDue(100, block: true), Is.True);
        Assert.That(service.OutstandingReplyCount, Is.EqualTo(0));
    }
}
