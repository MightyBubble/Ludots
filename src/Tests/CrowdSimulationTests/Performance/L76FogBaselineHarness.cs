using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 换实现之前的迷雾采样。显式点名才跑，写出 Baselines/l76-fog.json。
/// 头记录当时的提交、uname -srm、nproc、dotnet --version。
/// 行为行是 S7 雾脚本和回收脚本（种子 s1337），每个 Period 记每组可见格数和已探索格数。
/// 性能行是雾开着的 12k / 50k，各 5 次，取中位；分配是同一 200 tick 稳态窗的本线程字节。
/// </summary>
[TestFixture]
[Explicit]
[Category("L76Baseline")]
public sealed class L76FogBaselineHarness
{
    private const int WarmupTicks = 40;
    private const int MeasuredTicks = 200;
    private const int PerfRepeats = 5;

    [Test]
    public void WriteL76FogBaseline()
    {
        string root = RepoRoot();
        var doc = new JsonObject
        {
            ["commit"] = Git(root, "rev-parse HEAD"),
            ["uname"] = Run("uname", "-srm"),
            ["nproc"] = int.Parse(Run("nproc", "")),
            ["dotnet"] = Run("dotnet", "--version"),
            ["fog"] = SampleTruth("s7fog-truth.json"),
            ["recycle"] = SampleTruth("s7crecycle-truth.json"),
            ["share"] = SampleShare(),
            ["perf"] = SamplePerf(),
        };

        string path = Path.Combine(root, "src", "Tests", "CrowdSimulationTests", "Baselines", "l76-fog.json");
        if (File.Exists(path) && Environment.GetEnvironmentVariable("L76_OVERWRITE_BASELINE") != "1")
        {
            throw new InvalidOperationException("基线已在。设置 L76_OVERWRITE_BASELINE=1 才会覆盖。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        TestContext.Out.WriteLine(path);
        TestContext.Out.WriteLine(doc["perf"]!.ToJsonString());
    }

    private static JsonObject SampleTruth(string truthName)
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;

        string dir = Path.Combine(Parity.S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, truthName)))!;
        var script = new List<CrowdCommand>();
        foreach (var entry in meta["script"]!.AsArray())
        {
            script.Add(new CrowdCommand(entry!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(entry["cmd"]!)));
        }

        session.Commands.Schedule(session, script);
        var fog = session.Fog!;
        int period = fog.Period;
        int groups = fog.VisionGroupCount;
        int ticks = meta["ticks"]!.GetValue<int>();
        var samples = new JsonArray();
        for (int t = 0; t < ticks; t++)
        {
            Step(session, t);
            if (t % period != 0) continue;
            samples.Add(Counts(fog, t));
        }

