using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Tests;
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
        var right = new CrowdSimStateDigest(4, 2, 1, 1, 1, groups: 9, link: 8);
        CrowdSimDigestDiff both = CrowdSimChecksum.DiffDigest(new[] { left }, new[] { right });
        TestContext.Out.WriteLine(
            $"both diff tick={both.Tick} section={both.Section} diag={CrowdSimChecksum.FormatDiagnostic(both.Diagnostic)}");
        Assert.That(both.Diverged, Is.True);
        Assert.That(both.Tick, Is.EqualTo(4));
        Assert.That(both.Section, Is.EqualTo("position"));
        Assert.That(both.DiagnosticTick, Is.EqualTo(4));
        Assert.That(both.Diagnostic, Is.EqualTo(CrowdSimDiagnosticSection.Groups | CrowdSimDiagnosticSection.Link));

        var linkOnly = new CrowdSimStateDigest(4, 1, 1, 1, 1, link: 8);
        CrowdSimDigestDiff link = CrowdSimChecksum.DiffDigest(new[] { left }, new[] { linkOnly });
        Assert.That(link.Diverged, Is.False);
        Assert.That(link.Tick, Is.EqualTo(-1));
        Assert.That(link.Section, Is.EqualTo(""));
        Assert.That(link.DiagnosticTick, Is.EqualTo(4));
        Assert.That(link.Diagnostic, Is.EqualTo(CrowdSimDiagnosticSection.Link));

        var earlyLeft = new[]
        {
            new CrowdSimStateDigest(1, 1, 1, 1, 1, commands: 1),
            new CrowdSimStateDigest(2, 1, 1, 1, 1),
        };
        var earlyRight = new[]
        {
            new CrowdSimStateDigest(1, 1, 1, 1, 1, commands: 2),
            new CrowdSimStateDigest(2, 7, 1, 1, 1),
        };
        CrowdSimDigestDiff split = CrowdSimChecksum.DiffDigest(earlyLeft, earlyRight);
        TestContext.Out.WriteLine(
            $"split diff tick={split.Tick} section={split.Section} diagTick={split.DiagnosticTick} diag={CrowdSimChecksum.FormatDiagnostic(split.Diagnostic)}");
        Assert.That(split.Diverged, Is.True);
        Assert.That(split.Tick, Is.EqualTo(2));
        Assert.That(split.Section, Is.EqualTo("position"));
        Assert.That(split.DiagnosticTick, Is.EqualTo(1));
        Assert.That(split.Diagnostic, Is.EqualTo(CrowdSimDiagnosticSection.Commands));
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
            $"goal diff chainedTick={diff.Tick} diagTick={diff.DiagnosticTick} section=groups diag={CrowdSimChecksum.FormatDiagnostic(diff.Diagnostic)}");
        CrowdSimStateDigest recomputed = CrowdSimChecksum.Compute(sessionB);
        Assert.That(diff.Diverged, Is.False);
        Assert.That(diff.Tick, Is.EqualTo(-1));
        Assert.That(diff.Section, Is.EqualTo(""));
        Assert.That(diff.DiagnosticTick, Is.EqualTo(TickN));
        Assert.That(diff.Diagnostic, Is.EqualTo(CrowdSimDiagnosticSection.Groups));
        Assert.That(recomputed.Total, Is.EqualTo(digestsA[TickN - 1].Total));
        Assert.That(recomputed.Position, Is.EqualTo(digestsA[TickN - 1].Position));
        Assert.That(recomputed.Motion, Is.EqualTo(digestsA[TickN - 1].Motion));
        Assert.That(recomputed.State, Is.EqualTo(digestsA[TickN - 1].State));
        Assert.That(recomputed.Avoidance, Is.EqualTo(digestsA[TickN - 1].Avoidance));
        Assert.That(recomputed.Groups, Is.Not.EqualTo(digestsA[TickN - 1].Groups));
    }

    [Test]
    public void PendingFutureCommand_DoesNotFlipReplayStatus_DiagnosticLeavesTotalAndPosition()
    {
        const int Ticks = 20;
        var (config, sessionOn) = Parity.S4DeployTruthTests.BuildSession("s7");
        sessionOn.Structures = null;
        sessionOn.DiagnosticDigest = true;
        var sessionOff = new CrowdSimSession(
            config, World.Create(), sessionOn.Navs, sessionOn.NavByLayerRadius, sessionOn.Presentation);
        sessionOff.Structures = null;
        sessionOff.DiagnosticDigest = false;

        var script = new List<CrowdCommand>
        {
            new(0, new CrowdSimCommand
            {
                Kind = CrowdSimCommandKind.SpawnAt,
                Player = 1,
                XCm = 480000,
                YCm = 560000,
                Count = 8,
                UnitType = 0,
                RIdx = 0,
            }),
            new(100000, new CrowdSimCommand
            {
                Kind = CrowdSimCommandKind.PlaceStructure,
                TemplateId = "s7barrier",
                XCm = 620000,
                YCm = 620000,
                SizeCm = 4000,
            }),
        };
        sessionOn.Commands.Schedule(sessionOn, script);
        sessionOff.Commands.Schedule(sessionOff, script);

        var hostOn = new CrowdSimulationRuntime();
        var hostOff = new CrowdSimulationRuntime();
        Bind(hostOn, sessionOn);
        Bind(hostOff, sessionOff);
        var onTotals = new ulong[Ticks];
        var onPositions = new ulong[Ticks];
        var offTotals = new ulong[Ticks];
        var offPositions = new ulong[Ticks];
        for (int i = 0; i < Ticks; i++)
        {
            hostOn.Tick();
            Assert.That(sessionOn.TickCount, Is.EqualTo(i + 1));
            onTotals[i] = sessionOn.LastDigest.Total;
            onPositions[i] = sessionOn.LastDigest.Position;
        }

        for (int i = 0; i < Ticks; i++)
        {
            hostOff.Tick();
            Assert.That(sessionOff.TickCount, Is.EqualTo(i + 1));
            offTotals[i] = sessionOff.LastDigest.Total;
            offPositions[i] = sessionOff.LastDigest.Position;
        }

        Assert.That(sessionOn.Units.Count, Is.EqualTo(8));
        Assert.That(sessionOn.Commands.PendingCount, Is.EqualTo(1));
        Assert.That(sessionOn.TickCount, Is.EqualTo(Ticks));
        for (int i = 0; i < Ticks; i++)
        {
            Assert.That(offTotals[i], Is.EqualTo(onTotals[i]), $"tick {i + 1} Total");
            Assert.That(offPositions[i], Is.EqualTo(onPositions[i]), $"tick {i + 1} position");
        }

        TestContext.Out.WriteLine($"diag on/off total+position match ticks={Ticks}");
        hostOn.RunReplay();
        hostOff.RunReplay();
        TestContext.Out.WriteLine(
            $"replay on status={hostOn.ReplayStatus} tick={hostOn.ReplayDivergenceTick} section={hostOn.ReplayDivergenceSection} diagTick={hostOn.ReplayDiagnosticTick} diag={CrowdSimChecksum.FormatDiagnostic(hostOn.ReplayDivergenceDiagnostic)}");
        TestContext.Out.WriteLine(
            $"replay off status={hostOff.ReplayStatus} tick={hostOff.ReplayDivergenceTick} section={hostOff.ReplayDivergenceSection} diagTick={hostOff.ReplayDiagnosticTick} diag={CrowdSimChecksum.FormatDiagnostic(hostOff.ReplayDivergenceDiagnostic)}");
        Assert.That(hostOn.ReplayStatus, Is.EqualTo(1));
        Assert.That(hostOn.ReplayDivergenceDiagnostic.HasFlag(CrowdSimDiagnosticSection.Commands), Is.False);
        Assert.That(hostOff.ReplayStatus, Is.EqualTo(1));
        Assert.That(hostOff.ReplayDiagnosticTick, Is.EqualTo(-1));
    }

    [Test]
    public void DemoScript_FutureRowsStayPending_ReplayDiagnosticsMatch()
    {
        var (config, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        session.Structures = null;
        session.DiagnosticDigest = true;
        using var service = new PathQueryService(
            session.Navs, config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        var script = CrowdDemoScript.Load(new JsonArray
        {
            new JsonObject
            {
                ["tick"] = 0,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "spawnAt", ["player"] = 1, ["xCm"] = 480000, ["yCm"] = 560000,
                    ["count"] = 4, ["template"] = "crowd_simulation.unit.infantry.r100",
                },
            },
            new JsonObject
            {
                ["tick"] = 5,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "order", ["player"] = 1, ["xCm"] = 700000, ["yCm"] = 700000, ["shape"] = "box",
                },
            },
        });
        script.ValidateStructure(session);
        script.Schedule(session);
        var host = new CrowdSimulationRuntime();
        Bind(host, session);
        Set(host, "_planTimeout", TimeSpan.FromSeconds(30));
        const int Ticks = 8;
        for (int i = 0; i < Ticks; i++) host.Tick();
        Assert.That(session.TickCount, Is.EqualTo(Ticks));
        Assert.That(session.Commands.PendingCount, Is.EqualTo(0));
        Assert.That(session.Commands.Log, Has.Count.EqualTo(2));
        host.RunReplay();
        TestContext.Out.WriteLine(
            $"demo replay status={host.ReplayStatus} tick={host.ReplayDivergenceTick} section={host.ReplayDivergenceSection} diagTick={host.ReplayDiagnosticTick} diag={CrowdSimChecksum.FormatDiagnostic(host.ReplayDivergenceDiagnostic)}");
        Assert.That(host.ReplayStatus, Is.EqualTo(1));
        Assert.That(host.ReplayDivergenceTick, Is.EqualTo(-1));
        Assert.That(host.ReplayDiagnosticTick, Is.EqualTo(-1));
        Assert.That(host.ReplayDivergenceDiagnostic, Is.EqualTo(CrowdSimDiagnosticSection.None));
    }

    /// <summary>全体己方下令执行时该玩家没有单位：照常进日志，不改任何组，会话计数并发通知；回放同样对齐。</summary>
    [Test]
    public void ScriptedAllOwnOrder_PlayerWithoutUnits_IsLoggedAndNotified()
    {
        var (config, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        session.Structures = null;
        session.DiagnosticDigest = true;
        using var service = new PathQueryService(session.Navs, config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        var script = CrowdDemoScript.Load(new JsonArray
        {
            new JsonObject
            {
                ["tick"] = 0,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "spawnAt", ["player"] = 1, ["xCm"] = 480000, ["yCm"] = 560000,
                    ["count"] = 4, ["template"] = "crowd_simulation.unit.infantry.r100",
                },
            },
            new JsonObject
            {
                ["tick"] = 3,
                ["cmd"] = new JsonObject { ["type"] = "order", ["player"] = 2, ["xCm"] = 700000, ["yCm"] = 700000, ["shape"] = "box" },
            },
        });
        script.ValidateStructure(session);
        script.Schedule(session);
        var notices = new List<string>();
        session.Notified += notices.Add;
        var host = new CrowdSimulationRuntime();
        Bind(host, session);
        Set(host, "_planTimeout", TimeSpan.FromSeconds(30));
        for (int i = 0; i < 6; i++) host.Tick();

        Assert.That(session.TickCount, Is.EqualTo(6));
        Assert.That(session.Commands.Log.Count(e => e.Cmd.Kind == CrowdSimCommandKind.Order), Is.EqualTo(1));
        Assert.That(session.EmptyOrderSelectionTotal, Is.EqualTo(1));
        Assert.That(notices, Has.Some.Contains("玩家 2 没有单位"));
        Assert.That(session.Orders.List, Is.Empty);
        host.RunReplay();
        Assert.That(host.ReplayStatus, Is.EqualTo(1));
        Assert.That(host.ReplayDiagnosticTick, Is.EqualTo(-1));
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

    private static void Bind(CrowdSimulationRuntime host, CrowdSimSession session)
    {
        var relations = TeamRelationTestHarness.Create(session.World);
        var rules = CrowdPairRules.Matrices(1, new CrowdPushResponse[1], new bool[1]);
        var guard = new CrowdPairRulesSessionGuard(
            relations.Query,
            session.World,
            System.Array.Empty<Arch.Core.Entity>(),
            System.Array.Empty<int>(),
            System.Array.Empty<int>(),
            relations.HostileTypeId,
            relations.FriendlyTypeId,
            rules,
            relations.Query.Revision);
        Set(host, "_session", session);
        Set(host, "_pairRulesGuard", guard);
    }

    private static void Set(CrowdSimulationRuntime host, string field, object value)
    {
        typeof(CrowdSimulationRuntime).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);
    }
}
