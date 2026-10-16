using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Navigation.AgentProfiles;
using CrowdSimulationTests.Parity;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>
/// 路径答复迟到、仿真停摆重试同一 tick 时逐 tick 校验码与不延迟一致；跨导航的扩展答复不被采纳并改排刷新；
/// 指令队列按 tick 归并且入队后与调用方脱钩；坏配置叶子被加载器拒绝；最近可达格环搜等于全扫；
/// 迷雾混编下 300 tick 的路径任务数落在重排形态区间。
/// </summary>
[TestFixture]
public sealed class DefenseUatTests
{
    private static readonly TimeSpan ReplyDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan RunBudget = TimeSpan.FromMinutes(10);
    private const int NearestCasesPerNav = 30;
    private const int PerfUnits = 3000;
    private const int PerfWarmTicks = 4;
    private const int PerfTicks = 300;
    private const int PerfLineWidthCm = 80000;
    private const long MinPlans = 10;
    private const long MaxPlans = 40;

    [TestCase("deploy")]
    [TestCase("rebake")]
    [TestCase("march")]
    public void DelayedReplies_StallAndRetryTheSameTick_MatchTheZeroDelayRun(string id)
    {
        var a = RunWithDelay(id, delayed: false);
        var b = RunWithDelay(id, delayed: true);
        int first = -1;
        for (int t = 0; t < a.Trace.Length; t++)
        {
            if (a.Trace[t] == b.Trace[t]) continue;
            first = t;
            break;
        }

        var differing = new List<string>();
        CrowdSimPartitions.CollectDifferences(a.Partitions, b.Partitions, differing);
        TestContext.Out.WriteLine(
            $"[uat-defense-latency] {id} ticks={a.Trace.Length} stalls={b.Stalls} jobStalls={b.JobStalls} firstDiff={first} " +
            $"end={a.Trace[^1]:x16}/{b.Trace[^1]:x16} differing={string.Join(",", differing.Take(3))}");
        Assert.That(b.Stalls, Is.GreaterThan(0), $"{id}：延迟档未产生停摆（防线无效）");
        if (id == "march") Assert.That(b.JobStalls, Is.GreaterThan(0), "march：停摆未压到在途重烘任务");
        Assert.That(first, Is.EqualTo(-1), $"{id}：tick {first + 1} 起校验码与零延迟不一致；{string.Join("；", differing.Take(2))}");
        Assert.That(differing, Is.Empty, $"{id}：终态分区与零延迟不一致");
    }

