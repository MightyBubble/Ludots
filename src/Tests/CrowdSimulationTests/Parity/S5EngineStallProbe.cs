using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;

namespace CrowdSimulationTests.Parity;

/// <summary>引擎形态探针(非阻塞停摆口径):Step 停摆 → 让出墙钟 → 答复落地后应续走。</summary>
public sealed class S5EngineStallProbe
{
    [Test]
    public void NonBlocking_StallRecovers()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(10), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        // 引擎口径:停摆让出,不阻塞
        session.BlockOnDueReplies = false;

        CrowdDeployment.SpawnAt(session, Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(480000), Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(560000), 100, 1, 0, 0);
        session.Commands.Submit(session, new JsonObject
        {
            ["type"] = "order", ["player"] = 1, ["xCm"] = 1200000, ["yCm"] = 800000, ["shape"] = "box",
        }, CrowdSimCommands.Exec);

        int ran = 0, stalls = 0;
        var sw = Stopwatch.StartNew();
        while (ran < 300 && sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (session.Step(out _)) ran++;
            else { stalls++; Thread.Sleep(2); }
        }

        TestContext.Out.WriteLine($"ran={ran} stalls={stalls} elapsed={sw.ElapsedMilliseconds}ms");
        Assert.That(ran, Is.EqualTo(300), $"停摆必须在答复落地后恢复(实际只推进 {ran} tick)");
    }
}
