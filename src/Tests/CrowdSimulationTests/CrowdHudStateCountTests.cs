using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 单位状态计数在 scatter 时顺带累加,与组件直读一致。
/// </summary>
public sealed class CrowdHudStateCountTests
{
    [Test]
    public void Scatter_StateCountsMatchComponentScan()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, session.Config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        int spawned = CrowdDeployment.SpawnAt(
            session, Fix64.FromInt(480000), Fix64.FromInt(560000), 24, 1, 0, 0);
        Assert.That(spawned, Is.EqualTo(24));
        session.Advance(6);

        int idle = 0, moving = 0, arrived = 0, unreachable = 0, jumping = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            switch (session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(i)).State)
            {
                case (byte)CrowdUnitState.Idle: idle++; break;
                case (byte)CrowdUnitState.Moving: moving++; break;
                case (byte)CrowdUnitState.Arrived: arrived++; break;
                case (byte)CrowdUnitState.Unreachable: unreachable++; break;
                case (byte)CrowdUnitState.Traversing: jumping++; break;
            }
        }

        var telemetry = session.Telemetry;
        Assert.That(telemetry.Idle, Is.EqualTo(idle));
        Assert.That(telemetry.Moving, Is.EqualTo(moving));
        Assert.That(telemetry.Arrived, Is.EqualTo(arrived));
        Assert.That(telemetry.Unreachable, Is.EqualTo(unreachable));
        Assert.That(telemetry.Traversing, Is.EqualTo(jumping));
        Assert.That(idle + moving + arrived + unreachable + jumping, Is.EqualTo(session.Units.Count));
    }
}
