using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S5-a 冒烟:真图真导航,每玩家生成一簇,下移动令,推进 300 tick——
/// 单位必须朝目标方向位移、无服务故障、校验码持续推进(停摆会卡 Advance)。
/// </summary>
public sealed class S5MovementSmokeTests
{
    [Test]
    public void Order_MovesUnitsTowardTarget()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(10));
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;

        CrowdDeployment.SpawnAt(session, Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(480000), Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(560000), 200, 1, 0, 0);
        int total = session.Units.Count;
        Assert.That(total, Is.EqualTo(200));

        // 开局质心
        (double X, double Y) Centroid()
        {
            double sx = 0, sy = 0;
            for (int i = 0; i < session.Units.Count; i++)
            {
                var p = session.World.Get<Ludots.Core.Components.WorldPositionCm>(session.Units.EntityAt(i)).Value;
                sx += p.X.ToDouble();
                sy += p.Y.ToDouble();
            }

            return (sx / total, sy / total);
        }

        var before = Centroid();
        session.Commands.Submit(session, new JsonObject
        {
            ["type"] = "order",
            ["player"] = 1,
            ["xCm"] = 800000,
            ["yCm"] = 800000,
            ["shape"] = "box",
        }, CrowdSimCommands.Exec);

        // 路径答复落地后读组的(可能可达替代的)目标格
        session.Advance(5);
        int goalCell = -1;
        for (int i = 0; i < session.Groups.Groups.Count; i++)
        {
            if (session.Groups.Groups[i] is { OrderId: > 0 } g) goalCell = g.Goal;
        }

        Assert.That(goalCell, Is.GreaterThanOrEqualTo(0), "指令答复后组必须有目标格");
        int n = session.Config.NavCellCount;
        double goalXCm = (goalCell % n + 0.5) * session.Config.NavCellSizeCm;
        double goalYCm = (goalCell / n + 0.5) * session.Config.NavCellSizeCm;

        var hashes = new List<string>();
        session.Advance(295, hashes);
        Assert.That(hashes, Has.Count.EqualTo(295), "295 tick 必须全部推进(停摆 = 路径服务卡死)");

        double DistBefore() => Math.Sqrt(Math.Pow(before.X - goalXCm, 2) + Math.Pow(before.Y - goalYCm, 2));
        var after = Centroid();
        double distAfter = Math.Sqrt(Math.Pow(after.X - goalXCm, 2) + Math.Pow(after.Y - goalYCm, 2));
        int arrived = 0, moving = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var st = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(i));
            if (st.State == (byte)CrowdUnitState.Arrived) arrived++;
            else if (st.State == (byte)CrowdUnitState.Moving) moving++;
        }

        TestContext.Out.WriteLine($"goal cell ({goalXCm:F0},{goalYCm:F0})cm; dist {DistBefore():F0} -> {distAfter:F0} cm; arrived={arrived} moving={moving}");
        Assert.That(distAfter, Is.LessThan(DistBefore() - 60000), "质心必须显著接近目标格");
        Assert.That(arrived + moving, Is.GreaterThan(0), "单位必须在移动或已到达");
    }

    [Test]
    public void Order_Replay_BitIdentical()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(10));
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;

        var script = new List<CrowdCommand>
        {
            new(0, new JsonObject { ["type"] = "spawnAt", ["player"] = 1, ["xCm"] = 480000, ["yCm"] = 560000, ["count"] = 120, ["unitType"] = 0, ["rIdx"] = 0 }),
            new(10, new JsonObject { ["type"] = "order", ["player"] = 1, ["xCm"] = 800000, ["yCm"] = 800000, ["shape"] = "box" }),
        };
        session.Commands.Schedule(script);
        var live = new List<string>();
        session.Advance(200, live);

        var (runtime2, replay) = S4DeployTruthTests.BuildSession("s1337");
        using var service2 = new PathQueryService(replay.Navs, runtime2, 1, TimeSpan.FromSeconds(10));
        replay.EnableMovement(CrowdMovementKernel.Create(replay), new CrowdSimPlanner(replay, service2));
        replay.BlockOnDueReplies = true;
        replay.Commands.Schedule(script.Select(c => new CrowdCommand(c.Tick, (JsonNode)c.Payload.DeepClone())).ToArray());
        var reHashes = new List<string>();
        replay.Advance(200, reHashes);

        Assert.That(reHashes, Is.EqualTo(live), "移动脚本的回放必须与首次运行逐帧一致");
    }
}
