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
        session.PublishTelemetryFrame(stalled: false, tickMs: 1);
        Assert.That(session.Telemetry.ContactsSum, Is.EqualTo(session.Movement.ContactSum));

        long rebakes = session.Telemetry.Rebakes;
        session.Commands.Submit(
            session,
            JsonNode.Parse("""{"type":"placeStructure","template":"s7barrier","xCm":620000,"yCm":620000,"sizeCm":4000}""")!,
            CrowdSimCommands.Exec);
        session.Advance(8);
        Assert.That(session.Telemetry.Rebakes, Is.GreaterThan(rebakes), "重烘报告发布时应 MarkRebake");

        service.TestDelayPerJob = TimeSpan.FromMilliseconds(400);
        session.BlockOnDueReplies = false;
        long waits = session.Telemetry.PathWaits;
        session.Commands.Submit(
            session,
            new JsonObject
            {
                ["type"] = "order",
                ["player"] = 1,
                ["xCm"] = 1200000,
                ["yCm"] = 800000,
                ["shape"] = "box",
            },
            CrowdSimCommands.Exec);
        int guard = 0;
        while (session.Telemetry.PathWaits == waits)
        {
            if (guard++ > 40) Assert.Fail("路径延迟下应出现等路停摆");
            session.Step(out _);
        }

        service.TestDelayPerJob = TimeSpan.Zero;
        Assert.That(session.Telemetry.PathWaits, Is.GreaterThan(waits));
    }
}
