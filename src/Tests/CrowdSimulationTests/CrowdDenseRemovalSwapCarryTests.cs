using System.Reflection;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

public sealed class CrowdDenseRemovalSwapCarryTests
{
    [Test]
    public void SwapRemove_CarriesSeparationAndCalmToNewDenseIndex()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new Ludots.Core.CrowdSimulation.Nav.Pathing.PathQueryService(
            session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        var kernel = CrowdMovementKernel.Create(session);
        session.EnableMovement(kernel, new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 3, 1, 0, 0), Is.EqualTo(3));

        kernel.EnsureCapacity(session.Units.Count);
        int tail = 2;
        kernel.Separation[tail] = new Fix64Vec2(Fix64.FromInt(11), Fix64.FromInt(-7));
        kernel.Calm[tail] = 3;

        var release = typeof(CrowdSimUnits).GetMethod("ReleaseDense", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var moveRow = typeof(CrowdMovementKernel).GetMethod("MoveAvoidanceRow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        moveRow.Invoke(kernel, new object[] { tail, 1 });
        release.Invoke(session.Units, new object[] { 1 });
        session.Units.PlaybackStructuralChanges();

        Assert.That(kernel.Separation[1].X, Is.EqualTo(Fix64.FromInt(11)));
        Assert.That(kernel.Separation[1].Y, Is.EqualTo(Fix64.FromInt(-7)));
        Assert.That(kernel.Calm[1], Is.EqualTo((byte)3));
        Assert.That(kernel.Separation[tail].X, Is.EqualTo(Fix64.Zero));
        Assert.That(kernel.Calm[tail], Is.EqualTo((byte)0));
        Assert.That(session.Units.Count, Is.EqualTo(2));
    }
}
