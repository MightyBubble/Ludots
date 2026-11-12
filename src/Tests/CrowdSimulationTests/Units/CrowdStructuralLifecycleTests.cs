using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Units;

public sealed class CrowdStructuralLifecycleTests
{
    [Test]
    public void Kill_DefersDestroyUntilPlayback_DenseMatchesEntities()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 2, 1, 0, 0), Is.EqualTo(2));
        const int cx = 480000;
        const int cy = 560000;
        session.World.Set(session.Units.EntityAt(0), new WorldPositionCm { Value = new Fix64Vec2(Fix64.FromInt(cx), Fix64.FromInt(cy)) });
        session.World.Set(session.Units.EntityAt(1), new WorldPositionCm { Value = new Fix64Vec2(Fix64.FromInt(cx + 4000), Fix64.FromInt(cy)) });
        Entity victim = session.Units.EntityAt(0);
        uint victimHandle = session.Units.HandleAt(0);

        var kill = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = cx,
            YCm = cy,
            RadiusCm = 1000,
            RingScale = Fix64.FromInt(2),
            Impulse = Fix64.Zero,
        };

        CrowdSimCommands.Exec(session, kill);
        Assert.That(session.Units.Count, Is.EqualTo(1), "稠密表应先于实体销毁更新");
        Assert.That(session.Units.TryResolveHandle(victimHandle, out _), Is.False);
        Assert.That(session.World.IsAlive(victim), Is.True, "结构同步点前实体应仍存活");

        session.Units.PlaybackStructuralChanges();
        Assert.That(session.World.IsAlive(victim), Is.False);
    }

    [Test]
    public void Step_DoesNotDestroyDuringMotionSubSteps()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new Ludots.Core.CrowdSimulation.Nav.Pathing.PathQueryService(
            session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        TestDefaults.EnableFog(session);
        session.BlockOnDueReplies = true;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 4, 1, 0, 0), Is.EqualTo(4));
        for (int i = 0; i < session.Units.Count; i++)
        {
            session.World.Set(session.Units.EntityAt(i), new WorldPositionCm
            {
                Value = new Fix64Vec2(Fix64.FromInt(480000 + i * 1500), Fix64.FromInt(560000)),
            });
        }

        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(0, new CrowdSimCommand
            {
                Kind = CrowdSimCommandKind.Kill,
                XCm = 480000,
                YCm = 560000,
                RadiusCm = 2000,
                RingScale = Fix64.FromInt(2),
                Impulse = Fix64.Zero,
            }),
        });

        Assert.That(session.Step(out _), Is.True);
        Assert.That(session.Units.Count, Is.EqualTo(2));
        for (int i = 0; i < session.Units.Count; i++)
        {
            Assert.That(session.World.IsAlive(session.Units.EntityAt(i)), Is.True);
        }
    }
}
