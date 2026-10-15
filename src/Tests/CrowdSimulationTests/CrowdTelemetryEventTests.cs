using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 接触、重烘、等路、迷雾刷新在事件点推进遥测,不是恒 0。
/// </summary>
public sealed class CrowdTelemetryEventTests
{
    [Test]
    public void Counters_IncrementAtContactRebakePathWaitAndFogRefresh()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, session.Config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;

        Assert.That(session.Step(out _), Is.True);
        Assert.That(session.Telemetry.FogRefreshes, Is.GreaterThan(0), "迷雾刷新应在 Fog.Update 计入");

        int spawned = CrowdDeployment.SpawnAt(
            session, Fix64.FromInt(480000), Fix64.FromInt(560000), 40, 1, 0, 0);
        Assert.That(spawned, Is.EqualTo(40));
        session.Advance(12);
        Assert.That(session.Movement!.ContactSum, Is.GreaterThan(0), "挤在一起的单位应产生接触");
        int firstContacts = session.Movement.ContactSum;
        session.PublishTelemetryFrame(stalled: false, tickMs: 1);
        Assert.That(session.Telemetry.Contacts, Is.EqualTo(firstContacts));
        session.Movement.ContactSum = firstContacts + 5;
        session.PublishTelemetryFrame(stalled: false, tickMs: 1);
        Assert.That(session.Telemetry.Contacts, Is.EqualTo(firstContacts + 5), "Contacts 是本 tick 读数,第二次发布应覆盖而不是累加");

        long rebakes = session.Telemetry.Rebakes;
        session.Commands.Submit(
            session,
            JsonNode.Parse("""{"type":"placeStructure","template":"s7barrier","xCm":620000,"yCm":620000,"sizeCm":4000}""")!,
            CrowdSimCommands.Exec);
        session.Advance(8);
        Assert.That(session.Telemetry.Rebakes, Is.GreaterThan(rebakes), "重烘报告发布时应 MarkRebake");

        long waits = session.Telemetry.PathWaits;
        // 工人取出任务时才读延迟。到期帧是推进 latency 之后的下一步，这两步本身还不会等到点。
        service.TestDelayPerJob = TimeSpan.FromMilliseconds(400);
        session.Commands.Submit(session, MoveOrder(session, 1, 1200000, 800000), CrowdSimCommands.Exec);
        session.Advance(session.Config.Planning.LatencyTicks);
        int membersBefore = session.Units.Count;
        int hist = session.Telemetry.Idle + session.Telemetry.Moving + session.Telemetry.Arrived
            + session.Telemetry.Unreachable + session.Telemetry.Traversing;
        Assert.That(hist, Is.EqualTo(membersBefore));
        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(session.TickCount, CrowdSimCommand.Parse(
                JsonNode.Parse("""{"type":"spawnAt","player":1,"xCm":480000,"yCm":560000,"count":4,"unitType":0,"rIdx":0}""")!)),
        });
        session.BlockOnDueReplies = false;
        Assert.That(session.Step(out _), Is.False, "到期规划在路径延迟下应停摆");

        service.TestDelayPerJob = TimeSpan.Zero;
        Assert.That(session.Telemetry.PathWaits, Is.GreaterThan(waits));
        Assert.That(session.Telemetry.StallTicks, Is.EqualTo(session.Telemetry.PathWaits), "无头 Step 的停摆与等路同一次计数");
        Assert.That(session.Units.Count, Is.EqualTo(membersBefore + 4));
        int histAfter = session.Telemetry.Idle + session.Telemetry.Moving + session.Telemetry.Arrived
            + session.Telemetry.Unreachable + session.Telemetry.Traversing;
        Assert.That(histAfter, Is.EqualTo(session.Units.Count), "等路停摆前 Flush 增加的成员要重数进直方");
    }

    private static JsonObject MoveOrder(CrowdSimSession session, int player, int xCm, int yCm)
    {
        var handles = new uint[session.Units.Count];
        int count = session.Units.CopyOwnHandles(player, handles);
        var members = new JsonArray();
        for (int i = 0; i < count; i++) members.Add((long)handles[i]);
        return new JsonObject
        {
            ["type"] = "order",
            ["player"] = player,
            ["xCm"] = xCm,
            ["yCm"] = yCm,
            ["shape"] = "box",
            ["members"] = members,
        };
    }
}