    [Test]
    public void ExtendReplyForAnotherNav_IsRejectedAndTheGroupIsRequeued()
    {
        using var scene = CrowdUatKit.Scene();
        var session = scene.Session;
        var nav = scene.Nav;
        int side = nav.NavCellsPerSide;
        int CellAtFraction(int permille)
        {
            var at = scene.WorldCm * permille / 1000;
            return CrowdDeployment.NearestPassable(nav, CrowdSimulationSpace.CellAtStrict(at, at, scene.CellCm, side));
        }

        int c0 = CellAtFraction(450), c1 = CellAtFraction(550);
        var (sx, sy) = CrowdUatKit.CellCentre(session, c0);
        var (gx, gy) = CrowdUatKit.CellCentre(session, c1);
        CrowdUatKit.SpawnAt(session, sx, sy, 20, rIdx: 0);
        CrowdUatKit.Order(session, gx, gy);
        CrowdUatKit.Step(session, session.Config.Planning.LatencyTicks + 2);
        int gid = CrowdUatKit.StateAt(session, 0).GroupId;
        if (!session.Groups.TryGet(gid, out var g) || g.Flow?.Mask == null) Assert.Fail("场景无效：组未拿到流场");

        var planner = session.Planner!;
        int outside = CrowdUatNav.CellOutside(session.ResolveNavContext(g.NavId), g.Flow!.Mask!);
        if (outside < 0) Assert.Fail("场景无效：流场掩码外没有可扩展的 tile");
        planner.NoteStray(g.Id, outside);
        CrowdUatKit.Step(session);
        if (!g.Extending) Assert.Fail("场景无效：场外采样没有提交扩展");

        int own = g.NavId;
        var version = session.ResolveNavContext(own).Version;
        var other = session.Navs.OrderBy(kv => kv.Key).Select(kv => kv.Value)
            .FirstOrDefault(n => n.Id != own && n.Version == version && n.NavCellsPerSide == side);
        if (other == null) Assert.Fail("场景无效：没有同版本的另一份导航");
        var before = g.Flow;
        g.NavId = other!.Id;
        try
        {
            Assert.That(planner.ApplyDue(session.TickCount + session.Config.Planning.RefreshLatencyTicks, true), Is.True);
        }
        finally
        {
            g.NavId = own;
        }

        TestContext.Out.WriteLine(
            $"[uat-defense-extend] group={g.Id} nav={own} other={other.Id} version={version} adopted={!ReferenceEquals(g.Flow, before)} " +
            $"extending={g.Extending} requeued={planner.Refreshing(g)}");
        Assert.That(g.Flow, Is.SameAs(before), "另一导航（同版本、不同导航号）的扩展答复被采纳");
        Assert.That(g.Extending, Is.False, "跨导航答复被拒后组仍停在扩展中");
        Assert.That(planner.Refreshing(g), Is.True, "跨导航答复被拒后未改排刷新");
        Assert.That(g.Flow!.NavId, Is.EqualTo(own), "组流场导航号与组导航不符");
        CrowdSimulationValidator.ValidateSim(session);
    }

    [Test]
    public void CommandQueue_MergesByTick_DetachesFromTheCaller_AndRunsLateEntriesNow()
    {
        using var scene = CrowdUatKit.Scene();
        var session = scene.Session;
        var queue = new CrowdCommandQueue();
        var ran = new List<(int Tick, string Label)>();
        object? Exec(CrowdSimSession sim, CrowdSimCommand cmd)
        {
            ran.Add((sim.TickCount, Label(cmd)));
            return null;
        }

        var members = new uint[] { 11 };
        var script = new List<CrowdCommand>
        {
            new(2, new CrowdSimCommand { Kind = CrowdSimCommandKind.Order, Player = 1, ShapeId = "box", Members = members }),
            new(4, Spawn(2)),
        };
        queue.Schedule(session, script);
        script[0] = new CrowdCommand(2, Spawn(9));
        script.Add(new CrowdCommand(3, Spawn(8)));
        members[0] = 99;
        queue.Schedule(session, new List<CrowdCommand> { new(2, Spawn(3)), new(3, Spawn(4)) });
        int start = session.TickCount;
        while (session.TickCount <= start + 4)
        {
            queue.Flush(session, Exec);
            CrowdUatKit.Step(session);
        }

        string got = string.Join(",", ran.Select(r => $"{r.Tick - start}{r.Label}"));
        TestContext.Out.WriteLine($"[uat-defense-queue] order={got}");
        Assert.That(got, Is.EqualTo("2a11,2c,3d,4b"), "执行序应为 2a,2c,3d,4b（入队与调用方脱钩，按 tick 归并，同 tick 旧脚本先）");

        queue.Schedule(session, new List<CrowdCommand> { new(start + 1, Spawn(7)) });
        CrowdUatKit.Step(session);
        int now = session.TickCount;
        queue.Flush(session, Exec);
        var last = queue.Log[^1];
        TestContext.Out.WriteLine($"[uat-defense-queue] late ran={ran[^1].Tick} log={last.Tick} now={now}");
        Assert.That(ran[^1], Is.EqualTo((now, "late")), "迟到条目应在当前 tick 执行");
        Assert.That(last.Tick, Is.EqualTo(now), "迟到条目的日志 tick 应改成当前 tick");
    }

