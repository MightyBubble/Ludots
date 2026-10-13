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

    [Test]
    public void StrayAndCacheHit_ReplayDiagnosticsMatch()
    {
        const string Seed = "s1337";
        const int Cm = 6250;
        const int Ticks = 160;
        int spawnX = 70 * Cm + Cm / 2;
        int spawnY = 90 * Cm + Cm / 2;
        int goalX = 96 * Cm + Cm / 2;
        int goalY = 112 * Cm + Cm / 2;
        int farX = 8 * Cm + Cm / 2;
        int farY = 8 * Cm + Cm / 2;
        var (config, session) = Parity.S4DeployTruthTests.BuildSession(Seed);
        session.DiagnosticDigest = true;
        session.VerifyIncrementalNav = false;
        using var service = new PathQueryService(session.Navs, config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        var planner = new CrowdSimPlanner(session, service);
        session.EnableMovement(CrowdMovementKernel.Create(session), planner);
        var script = CrowdDemoScript.Load(new JsonArray
        {
            new JsonObject
            {
                ["tick"] = 0,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "spawnAt", ["player"] = 1, ["xCm"] = spawnX, ["yCm"] = spawnY,
                    ["count"] = 48, ["unitType"] = 0, ["rIdx"] = 0,
                },
            },
            new JsonObject
            {
                ["tick"] = 0,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "spawnAt", ["player"] = 2, ["xCm"] = spawnX, ["yCm"] = spawnY,
                    ["count"] = 1, ["unitType"] = 0, ["rIdx"] = 0,
                },
            },
            new JsonObject
            {
                ["tick"] = 20,
                ["cmd"] = new JsonObject { ["type"] = "order", ["player"] = 1, ["xCm"] = goalX, ["yCm"] = goalY, ["shape"] = "box" },
            },
            new JsonObject
            {
                ["tick"] = 100,
                ["cmd"] = new JsonObject { ["type"] = "order", ["player"] = 2, ["xCm"] = goalX, ["yCm"] = goalY, ["shape"] = "box" },
            },
            new JsonObject
            {
                ["tick"] = 101,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "placeStructure", ["template"] = "building",
                    ["xCm"] = farX, ["yCm"] = farY, ["sizeCm"] = 14000,
                },
            },
        });
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

        TestContext.Out.WriteLine(
            $"live extends {planner.ExtendSubmits} hits {planner.FlowCacheHits} misses {planner.FlowCacheMisses} " +
            $"covered {planner.ExtendTileCovered} inPad {planner.ExtendWithinPad} beyond {planner.ExtendBeyondPad} " +
            $"skipFlight {planner.StraySkipInFlight} skipClosed {planner.StraySkipNoGrowth} notes {planner.StrayGroupTicks}");
        Assert.That(planner.ExtendSubmits, Is.GreaterThan(0));
        Assert.That(planner.FlowCacheHits, Is.GreaterThan(0));
        host.RunReplay();
        TestContext.Out.WriteLine(
            $"replay status={host.ReplayStatus} divTick={host.ReplayDivergenceTick} section={host.ReplayDivergenceSection} diagTick={host.ReplayDiagnosticTick} diag={CrowdSimChecksum.FormatDiagnostic(host.ReplayDivergenceDiagnostic)}");
        Assert.That(host.ReplayStatus, Is.EqualTo(1));
        Assert.That(host.ReplayDivergenceTick, Is.EqualTo(-1));
        Assert.That(host.ReplayDivergenceSection, Is.EqualTo(""));
        Assert.That(host.ReplayDiagnosticTick, Is.EqualTo(-1));
        Assert.That(host.ReplayDivergenceDiagnostic, Is.EqualTo(CrowdSimDiagnosticSection.None));
    }

    [Test]
    [Explicit]
    public void S4Script_ReportsStrayExtensionDistribution()
    {
        const string Seed = "s1337";
        const int Ticks = 260;
        var (config, session) = Parity.S4DeployTruthTests.BuildSession(Seed);
        session.VerifyIncrementalNav = false;
        using var service = new PathQueryService(session.Navs, config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        var planner = new CrowdSimPlanner(session, service);
        session.EnableMovement(CrowdMovementKernel.Create(session), planner);
        string root = RepoRoot();
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(
            root,
            "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulationDebug.json")))!;
        var script = CrowdDemoScript.Load(doc["session"]!["script"]!.AsArray());
        script.ValidateStructure(session);
        session.StageDeferredCommands = script.StageFormable;
        var host = new CrowdSimulationRuntime();
        Bind(host, session, Seed);
        int guard = Ticks * 8000;
        while (session.TickCount < Ticks)
        {
            int before = session.TickCount;
            host.Tick();
            if (session.TickCount != before) continue;
            if (service.Faulted) Assert.Fail($"path fault at tick {session.TickCount}: {service.FaultMessage}");
            if (guard-- <= 0) Assert.Fail($"stalled at tick {session.TickCount}");
            Thread.Sleep(1);
        }

        int groups = 0, max = 0, sum = 0;
        var hist = new int[8];
        for (int i = 0; i < planner.ExtendByGroup.Length; i++)
        {
            int n = planner.ExtendByGroup[i];
            if (n == 0) continue;
            groups++;
            sum += n;
            if (n > max) max = n;
            hist[n >= hist.Length ? hist.Length - 1 : n]++;
        }

        var dist = planner.ExtendDistance;
        var stats = service.CopyStats();
        TestContext.Out.WriteLine(
            $"tick {session.TickCount} extends {planner.ExtendSubmits} sum {sum} groups {groups} max {max} " +
            $"hits {planner.FlowCacheHits} misses {planner.FlowCacheMisses} evict {planner.FlowCacheEvicts} " +
            $"notes {planner.StrayGroupTicks} skipFlight {planner.StraySkipInFlight} skipClosed {planner.StraySkipNoGrowth} " +
            $"covered {planner.ExtendTileCovered} inPad {planner.ExtendWithinPad} beyond {planner.ExtendBeyondPad} " +
            $"extendMs {stats.ExtendMs:F1} extendJobs {stats.ExtendJobs}");
        TestContext.Out.WriteLine("per-group histogram (index = count, last = that many or more): " + string.Join(",", hist));
        TestContext.Out.WriteLine("tile distance histogram: " + string.Join(",", dist));
        Assert.That(sum, Is.EqualTo(planner.ExtendSubmits));
        Assert.That(planner.ExtendTileCovered + planner.ExtendWithinPad + planner.ExtendBeyondPad, Is.EqualTo(planner.ExtendSubmits));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulationDebug.json")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到 S4 演示脚本。");
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
