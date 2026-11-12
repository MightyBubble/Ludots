using System;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>结构变更只唤醒触及范围内的休眠单位；跨图远端单位保持休眠（厘米距离平方不能溢出定点范围）。</summary>
public sealed class CrowdStructureWakeReachTests
{
    [Test]
    public void WakeNear_FarAcrossMap_KeepsSleepers()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 8, 1, 0, 0), Is.EqualTo(8));
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(1120000), Fix64.FromInt(1040000), 8, 2, 0, 0), Is.EqualTo(8));
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(1477500), Fix64.FromInt(1124500), 8, 3, 0, 0), Is.EqualTo(8));
        Assert.That(session.Step(out _), Is.True);

        var kernel = session.Movement!;
        int cellCm = runtime.NavCellSizeCm;
        var near = kernel.Positions[0];
        int cx = (int)(near.X.ToDouble() / cellCm), cy = (int)(near.Y.ToDouble() / cellCm);
        int n = session.Units.Count;
        for (int i = 0; i < n; i++) kernel.Calm[i] = 1;

        CrowdStructureOps.WakeNear(session, (cx, cy, cx + 1, cy + 1));

        int nearWoken = 0, farWoken = 0, farCount = 0;
        for (int i = 0; i < n; i++)
        {
            var p = kernel.Positions[i];
            double d = Math.Max(Math.Abs(p.X.ToDouble() - near.X.ToDouble()), Math.Abs(p.Y.ToDouble() - near.Y.ToDouble()));
            if (d < 100_000)
            {
                if (kernel.Calm[i] == 0) nearWoken++;
            }
            else
            {
                farCount++;
                if (kernel.Calm[i] == 0) farWoken++;
            }
        }

        Assert.That(nearWoken, Is.GreaterThan(0), "变更格旁的单位应被唤醒");
        Assert.That(farCount, Is.EqualTo(16));
        Assert.That(farWoken, Is.EqualTo(0), "数公里外的休眠单位不应被结构变更唤醒");
    }
}