    private static readonly (string Label, Action<JsonObject> Spoil)[] BadLeaves =
    {
        ("planning.refreshLatencyTicks=1.5", c => c["planning"]!["refreshLatencyTicks"] = Num("1.5")),
        ("movement.maxStepNavCells=1.2", c => c["movement"]!["maxStepNavCells"] = Num("1.2")),
        ("movement.maxSubSteps=0", c => c["movement"]!["maxSubSteps"] = Num("0")),
        ("push.movingPriorityBonus=-1", c => c["push"]!["movingPriorityBonus"] = Num("-1")),
        ("movement.sightHysteresisScale=0.5", c => c["movement"]!["sightHysteresisScale"] = Num("0.5")),
        ("agents.avoidanceRadiusScale=0", c => c["agents"]!["avoidanceRadiusScale"] = Num("0")),
        ("spawn.groupSpacingScale=NaN", c => c["spawn"]!["groupSpacingScale"] = JsonValue.Create(double.NaN)),
        ("formations.box.spacingDiameterScale=0", c => c["formations"]!["box"]!["spacingDiameterScale"] = Num("0")),
        ("formations.box.aspect=-1", c => c["formations"]!["box"]!["aspect"] = Num("-1")),
    };

    [Test]
    public void BadConfigLeaves_AreRejectedByTheLoaders()
    {
        Assert.DoesNotThrow(() => S1SurfaceTruthTests.LoadRuntime(CrowdUatKit.DefaultSeed, false, null, null), "默认配置被拒");
        var accepted = new List<string>();
        foreach (var (label, spoil) in BadLeaves)
        {
            string? why = Rejection(() => S1SurfaceTruthTests.LoadRuntime(CrowdUatKit.DefaultSeed, false, null, spoil));
            TestContext.Out.WriteLine($"[uat-defense-config] {label} → {why ?? "接受"}");
            if (why == null) accepted.Add(label);
        }

        var profiles = (JsonArray)JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "agent_profiles.json")))!;
        profiles[0]!["radiusCm"] = null;
        string? radiusWhy = Rejection(() => new AgentProfileRegistry(
            JsonSerializer.Deserialize<List<AgentProfileConfig>>(profiles.ToJsonString(), StrictJsonOptions.CreateCamelCase())!));
        TestContext.Out.WriteLine($"[uat-defense-config] agent_profiles[0].radiusCm=null → {radiusWhy ?? "接受"}");
        if (radiusWhy == null) accepted.Add("agent_profiles[0].radiusCm=null");

        var agent = (JsonObject)JsonNode.Parse(
            """{"profileId":"foot_r100","speedCmPerSec":1400,"pushPriority":2,"autoDeploy":true}""")!;
        Assert.DoesNotThrow(() => CrowdUnitTemplateFacts.Parse("template", (JsonObject)agent.DeepClone()), "默认单位模板被拒");
        agent["pushPriority"] = "1";
        string? pushWhy = Rejection(() => CrowdUnitTemplateFacts.Parse("template", agent));
        TestContext.Out.WriteLine($"[uat-defense-config] CrowdSimulationAgent.pushPriority=\"1\" → {pushWhy ?? "接受"}");
        if (pushWhy == null) accepted.Add("CrowdSimulationAgent.pushPriority 字符串");

        TestContext.Out.WriteLine($"[uat-defense-config] cases={BadLeaves.Length + 2} accepted={accepted.Count}");
        Assert.That(accepted, Is.Empty, $"坏值未被拒：{string.Join("，", accepted)}");
    }

    [Test]
    public void NearestReachable_RingSearch_EqualsTheFullScan()
    {
        using var scene = CrowdUatKit.Scene();
        var session = scene.Session;
        var planner = session.Planner!;
        uint seed = 7;
        var errors = new List<string>();
        int cases = 0;
        foreach (var nav in session.Navs.OrderBy(kv => kv.Key).Select(kv => kv.Value))
        {
            int side = nav.NavCellsPerSide, total = side * side;
            int Rnd()
            {
                seed = unchecked(seed * 1103515245u + 12345u);
                return (int)(seed % (uint)total);
            }

            for (int k = 0; k < NearestCasesPerNav; k++)
            {
                int target = Rnd();
                int s = CrowdDeployment.NearestPassable(nav, Rnd());
                int from = k == 0 ? -1 : s >= 0 ? nav.Comp[s] : -1;
                int ring = planner.NearestReachable(nav, from, target, side);
                int scan = from < 0 ? -1 : FullScan(nav, from, target);
                cases++;
                if (ring != scan && errors.Count < 5) errors.Add($"nav {nav.Id} target {target} from {from}：环搜 {ring} ≠ 全扫 {scan}");
            }
        }

        TestContext.Out.WriteLine($"[uat-defense-nearest] navs={session.Navs.Count} cases={cases} mismatches={errors.Count}");
        Assert.That(errors, Is.Empty, string.Join("；", errors));
    }

    [Test]
    public void FogMixedLineOrder_PathJobsStayInTheReplanShape()
    {
        using var scene = CrowdUatKit.Scene();
        var session = scene.Session;
        session.Commands.Submit<object?>(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.Spawn, Count = PerfUnits }, CrowdSimCommands.Exec);
        session.Commands.Submit<object?>(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.FogSight, On = true }, CrowdSimCommands.Exec);
        CrowdUatKit.Step(session, PerfWarmTicks);

        var buffer = new uint[session.Units.Count];
        int count = session.Units.CopyOwnHandles(1, buffer);
        int far = (int)(scene.WorldCm * 9 / 10).ToLong();
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = 1,
            XCm = far,
            YCm = far,
            ShapeId = "line",
            HasAuto = true,
            Auto = true,
            WidthCm = PerfLineWidthCm,
            HasFogTerrain = true,
            FogTerrain = true,
            Members = buffer.AsSpan(0, count).ToArray(),
        }, CrowdSimCommands.Exec);
        CrowdUatKit.Step(session, PerfTicks);

        var planner = session.Planner!;
        long plans = session.Telemetry.Plans, refreshes = planner.FlowRefreshSubmits, extends = planner.ExtendSubmits;
        long jobs = plans + refreshes + extends;
        TestContext.Out.WriteLine(
            $"[uat-defense-perf] units={session.Units.Count} ordered={count} jobs={jobs} plans={plans} refreshes={refreshes} extends={extends}");
        Assert.That(jobs, Is.GreaterThan(MinPlans), $"mixed {PerfTicks} tick 路径任务 {jobs} ≤ {MinPlans}（重排形态缺失）");
        Assert.That(jobs, Is.LessThanOrEqualTo(MaxPlans), $"mixed {PerfTicks} tick 路径任务 {jobs} > {MaxPlans}（重排风暴）");
    }

    private sealed record LatencyRun(ulong[] Trace, List<CrowdSimPartition> Partitions, int Stalls, int JobStalls);

    private static LatencyRun RunWithDelay(string id, bool delayed)
    {
        using var scene = CrowdUatKit.Scene();
        var session = scene.Session;
        var (script, ticks) = LatencyScript(id, (int)scene.WorldCm.ToLong());
        var reference = CrowdReferenceScript.Load(script, session.Units.Capacity);
        reference.ValidateStructure(session);
        if (delayed)
        {
            session.BlockOnDueReplies = false;
            scene.Service.TestDelayPerJob = ReplyDelay;
        }

        var trace = new ulong[ticks];
        int stalls = 0, jobStalls = 0;
        var clock = Stopwatch.StartNew();
        for (int t = 0; t < ticks; t++)
        {
            ulong hash;
            while (!reference.Step(session, out hash))
            {
                stalls++;
                if (session.RebakeJob != null) jobStalls++;
                if (clock.Elapsed > RunBudget) throw new InvalidOperationException($"{id}：延迟档停摆不收敛（tick {session.TickCount}）");
                Thread.Yield();
            }

            trace[t] = hash;
        }

        var partitions = new List<CrowdSimPartition>();
        CrowdSimPartitions.Copy(session, partitions);
        return new LatencyRun(trace, partitions, stalls, jobStalls);
    }

    private static (JsonArray Script, int Ticks) LatencyScript(string id, int w)
    {
        int c = w / 2;
        var rows = new JsonArray();
        void Add(int tick, JsonObject cmd) => rows.Add(new JsonObject { ["tick"] = tick, ["cmd"] = cmd });
        JsonObject Place(int x, int y, int size) => new()
        {
            ["type"] = "placeStructure", ["template"] = "building", ["xCm"] = x, ["yCm"] = y, ["sizeCm"] = size,
        };

        switch (id)
        {
            case "deploy":
                Add(0, new JsonObject { ["type"] = "spawn", ["count"] = 12000 });
                Add(0, new JsonObject { ["type"] = "scatter" });
                Add(300, new JsonObject { ["type"] = "scatter" });
                return (rows, 600);
            case "rebake":
                Add(0, new JsonObject { ["type"] = "spawn", ["count"] = 12000 });
                Add(0, new JsonObject { ["type"] = "scatter" });
                Add(60, Place(c, c, 80000));
                Add(120, Place(c - 200000, c + 150000, 50000));
                Add(180, Place(c + 250000, c - 180000, 120000));
                Add(240, new JsonObject { ["type"] = "removeStructureAt", ["xCm"] = c, ["yCm"] = c });
                Add(300, new JsonObject { ["type"] = "scatter" });
                Add(360, new JsonObject { ["type"] = "removeStructureAt", ["xCm"] = c + 250000, ["yCm"] = c - 180000 });
                return (rows, 480);
            case "march":
                Add(1, new JsonObject
                {
                    ["type"] = "spawnAt", ["player"] = 1, ["xCm"] = w * 3 / 10, ["yCm"] = w * 3 / 10, ["count"] = 300, ["template"] = "crowd_simulation.unit.infantry.r100",
                });
                Add(2, new JsonObject { ["type"] = "clearSelection" });
                Add(2, new JsonObject
                {
                    ["type"] = "order", ["player"] = 1, ["xCm"] = w * 6 / 10, ["yCm"] = w * 6 / 10, ["shape"] = "box", ["auto"] = false,
                });
                for (int k = 0; k < 40; k++)
                {
                    Add(10 + k * 2, Place((int)((long)w * (330 + 6 * k) / 1000), (int)((long)w * (340 + 6 * k) / 1000), w * 4 / 1000));
                }

                return (rows, 120);
            default:
                throw new ArgumentOutOfRangeException(nameof(id), id, null);
        }
    }

    private static CrowdSimCommand Spawn(int label) => new() { Kind = CrowdSimCommandKind.Spawn, Count = label };

    private static string Label(CrowdSimCommand cmd) => cmd.Kind == CrowdSimCommandKind.Order
        ? $"a{cmd.Members![0]}"
        : cmd.Count switch { 2 => "b", 3 => "c", 4 => "d", 7 => "late", 8 => "Y", 9 => "X", _ => $"?{cmd.Count}" };

    private static JsonNode Num(string text) => JsonNode.Parse(text)!;

    private static string? Rejection(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static int FullScan(NavContext nav, int from, int target)
    {
        int side = nav.NavCellsPerSide, tx = target % side, ty = target / side;
        int best = -1;
        long bd = long.MaxValue;
        for (int c = 0; c < side * side; c++)
        {
            if (nav.Passable[c] == 0 || !nav.CanReach(from, nav.Comp[c])) continue;
            long dx = c % side - tx, dy = c / side - ty, d = dx * dx + dy * dy;
            if (d < bd)
            {
                bd = d;
                best = c;
            }
        }

        return best;
    }
}
