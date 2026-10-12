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
        Assert.That(diff.Diagnostic, Is.EqualTo(CrowdSimDiagnosticSection.None));
    }

    [Test]
    public void DiffDigest_ChainedAndDiagnostic_NamesChainedFirst()
    {
        var left = new CrowdSimStateDigest(4, 1, 1, 1, 1);
        var right = new CrowdSimStateDigest(4, 2, 1, 1, 1, groups: 9, jump: 8);
        CrowdSimDigestDiff both = CrowdSimChecksum.DiffDigest(new[] { left }, new[] { right });
        TestContext.Out.WriteLine(
            $"both diff tick={both.Tick} section={both.Section} diag={CrowdSimChecksum.FormatDiagnostic(both.Diagnostic)}");
        Assert.That(both.Diverged, Is.True);
        Assert.That(both.Tick, Is.EqualTo(4));
        Assert.That(both.Section, Is.EqualTo("position"));
        Assert.That(both.Diagnostic, Is.EqualTo(CrowdSimDiagnosticSection.Groups | CrowdSimDiagnosticSection.Jump));

        var jumpOnly = new CrowdSimStateDigest(4, 1, 1, 1, 1, jump: 8);
        CrowdSimDigestDiff jump = CrowdSimChecksum.DiffDigest(new[] { left }, new[] { jumpOnly });
        Assert.That(jump.Section, Is.EqualTo("jump"));
        Assert.That(jump.Diagnostic, Is.EqualTo(CrowdSimDiagnosticSection.Jump));
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

    [Test]
    public void GoalPerturbation_ReportsGroups_BeforeChainedSections()
    {
        var (runtime, sessionA) = Parity.S4DeployTruthTests.BuildSession("s7");
        sessionA.Structures = null;
        sessionA.DiagnosticDigest = true;
        var sessionB = new CrowdSimSession(
            runtime, World.Create(), sessionA.Navs, sessionA.NavByLayerRadius, sessionA.Presentation);
        sessionB.DiagnosticDigest = true;
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
        Assert.That(CrowdSimChecksum.DiffDigest(digestsA, digestsB).Diverged, Is.False);

        int groupId = sessionB.Movement!.States[0].GroupId;
        Assert.That(sessionB.Groups.TryGet(groupId, out var group), Is.True);
        group.Goal = group.Goal == 3 ? 4 : 3;
        CrowdSimDigestDiff diff = DiffAgainstCurrent(digestsA, digestsB, sessionB);
        TestContext.Out.WriteLine(
            $"goal diff tick={diff.Tick} section={diff.Section} diag={CrowdSimChecksum.FormatDiagnostic(diff.Diagnostic)}");
        CrowdSimStateDigest recomputed = CrowdSimChecksum.Compute(sessionB);
        Assert.That(diff.Diverged, Is.True);
        Assert.That(diff.Tick, Is.EqualTo(TickN));
        Assert.That(diff.Section, Is.EqualTo("groups"));
        Assert.That(diff.Diagnostic, Is.EqualTo(CrowdSimDiagnosticSection.Groups));
        Assert.That(recomputed.Total, Is.EqualTo(digestsA[TickN - 1].Total));
        Assert.That(recomputed.Position, Is.EqualTo(digestsA[TickN - 1].Position));
        Assert.That(recomputed.Motion, Is.EqualTo(digestsA[TickN - 1].Motion));
        Assert.That(recomputed.State, Is.EqualTo(digestsA[TickN - 1].State));
        Assert.That(recomputed.Avoidance, Is.EqualTo(digestsA[TickN - 1].Avoidance));
        Assert.That(recomputed.Groups, Is.Not.EqualTo(digestsA[TickN - 1].Groups));
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
