using System;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 一条指令拆成两组后，只改派其中一组。空组释放后，旧链接不能再被重规划按空槽解引用。
/// </summary>
public sealed class CrowdStaleOrderLinkTests
{
    [Test]
    public void PartialReorder_DropsReleasedGroupBeforeRebakePlan()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7", lineOfSight: false);
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        TestDefaults.EnableFog(session);
        session.BlockOnDueReplies = true;

        var radii = CrowdDeployment.DistinctRadiusClasses(session.Config);
        (int Type, int RIdx) first = default, second = default;
        bool haveFirst = false, haveSecond = false;
        for (int type = 0; type < session.Config.UnitTypes.Count && !haveSecond; type++)
        {
            int layer = session.Config.UnitTypes[type].AgentTypeIndex;
            for (int rIdx = 0; rIdx < radii.Count; rIdx++)
            {
                if (!HasProfile(session, layer, radii[rIdx])) continue;
                if (!session.NavByLayerRadius.ContainsKey((layer, rIdx))) continue;
                if (!haveFirst)
                {
                    first = (type, rIdx);
                    haveFirst = true;
                    continue;
                }

                int firstLayer = session.Config.UnitTypes[first.Type].AgentTypeIndex;
                if (layer == firstLayer && rIdx == first.RIdx) continue;
                second = (type, rIdx);
                haveSecond = true;
                break;
            }
        }

        Assert.That(haveSecond, Is.True, "需要两个 (移动类型 × 半径级) 才能把一条指令拆成两组。");

        var basis = session.Config.Deploy.Bases[0];
        int player = basis.PlayerId;
        int cell = session.Config.NavCellSizeCm;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(basis.XCm), Fix64.FromInt(basis.YCm), 1, player, first.Type, first.RIdx), Is.EqualTo(1));
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(basis.XCm + cell * 4), Fix64.FromInt(basis.YCm), 1, player, second.Type, second.RIdx), Is.EqualTo(1));

        var nav = session.NavFor(session.Config.UnitTypes[first.Type].AgentTypeIndex, first.RIdx);
        int side = session.Config.NavCellsPerSide;
        int far = CrowdDeployment.NearestPassable(nav, side * side - 1);
        int near = CrowdDeployment.NearestPassable(nav, 0);
        Assert.That(far, Is.GreaterThanOrEqualTo(0));
        Assert.That(near, Is.GreaterThanOrEqualTo(0));
        Order(session, player, CellCenter(far, side, cell), session.Units.HandleAt(0), session.Units.HandleAt(1));

        Assert.That(session.Orders.List, Has.Count.EqualTo(1));
        var shared = session.Orders.List[0];
        Assert.That(shared.Groups, Has.Count.EqualTo(2));
        int released = shared.Groups[0].GroupId;
        int stayed = shared.Groups[1].GroupId;
        Assert.That(session.Groups.TryGet(released, out var releasedGroup), Is.True);
        Assert.That(releasedGroup.Count, Is.EqualTo(1));

        Order(session, player, CellCenter(near, side, cell), session.Units.HandleAt(0));

        bool stillOnShared = session.Groups.TryGet(released, out var reused) && reused.OrderId == shared.Id;
        Assert.That(stillOnShared, Is.False, "被抽空的组不能继续挂在原指令上。");
        Assert.That(session.Groups.TryGet(stayed, out var stayedGroup), Is.True);
        Assert.That(stayedGroup.OrderId, Is.EqualTo(shared.Id));
        AssertNoStaleLinks(session);
        session.Advance(4);

        int x = far % side * cell + cell / 2;
        int y = far / side * cell + cell / 2;
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = "building",
            XCm = x,
            YCm = y,
            SizeCm = 14000,
        }, CrowdSimCommands.Exec);
        Assert.That(
            CrowdDeployment.SpawnAt(session, Fix64.FromInt(basis.XCm), Fix64.FromInt(basis.YCm + cell * 4), 1, player, first.Type, first.RIdx),
            Is.EqualTo(1));
        AssertNoStaleLinks(session);

        int lone = session.Units.Count - 1;
        var loneEntity = session.Units.EntityAt(lone);
        int loneGroup = session.World.Get<CrowdSimulationUnitState>(loneEntity).GroupId;
        var lonePos = session.World.Get<WorldPositionCm>(loneEntity).Value;
        Order(session, player, CellCenter(near, side, cell), session.Units.HandleAt(lone));
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = (int)lonePos.X.ToLong(),
            YCm = (int)lonePos.Y.ToLong(),
            RadiusCm = cell,
            RingScale = Fix64.FromInt(2),
            Impulse = Fix64.Zero,
        }, CrowdSimCommands.Exec);
        Assert.That(session.Groups.TryGet(loneGroup, out _), Is.False, "单独成组的单位被击杀后，组槽应为空。");
        AssertNoStaleLinks(session);

        Assert.That(session.Orders.List, Does.Contain(shared));
        shared.Groups.Add(session.Orders.RentLink(loneGroup, 0));
        Assert.DoesNotThrow(() => session.Planner!.Plan(session.Orders.List, session.TickCount));
    }

    private static void AssertNoStaleLinks(CrowdSimSession session)
    {
        foreach (var order in session.Orders.List)
        {
            foreach (var link in order.Groups)
            {
                Assert.That(session.Groups.TryGet(link.GroupId, out var group), Is.True, $"指令 {order.Id} 仍指向已释放的组 {link.GroupId}。");
                Assert.That(group.OrderId, Is.EqualTo(order.Id));
                Assert.That(group.Count, Is.GreaterThan(0));
            }
        }
    }

    private static bool HasProfile(CrowdSimSession session, int layer, int radiusCm)
    {
        var profiles = session.Config.Profiles;
        for (int i = 0; i < profiles.Count; i++)
        {
            if (profiles[i].AgentTypeIndex == layer && (int)profiles[i].RadiusCm.ToInt() == radiusCm) return true;
        }

        return false;
    }

    private static (int X, int Y) CellCenter(int cellIndex, int side, int cell)
        => (cellIndex % side * cell + cell / 2, cellIndex / side * cell + cell / 2);

    private static void Order(CrowdSimSession session, int player, (int X, int Y) goal, params uint[] members)
    {
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = player,
            XCm = goal.X,
            YCm = goal.Y,
            ShapeId = "box",
            Members = members,
        }, CrowdSimCommands.Exec);
    }
}
