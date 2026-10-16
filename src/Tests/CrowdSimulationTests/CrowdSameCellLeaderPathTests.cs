using System;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 起终点落在同一导航格时，漏斗只留下一个世界点。领队仍要能接住这条路径。
/// </summary>
public sealed class CrowdSameCellLeaderPathTests
{
    [Test]
    public void OrderIntoOwnCell_LeaderPathKeepsTheCollapsedPoint()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7", lineOfSight: false);
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        TestDefaults.EnableFog(session);
        session.BlockOnDueReplies = true;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));

        var pos = session.World.Get<WorldPositionCm>(session.Units.EntityAt(0)).Value;
        int navCell = session.Config.NavCellSizeCm;
        int side = session.Config.NavCellsPerSide;
        int cell = CrowdSimulationSpace.CellAt(pos.X, pos.Y, navCell, side);
        int cx = cell % side;
        int cy = cell / side;
        int centerX = cx * navCell + navCell / 2;
        int centerY = cy * navCell + navCell / 2;
        var center = new Fix64Vec2(Fix64.FromInt(centerX), Fix64.FromInt(centerY));
        Assert.That(CrowdSimulationSpace.CellAt(center.X, center.Y, navCell, side), Is.EqualTo(cell));

        int n = session.Units.CopyOwnHandles(1, session.MemberCapture);
        var members = new uint[n];
        Array.Copy(session.MemberCapture, members, n);
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = 1,
            XCm = centerX,
            YCm = centerY,
            ShapeId = "box",
            Members = members,
        }, CrowdSimCommands.Exec);

        CrowdLeader? leader = null;
        for (int i = 0; i < 8 && leader == null; i++)
        {
            session.Advance(1);
            var groups = session.Groups.Groups;
            for (int g = 0; g < groups.Count; g++)
            {
                if (groups[g]?.Leader is { } found)
                {
                    leader = found;
                    break;
                }
            }
        }

        Assert.That(leader, Is.Not.Null);
        Assert.That(leader!.PathCount, Is.EqualTo(2));
        Assert.That(leader.PathAt(1).X, Is.EqualTo(center.X));
        Assert.That(leader.PathAt(1).Y, Is.EqualTo(center.Y));
    }
}
