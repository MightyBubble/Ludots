using System.Reflection;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Tests;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 现场会话不阻塞等路径答复，回放走运行时的重建导航并阻塞等答复。
/// 脚本含下令和一次结构放置。诊断断面必须全部对齐。
/// </summary>
public sealed class CrowdReplayInteractiveDiagnosticTests
{
    [Test]
    public void S4LikeScript_WithOrderAndStructure_ReplayDiagnosticsMatch()
    {
        const string Seed = "s1337";
        const int Ticks = 140;
        var (config, session) = Parity.S4DeployTruthTests.BuildSession(Seed);
        session.DiagnosticDigest = true;
        session.VerifyIncrementalNav = false;
        using var service = new PathQueryService(session.Navs, config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        var script = CrowdDemoScript.Load(Script());
        script.ValidateStructure(session);
        session.StageDeferredCommands = script.StageFormable;

        var host = new CrowdSimulationRuntime();
        Bind(host, session, Seed);
        int guard = Ticks * 4000;
        while (session.TickCount < Ticks)
        {
            int before = session.TickCount;
            host.Tick();
            if (session.TickCount != before) continue;
            if (service.Faulted) Assert.Fail($"path fault at tick {session.TickCount}: {service.FaultMessage}");
            if (guard-- <= 0) Assert.Fail($"stalled at tick {session.TickCount}");
            Thread.Sleep(1);
        }

        Assert.That(session.Commands.Log.Any(e => e.Cmd.Kind == CrowdSimCommandKind.Order), Is.True);
        Assert.That(session.Commands.Log.Any(e => e.Cmd.Kind == CrowdSimCommandKind.PlaceStructure), Is.True);
        host.RunReplay();
        TestContext.Out.WriteLine(
            $"replay status={host.ReplayStatus} divTick={host.ReplayDivergenceTick} section={host.ReplayDivergenceSection} diagTick={host.ReplayDiagnosticTick} diag={CrowdSimChecksum.FormatDiagnostic(host.ReplayDivergenceDiagnostic)}");
        Assert.That(host.ReplayStatus, Is.EqualTo(1));
        Assert.That(host.ReplayDivergenceTick, Is.EqualTo(-1));
        Assert.That(host.ReplayDivergenceSection, Is.EqualTo(""));
        Assert.That(host.ReplayDiagnosticTick, Is.EqualTo(-1));
        Assert.That(host.ReplayDivergenceDiagnostic, Is.EqualTo(CrowdSimDiagnosticSection.None));
    }

    private static JsonArray Script() => new()
    {
        new JsonObject { ["tick"] = 0, ["cmd"] = new JsonObject { ["type"] = "spawn", ["count"] = 256 } },
        new JsonObject
        {
            ["tick"] = 40,
            ["cmd"] = new JsonObject { ["type"] = "order", ["player"] = 1, ["xCm"] = 1200000, ["yCm"] = 800000, ["shape"] = "box" },
        },
        new JsonObject
        {
            ["tick"] = 60,
            ["cmd"] = new JsonObject
            {
                ["type"] = "reveal", ["player"] = 1,
                ["rect"] = JsonNode.Parse("[1000000, 700000, 1150000, 850000]"),
            },
        },
        new JsonObject
        {
            ["tick"] = 90,
            ["cmd"] = new JsonObject { ["type"] = "order", ["player"] = 2, ["xCm"] = 400000, ["yCm"] = 900000, ["shape"] = "box" },
        },
        new JsonObject
        {
            ["tick"] = 120,
            ["cmd"] = new JsonObject
            {
                ["type"] = "placeStructure", ["template"] = "building",
                ["xCm"] = 503125, ["yCm"] = 565625, ["sizeCm"] = 14000,
            },
        },
    };

    private static void Bind(CrowdSimulationRuntime host, CrowdSimSession session, string seed)
    {
        var relations = TeamRelationTestHarness.Create(session.World);
        var rules = CrowdPairRules.Matrices(1, new CrowdPushResponse[1], new bool[1]);
        var guard = new CrowdPairRulesSessionGuard(
            relations.Query,
            session.World,
            Array.Empty<Entity>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            relations.HostileTypeId,
            relations.FriendlyTypeId,
            rules,
            relations.Query.Revision);
        var map = Parity.S1SurfaceTruthTests.LoadMapSurface(seed);
        var sources = session.RebakeSources ?? throw new InvalidOperationException("会话没有重烘源。");
        Set(host, "_session", session);
        Set(host, "_pairRulesGuard", guard);
        Set(host, "_planTimeout", TimeSpan.FromSeconds(30));
        Set(host, "_surfaceGrid", session.Structures!.TerrainSurface());
        Set(host, "_heights", sources.HeightS);
        Set(host, "_deck", sources.Deck);
        Set(host, "_jumpCandidates", sources.JumpCandidates);
        Set(host, "_mapSurfaceBlockers", map.Blockers);
        Set(host, "_mapSurfaceBridges", map.Bridges);
    }

    private static void Set(CrowdSimulationRuntime host, string field, object value)
        => typeof(CrowdSimulationRuntime).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);
}