        return new JsonObject
        {
            ["period"] = period,
            ["groups"] = groups,
            ["samples"] = samples,
            ["exploredEnd"] = Explored(fog),
        };
    }

    private static JsonObject SampleShare()
    {
        var demo = RunShare(TestDefaults.DemoPairRules());
        var oneWay = RunShare(OneWayShares());
        return new JsonObject
        {
            ["bidirectional"] = PairReport(demo, playerA: 1, playerB: 3, includeOnlyA: false),
            ["noRelation"] = PairReport(demo, playerA: 1, playerB: 2, includeOnlyA: false),
            ["oneWay"] = PairReport(oneWay, playerA: 1, playerB: 2, includeOnlyA: true),
        };
    }

    private readonly struct ShareRun
    {
        public ShareRun(CrowdPairRules rules, CrowdFog fog)
        {
            Rules = rules;
            Fog = fog;
        }

        public readonly CrowdPairRules Rules;
        public readonly CrowdFog Fog;
    }

    private static ShareRun RunShare(CrowdPairRules rules)
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337", rules);
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            new(0, CrowdSimCommand.Parse(JsonNode.Parse(
                """{"type":"spawnAt","player":1,"xCm":480000,"yCm":560000,"count":16,"unitType":0,"rIdx":0}""")!)),
        });

        var fog = session.Fog!;
        for (int t = 0; t < fog.Period * 2; t++) Step(session, t);
        return new ShareRun(rules, fog);
    }

    private static JsonObject PairReport(ShareRun run, int playerA, int playerB, bool includeOnlyA)
    {
        int groupA = run.Fog.GroupOf[run.Rules.IndexByPlayerId[playerA]];
        int groupB = run.Fog.GroupOf[run.Rules.IndexByPlayerId[playerB]];
        var cellsA = VisibleCells(run.Fog, groupA);
        var cellsB = VisibleCells(run.Fog, groupB);
        var setB = new HashSet<int>(cellsB);
        var setA = new HashSet<int>(cellsA);
        int symmetric = 0;
        int inBoth = 0;
        var onlyA = new JsonArray();
        foreach (int cell in cellsA)
        {
            if (setB.Contains(cell)) inBoth++;
            else
            {
                symmetric++;
                onlyA.Add(cell);
            }
        }

        foreach (int cell in cellsB)
        {
            if (!setA.Contains(cell)) symmetric++;
        }

        var doc = new JsonObject
        {
            ["playerA"] = playerA,
            ["playerB"] = playerB,
            ["groupA"] = groupA,
            ["groupB"] = groupB,
            ["sameGroup"] = groupA == groupB,
            ["symmetricDiff"] = symmetric,
            ["onlyAInB"] = groupA == groupB ? 0 : inBoth,
            ["cellsA"] = ToArray(cellsA),
            ["cellsB"] = ToArray(cellsB),
        };
        if (includeOnlyA) doc["onlyA"] = onlyA;
        return doc;
    }

    private static JsonObject SamplePerf()
    {
        var tick12 = new double[PerfRepeats];
        var tick50 = new double[PerfRepeats];
        var alloc12 = new long[PerfRepeats];
        var alloc50 = new long[PerfRepeats];
        for (int i = 0; i < PerfRepeats; i++)
        {
            (tick12[i], alloc12[i]) = Measure(12000);
            TestContext.Out.WriteLine($"12k run {i}: {tick12[i]:F4} ms alloc {alloc12[i]}");
        }

        for (int i = 0; i < PerfRepeats; i++)
        {
            (tick50[i], alloc50[i]) = Measure(50000);
            TestContext.Out.WriteLine($"50k run {i}: {tick50[i]:F4} ms alloc {alloc50[i]}");
        }

        return new JsonObject
        {
            ["repeats"] = PerfRepeats,
            ["warmupTicks"] = WarmupTicks,
            ["measuredTicks"] = MeasuredTicks,
            ["tick12kMs"] = ToArray(tick12),
            ["tick12kMedianMs"] = Median(tick12),
            ["steadyAlloc12k"] = ToArray(alloc12),
            ["steadyAlloc12kMedian"] = Median(alloc12),
            ["tick50kMs"] = ToArray(tick50),
            ["tick50kMedianMs"] = Median(tick50),
            ["steadyAlloc50k"] = ToArray(alloc50),
            ["steadyAlloc50kMedian"] = Median(alloc50),
        };
    }

    private static (double AvgMs, long AllocBytes) Measure(int units)
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(180), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            new(0, CrowdSimCommand.Parse(JsonNode.Parse(
                $"{{\"type\":\"spawnAt\",\"player\":1,\"xCm\":480000,\"yCm\":560000,\"count\":{units},\"unitType\":0,\"rIdx\":0}}")!)),
            new(2, CrowdSimCommand.Parse(JsonNode.Parse(
                """{"type":"order","player":1,"xCm":1200000,"yCm":800000,"shape":"box"}""")!)),
        });

        int ran = 0;
        int guard = 64 * (WarmupTicks + MeasuredTicks) + 64;
        var watch = new Stopwatch();
        double sum = 0;
        long alloc = 0;
        while (ran < WarmupTicks + MeasuredTicks)
        {
            if (guard-- <= 0) throw new InvalidOperationException($"{units} 基准推进停摆过长。");
            long beforeAlloc = 0;
            if (ran >= WarmupTicks) beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            bool advanced = session.Step(out _);
            watch.Stop();
            if (!advanced) continue;
            if (ran >= WarmupTicks)
            {
                sum += watch.Elapsed.TotalMilliseconds;
                alloc += GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
            }

            ran++;
        }

        if (session.Fog == null || session.Telemetry.FogRefreshes <= 0)
        {
            throw new InvalidOperationException($"{units} 计时窗没有迷雾刷新。");
        }

        return (sum / MeasuredTicks, alloc);
    }

    private static JsonObject Counts(CrowdFog fog, int tick)
    {
        return new JsonObject
        {
            ["tick"] = tick,
            ["visible"] = ExploredOrVisible(fog, visible: true),
            ["explored"] = ExploredOrVisible(fog, visible: false),
        };
    }

    private static JsonArray Explored(CrowdFog fog) => ExploredOrVisible(fog, visible: false);

    private static JsonArray ExploredOrVisible(CrowdFog fog, bool visible)
    {
        var row = new JsonArray();
        for (int g = 0; g < fog.VisionGroupCount; g++)
        {
            row.Add(visible ? fog.VisibleCountOf(g) : fog.ExploredCountOf(g));
        }
        return row;
    }

    private static List<int> VisibleCells(CrowdFog fog, int group)
    {
        int f2 = fog.FogCellsPerSide * fog.FogCellsPerSide;
        var cells = new List<int>();
        for (int c = 0; c < f2; c++)
        {
            if (fog.CellVisibilityOf(group, c) == CellVisibility.Visible) cells.Add(c);
        }

        return cells;
    }

    private static CrowdPairRules OneWayShares()
    {
        var response = (CrowdPushResponse[])TestDefaults.DemoResponse.Clone();
        var shares = new bool[16];
        for (int i = 0; i < 4; i++) shares[i * 4 + i] = true;
        shares[0 * 4 + 1] = true;
        return CrowdPairRules.Matrices(4, response, shares);
    }

    private static void Step(CrowdSimSession session, int tick)
    {
        if (session.Step(out _)) return;
        int guard = 0;
        while (!session.Step(out _))
        {
            if (guard++ > 4096) throw new InvalidOperationException($"tick {tick} 停摆未恢复。");
        }
    }

    private static JsonArray ToArray(List<int> values)
    {
        var row = new JsonArray();
        foreach (int value in values) row.Add(value);
        return row;
    }

    private static JsonArray ToArray(double[] values)
    {
        var row = new JsonArray();
        foreach (double value in values) row.Add(value);
        return row;
    }

    private static JsonArray ToArray(long[] values)
    {
        var row = new JsonArray();
        foreach (long value in values) row.Add(value);
        return row;
    }

    private static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static long Median(long[] values)
    {
        var copy = (long[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "showcase.registry.json"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到仓库根。");
    }

    private static string Git(string root, string args)
        => Run("git", $"-C {root} {args}");

    private static string Run(string file, string args)
    {
        using var process = Process.Start(new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"无法启动 {file}。");
        string stdout = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{file} {args} 退出 {process.ExitCode}: {process.StandardError.ReadToEnd()}");
        }

        return stdout;
    }
}
