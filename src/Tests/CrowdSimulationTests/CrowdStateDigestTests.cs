using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 分区摘要：扰动落在 tick 末的 SoA 上再重算，马达还没把速度积进位置。
/// 位置词在速度词之前，扰动后再步进会让首个分歧变成 position。
/// </summary>
public sealed class CrowdStateDigestTests
{
    private const int TickN = 3;

    [Test]
    public void DiffDigest_CountMismatch_ReportsSentinelTick()
    {
        var row = new CrowdSimStateDigest(1, 2, 3, 4, 5);
        CrowdSimDigestDiff diff = CrowdSimChecksum.DiffDigest(new[] { row }, new CrowdSimStateDigest[0]);
        Assert.That(diff.Diverged, Is.True);
        Assert.That(diff.Tick, Is.EqualTo(-2));
        Assert.That(diff.Section, Is.EqualTo(""));
    }

    [Test]
    public void VelocityPerturbation_ReportsMotion_PositionPerturbation_ReportsPosition()
    {
        var (runtime, sessionA) = Parity.S4DeployTruthTests.BuildSession("s7");
        sessionA.Structures = null;
        var sessionB = new CrowdSimSession(
            runtime, World.Create(), sessionA.Navs, sessionA.NavByLayerRadius, sessionA.Presentation);
        using var serviceA = new PathQueryService(sessionA.Navs, runtime, 1, TimeSpan.FromSeconds(30), sessionA.ResolveNavContext);
        using var serviceB = new PathQueryService(sessionB.Navs, runtime, 1, TimeSpan.FromSeconds(30), sessionB.ResolveNavContext);
        sessionA.EnableMovement(CrowdMovementKernel.Create(sessionA), new CrowdSimPlanner(sessionA, serviceA));
        sessionB.EnableMovement(CrowdMovementKernel.Create(sessionB), new CrowdSimPlanner(sessionB, serviceB));
        sessionA.BlockOnDueReplies = true;
        sessionB.BlockOnDueReplies = true;

        var origin = Fix64.FromInt(480000);
        var originY = Fix64.FromInt(560000);
        Assert.That(CrowdDeployment.SpawnAt(sessionA, origin, originY, 4, 1, 0, 0), Is.EqualTo(4));
        Assert.That(CrowdDeployment.SpawnAt(sessionB, origin, originY, 4, 1, 0, 0), Is.EqualTo(4));

        var digestsA = new List<CrowdSimStateDigest>(TickN);
        var digestsB = new List<CrowdSimStateDigest>(TickN);
        sessionA.Advance(TickN, digests: digestsA);
        sessionB.Advance(TickN, digests: digestsB);
        Assert.That(sessionA.TickCount, Is.EqualTo(TickN));
        Assert.That(sessionB.TickCount, Is.EqualTo(TickN));
        Assert.That(digestsA, Has.Count.EqualTo(TickN));
        Assert.That(CrowdSimChecksum.ComputeValue(sessionA), Is.EqualTo(sessionA.LastDigest.Total));
        Assert.That(sessionA.LastDigest.Total, Is.EqualTo(sessionA.LastDigest.Avoidance));
        CrowdSimDigestDiff same = CrowdSimChecksum.DiffDigest(digestsA, digestsB);
        Assert.That(same.Diverged, Is.False, "未扰动时两会话摘要应一致");

        var savedKin = sessionB.Movement!.Kins[0];
        var kin = savedKin;
        kin.Velocity = new Fix64Vec2(Fix64.FromInt(120), Fix64.Zero);
        sessionB.Movement.Kins[0] = kin;
        CrowdSimDigestDiff motion = DiffAgainstCurrent(digestsA, digestsB, sessionB);
        TestContext.Out.WriteLine($"velocity diff tick={motion.Tick} section={motion.Section}");
        Assert.That(motion.Diverged, Is.True);
        Assert.That(motion.Tick, Is.EqualTo(TickN));
        Assert.That(motion.Section, Is.EqualTo("motion"));
        Assert.That(motion.Section, Is.Not.EqualTo("position"));
        CrowdSimStateDigest motionDigest = CrowdSimChecksum.Compute(sessionB);
        Assert.That(motionDigest.Position, Is.EqualTo(digestsA[TickN - 1].Position));
        Assert.That(motionDigest.Motion, Is.Not.EqualTo(digestsA[TickN - 1].Motion));
        Assert.That(motionDigest.Total, Is.EqualTo(motionDigest.Avoidance));

        sessionB.Movement.Kins[0] = savedKin;
        var pos = sessionB.Movement.Positions[0];
        sessionB.Movement.Positions[0] = new Fix64Vec2(pos.X + Fix64.FromInt(25), pos.Y);
        CrowdSimDigestDiff position = DiffAgainstCurrent(digestsA, digestsB, sessionB);
        TestContext.Out.WriteLine($"position diff tick={position.Tick} section={position.Section}");
        Assert.That(position.Diverged, Is.True);
        Assert.That(position.Tick, Is.EqualTo(TickN));
        Assert.That(position.Section, Is.EqualTo("position"));
        Assert.That(CrowdSimChecksum.Compute(sessionB).Position, Is.Not.EqualTo(digestsA[TickN - 1].Position));
    }

    private static CrowdSimDigestDiff DiffAgainstCurrent(
        List<CrowdSimStateDigest> baseline,
        List<CrowdSimStateDigest> prior,
        CrowdSimSession mutated)
    {
        var copy = new List<CrowdSimStateDigest>(prior);
        copy[^1] = CrowdSimChecksum.Compute(mutated);
        return CrowdSimChecksum.DiffDigest(baseline, copy);
    }
}
