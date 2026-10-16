using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

public sealed class CrowdDenseRemovalSwapCarryTests
{
    [Test]
    public void KillCommand_SwapRemove_CarriesSeparationAndCalmToNewDenseIndex()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new Ludots.Core.CrowdSimulation.Nav.Pathing.PathQueryService(
            session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        var kernel = CrowdMovementKernel.Create(session);
        session.EnableMovement(kernel, new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        foreach (int xCm in new[] { 480000, 500000, 520000 })
        {
            Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(xCm), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        }

        kernel.EnsureCapacity(session.Units.Count);
        uint headHandle = session.Units.HandleAt(0);
        uint tailHandle = session.Units.HandleAt(2);
        kernel.Separation[0] = new Fix64Vec2(Fix64.FromInt(5), Fix64.FromInt(6));
        kernel.Calm[0] = 1;
        kernel.Separation[2] = new Fix64Vec2(Fix64.FromInt(11), Fix64.FromInt(-7));
        kernel.Calm[2] = 3;

        var victim = session.World.Get<WorldPositionCm>(session.Units.EntityAt(1)).Value;
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = (int)victim.X.ToLong(),
            YCm = (int)victim.Y.ToLong(),
            RadiusCm = 100,
            RingScale = Fix64.FromInt(2),
            Impulse = Fix64.Zero,
        }, CrowdSimCommands.Exec);

        Assert.That(session.Units.Count, Is.EqualTo(2));
        Assert.That(session.Units.HandleAt(0), Is.EqualTo(headHandle));
        Assert.That(session.Units.HandleAt(1), Is.EqualTo(tailHandle), "末行单位应被换到被删的稠密位");
        Assert.That(kernel.Separation[1], Is.EqualTo(new Fix64Vec2(Fix64.FromInt(11), Fix64.FromInt(-7))), "分离量没跟着末行单位搬");
        Assert.That(kernel.Calm[1], Is.EqualTo((byte)3), "静动标记没跟着末行单位搬");
        Assert.That(kernel.Separation[0], Is.EqualTo(new Fix64Vec2(Fix64.FromInt(5), Fix64.FromInt(6))));
        Assert.That(kernel.Calm[0], Is.EqualTo((byte)1));
        Assert.That(kernel.Separation[2], Is.EqualTo(Fix64Vec2.Zero));
        Assert.That(kernel.Calm[2], Is.EqualTo((byte)0));
    }
}
