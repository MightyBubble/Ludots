using System.Reflection;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Scripting;
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
        TestDefaults.EnableFog(session);
        var script = CrowdDemoScript.Load(Script());
        script.ValidateStructure(session);
        script.Schedule(session);

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

    /// <summary>
    /// S4 脚本里一条指令带多组同导航同目标，玩家 1 也会对同一目标再下指令：
    /// 规划会共用同指令另一组新建的场，也会沿用规划时钉住的缓存场；
    /// 结构变更后同导航同目标的组搭同一次刷新。
    /// 拆掉建筑后、旧场等刷新落地的那几 tick 成员采到场外，走失记录留到刷新落帧，
    /// 再整份交给一次扩展，走失 tile 都在旧走廊一圈垫层内。回放诊断仍须逐段对齐。
    /// </summary>
    [Test]
    public void S4Script_PlansReuseCachedFields_ReplayDiagnosticsMatch()
    {
        const string Seed = "s1337";
        const int Ticks = 260;
        var (config, session) = Parity.S4DeployTruthTests.BuildSession(Seed);
        session.DiagnosticDigest = true;
        session.VerifyIncrementalNav = false;
        using var service = new PathQueryService(session.Navs, config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        var planner = new CrowdSimPlanner(session, service);
        session.EnableMovement(CrowdMovementKernel.Create(session), planner);
        TestDefaults.EnableFog(session);
        var script = LoadS4Script();
        script.ValidateStructure(session);
        script.Schedule(session);

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

        WriteStrayDistribution(planner, service, session.TickCount);
        Assert.That(session.Telemetry.Plans, Is.GreaterThan(0), "场景无效：脚本没有落地任何规划");
        Assert.That(planner.PlanCachedReplies, Is.GreaterThan(0), "规划没有沿用缓存场");
        Assert.That(planner.PlanSameReplies, Is.GreaterThan(0), "规划没有共用同指令的场");
        Assert.That(planner.RefreshRiders, Is.GreaterThan(0), "刷新没有合并同导航同目标的组");
        Assert.That(planner.StraySkipInFlight, Is.GreaterThan(0), "刷新在途时成员没有采到场外");
        Assert.That(planner.ExtendSubmits, Is.GreaterThan(0), "刷新落帧后攒下的走失记录没有提交扩展");
        Assert.That(planner.ExtendSubmits, Is.LessThanOrEqualTo(planner.StrayGroupTicks - planner.StraySkipInFlight));
        Assert.That(planner.ExtendBeyondPad, Is.EqualTo(0), "走失 tile 离旧走廊超过一圈垫层");
        host.RunReplay();
        TestContext.Out.WriteLine(
            $"replay status={host.ReplayStatus} divTick={host.ReplayDivergenceTick} section={host.ReplayDivergenceSection} diagTick={host.ReplayDiagnosticTick} diag={CrowdSimChecksum.FormatDiagnostic(host.ReplayDivergenceDiagnostic)}");
        Assert.That(host.ReplayStatus, Is.EqualTo(1));
        Assert.That(host.ReplayDivergenceTick, Is.EqualTo(-1));
        Assert.That(host.ReplayDivergenceSection, Is.EqualTo(""));
        Assert.That(host.ReplayDiagnosticTick, Is.EqualTo(-1));
        Assert.That(host.ReplayDivergenceDiagnostic, Is.EqualTo(CrowdSimDiagnosticSection.None));
    }

    /// <summary>
    /// S4 演示脚本跑到 tick 150 时按压测键（Y）重新部署满员，脚本 tick 200 / 240 的下令还没执行。
    /// 重新部署清掉旧单位，这两条下令要落到新单位上，日志存全体己方选择，回放合计逐 tick 对齐。
    /// </summary>
    [Test]
    public void S4Script_StressRespawnBeforeScriptedOrders_OrdersBindRespawnedUnits()
    {
        const string Seed = "s1337";
        const int StressTick = 150;
        const int Ticks = 245;
        var (config, session) = Parity.S4DeployTruthTests.BuildSession(Seed);
        session.DiagnosticDigest = true;
        session.VerifyIncrementalNav = false;
        using var service = new PathQueryService(session.Navs, config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        TestDefaults.EnableFog(session);
        var script = LoadS4Script();
        script.ValidateStructure(session);
        script.Schedule(session);

        var host = new CrowdSimulationRuntime();
        Bind(host, session, Seed);
        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.CrowdSimulationSession, session);
        engine.SetService(CoreServiceKeys.CrowdSimulationRuntime, host);
        var demo = new S4DeployDemoRuntime(engine);
        int unitsBeforeStress = -1;
        int guard = Ticks * 4000;
        while (session.TickCount < Ticks)
        {
            if (session.TickCount == StressTick && unitsBeforeStress < 0)
            {
                unitsBeforeStress = session.Units.Count;
                demo.EnqueueStress();
                demo.Tick();
            }

            int before = session.TickCount;
            host.Tick();
            if (session.TickCount != before) continue;
            if (service.Faulted) Assert.Fail($"path fault at tick {session.TickCount}: {service.FaultMessage}");
            if (guard-- <= 0) Assert.Fail($"stalled at tick {session.TickCount}");
            Thread.Sleep(1);
        }

        var log = session.Commands.Log;
        Assert.That(log.Any(e => e.Tick == StressTick && e.Cmd.Kind == CrowdSimCommandKind.Spawn), Is.True, "压测部署没有进日志");
        Assert.That(session.Units.Count, Is.GreaterThan(unitsBeforeStress), "压测没有把单位补到上限");
        var orders = log.Where(e => e.Cmd.Kind == CrowdSimCommandKind.Order).ToArray();
        Assert.That(orders.Select(e => e.Tick), Is.EqualTo(new[] { 40, 90, 200, 240 }));
        Assert.That(orders.All(e => e.Cmd.AllOwnMembers && e.Cmd.Members == null), Is.True, "日志里的脚本下令应存全体己方选择");
        Assert.That(session.EmptyOrderSelectionTotal, Is.Zero);

        int playerOne = 0, playerOneOrdered = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var entity = session.Units.EntityAt(i);
            if (session.World.Get<PlayerOwner>(entity).PlayerId != 1) continue;
            playerOne++;
            var group = session.Groups.Groups[session.World.Get<CrowdSimulationUnitState>(entity).GroupId];
            if (group is { OrderId: > 0 }) playerOneOrdered++;
        }

        Assert.That(playerOne, Is.GreaterThan(0));
        Assert.That(playerOneOrdered, Is.EqualTo(playerOne), "tick 240 的下令没有落到重新部署后的全部玩家 1 单位");

        host.RunReplay();
        TestContext.Out.WriteLine(
            $"replay status={host.ReplayStatus} divTick={host.ReplayDivergenceTick} section={host.ReplayDivergenceSection} diagTick={host.ReplayDiagnosticTick} diag={CrowdSimChecksum.FormatDiagnostic(host.ReplayDivergenceDiagnostic)}");
        Assert.That(host.ReplayStatus, Is.EqualTo(1));
        Assert.That(host.ReplayDivergenceTick, Is.EqualTo(-1));
        // 压测是现场 Submit，活会话的未执行队列里从来没有它；回放把它连同脚本从 tick 0 排进队列，
        // 所以只有 commands 诊断从 tick 1 起不同，其余断面逐段对齐。
        Assert.That(host.ReplayDivergenceDiagnostic, Is.EqualTo(CrowdSimDiagnosticSection.Commands));
        Assert.That(host.ReplayDiagnosticTick, Is.EqualTo(1));
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
        TestDefaults.EnableFog(session);
        var script = LoadS4Script();
        script.ValidateStructure(session);
        script.Schedule(session);
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

        WriteStrayDistribution(planner, service, session.TickCount);
        int sum = 0;
        for (int i = 0; i < planner.ExtendByGroup.Length; i++) sum += planner.ExtendByGroup[i];
        Assert.That(sum, Is.EqualTo(planner.ExtendSubmits));
        Assert.That(planner.ExtendWithinPad + planner.ExtendBeyondPad, Is.EqualTo(planner.ExtendSubmits));
    }

    private static void WriteStrayDistribution(CrowdSimPlanner planner, PathQueryService service, int tick)
    {
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

        var stats = service.CopyStats();
        TestContext.Out.WriteLine(
            $"tick {tick} extends {planner.ExtendSubmits} sum {sum} groups {groups} max {max} " +
            $"hits {planner.FlowCacheHits} misses {planner.FlowCacheMisses} evict {planner.FlowCacheEvicts} " +
            $"planCached {planner.PlanCachedReplies} planSame {planner.PlanSameReplies} " +
            $"refreshes {planner.FlowRefreshSubmits} riders {planner.RefreshRiders} refreshCached {planner.RefreshCachedReplies} " +
            $"pendingTicks {planner.StrayGroupTicks} skipFlight {planner.StraySkipInFlight} strayTiles {planner.ExtendStrayTiles} " +
            $"inPad {planner.ExtendWithinPad} beyond {planner.ExtendBeyondPad} " +
            $"extendMs {stats.ExtendMs:F1} extendJobs {stats.ExtendJobs}");
        TestContext.Out.WriteLine("per-group histogram (index = count, last = that many or more): " + string.Join(",", hist));
        TestContext.Out.WriteLine("tile distance histogram: " + string.Join(",", planner.ExtendDistance));
    }

    private static CrowdDemoScript LoadS4Script()
    {
        // 会话建在 S1 地形配置上（S1 进图不带单位），开局人数取 S4 自己声明的 deploy.initialUnits，与会话宿主同一条合成。
        var doc = ModConfigChain.Open("CrowdSimulationS4DeployMod").Merged("CrowdSimulationDebug.json");
        string assets = Path.Combine(RepoRoot(), "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets");
        var s4Config = JsonNode.Parse(File.ReadAllText(Path.Combine(assets, "CrowdSimulationConfig.json")))!.AsObject();
        int initialUnits = s4Config["deploy"]!["initialUnits"]!.GetValue<int>();
        CrowdDebugScriptEntry[]? script = CrowdSimulationDebugConfig.Load(doc).Session?.Script;
        return CrowdDemoScript.Load(CrowdSimulationRuntime.ComposeDemoScript(initialUnits, script));
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
        Set(host, "_upperLayer", sources.UpperLayer);
        Set(host, "_linkCandidates", sources.LinkCandidates);
        Set(host, "_mapSurfaceBlockers", map.Blockers);
        Set(host, "_mapLayeredPaths", map.LayeredPaths);
    }

    private static void Set(CrowdSimulationRuntime host, string field, object value)
        => typeof(CrowdSimulationRuntime).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);
}
