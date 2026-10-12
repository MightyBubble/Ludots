using System;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 组级备忘的 byte 世代号在扩容当拍回绕时，新数组的 0 戳不得被当成命中。
/// </summary>
public sealed class GroupMemoEpochWrapTests
{
    [Test]
    public void GatherUnits_GroupGrowthAtEpoch255_DoesNotTreatFreshStampsAsHits()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(0, CrowdSimCommand.Parse((JsonObject)JsonNode.Parse("""{"type":"spawn","count":4}""")!)),
        });
        session.Advance(1);
        Assert.That(session.Units.Count, Is.GreaterThan(0));
        Assert.That(session.Groups.Groups.Count, Is.GreaterThan(0));

        CrowdMovementKernel kernel = session.Movement!;
        kernel.GatherUnits();
        CrowdNavGroupSet.Group existing = RequireGroup(session);
        CrowdOrder order = session.Orders.Create(existing.Player, CrowdOrderMode.Formation, "line", null, Fix64.Zero);
        existing.OrderId = order.Id;

        kernel.GroupMemoEpoch = byte.MaxValue;
        while (session.Groups.FreeCount > 0)
        {
            session.Groups.Alloc(existing.Player, existing.LayerIdx, existing.RIdx, existing.BodyNavId);
        }

        session.Groups.Alloc(existing.Player, existing.LayerIdx, existing.RIdx, existing.BodyNavId);
        kernel.GatherUnits();

        Assert.That(kernel.MemoOrder(existing.Id, existing), Is.SameAs(order),
            "扩容把世代号回绕到 0 时，全 0 的新戳不能当成已解析指令。");
        Assert.That(kernel.MemoNav(existing.Id, existing), Is.Not.Null,
            "同一回绕不能把导航备忘的空槽当成命中。");
    }

    private static CrowdNavGroupSet.Group RequireGroup(CrowdSimSession session)
    {
        for (int i = 0; i < session.Groups.Groups.Count; i++)
        {
            if (session.Groups.Groups[i] is { Count: > 0 } group)
            {
                return group;
            }
        }

        throw new AssertionException("spawn 后应有非空导航组。");
    }
}
