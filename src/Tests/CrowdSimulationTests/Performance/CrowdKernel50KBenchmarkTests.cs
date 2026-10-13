using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 50k 单位 tick 门(种子 1337,spawn 按 deploy.bases 分布,每座基地各下一令远点行军,200 tick 稳态窗)。
/// 迷雾不冻结:计时含运动内核、迷雾盖章、认知同步和校验码。
/// 硬门只在优化构建上生效:P95 必须低于 33 ms。Debug 只把数字写进产物,然后记为未测。
/// 测量窗开始时读 /proc/loadavg 的 1 分钟负载。高于 Environment.ProcessorCount 就每 5 秒再读,
/// 最多等 180 秒;到期仍高也开测,不重跑。产物记下实际开测时的负载。
/// </summary>
public sealed class CrowdKernel50KBenchmarkTests
{
    private const int Units = 50000;
    private const int WarmupTicks = 40;
    private const int MeasuredTicks = 200;
    private const double BudgetMs = 33.0;

    [Test]
    public void CrowdKernel_50K_TickTiming()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;

        var bases = session.Config.Deploy.Bases;
        var script = new JsonArray
        {
            new JsonObject { ["tick"] = 0, ["cmd"] = new JsonObject { ["type"] = "spawn", ["count"] = Units } },
        };
        for (int i = 0; i < bases.Count; i++)
        {
            script.Add(new JsonObject
            {
                ["tick"] = 2,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "order",
                    ["player"] = bases[i].PlayerId,
                    ["xCm"] = 1200000,
                    ["yCm"] = 800000,
                    ["shape"] = "box",
                },
            });
        }
        var commands = new List<CrowdCommand>();
        foreach (var e in script)
        {
            commands.Add(new CrowdCommand(e!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(e!["cmd"]!)));
        }

        session.Commands.Schedule(session, commands);

        var elapsed = new double[MeasuredTicks];
        var phases = new CrowdSimulationPhaseTicks[MeasuredTicks];
        var notes = new TickNote[MeasuredTicks];
        var watch = new Stopwatch();
        Advance(session, watch, WarmupTicks, null, null, null);
        // 1 分钟负载高于核数时，预热里的工人还压在平均上。等到它降下来再开窗，不把整段重跑一遍。
        string loadAtWindow = File.ReadAllText("/proc/loadavg").Trim();
        double load1 = Load1Minute(loadAtWindow);
        int cores = Environment.ProcessorCount;
        var loadWait = Stopwatch.StartNew();
        while (load1 > cores && loadWait.ElapsedMilliseconds < 180_000)
        {
            Thread.Sleep(5000);
            loadAtWindow = File.ReadAllText("/proc/loadavg").Trim();
            load1 = Load1Minute(loadAtWindow);
        }

        loadWait.Stop();
        Advance(session, watch, MeasuredTicks, elapsed, phases, notes);
        string loadAfter = File.ReadAllText("/proc/loadavg").Trim();
        Assert.That(session.Units.Count, Is.EqualTo(Units), "基准单位数");
        Assert.That(session.TruthNavFrozen, Is.False);
        Assert.That(session.Telemetry.FogRefreshes, Is.GreaterThan(0), "计时窗必须走过迷雾刷新");
        var order = new int[MeasuredTicks];
        for (int i = 0; i < MeasuredTicks; i++) order[i] = i;
        Array.Sort(order, (a, b) => elapsed[a].CompareTo(elapsed[b]));
        double avg = 0;
        foreach (var v in elapsed) avg += v;
        avg /= MeasuredTicks;
        double p50 = elapsed[order[MeasuredTicks / 2]];
        int p95Rank = (int)(MeasuredTicks * 0.95);
        double p95 = elapsed[order[p95Rank]];
        double max = elapsed[order[^1]];
        var phaseAvg = AveragePhases(phases);
        string phaseAvgLine = FormatPhases(phaseAvg, MeasuredTicks);
        string p95Line = FormatPhases(phases[order[p95Rank]], 1);
        string maxLine = FormatPhases(phases[order[^1]], 1);
        string spikeLine = DescribeSpike(order, elapsed, phases, notes, p95Rank);

        string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "artifacts", "benchmarks", "crowd-kernel-50k-tick"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "trace.jsonl"),
            $"{{\"units\":{Units},\"ticks\":{MeasuredTicks},\"avgMs\":{avg:F4},\"p50Ms\":{p50:F4},\"p95Ms\":{p95:F4},\"maxMs\":{max:F4},\"budgetMs\":{BudgetMs},\"fogFrozen\":false,\"fogRefreshes\":{session.Telemetry.FogRefreshes},\"workers\":{session.Config.Sim.ParallelWorkerCount},\"spawn\":\"deploy.bases\",\"loadAtWindow\":\"{loadAtWindow}\",\"load1\":{load1.ToString("F2", CultureInfo.InvariantCulture)},\"cores\":{cores},\"loadWaitMs\":{loadWait.ElapsedMilliseconds},\"loadAfter\":\"{loadAfter}\"}}\n");
        File.WriteAllText(Path.Combine(dir, "benchmark-report.md"),
            $"# 50k 单位 tick 门(种子 1337,deploy.bases 分布,每座基地远点行军,{MeasuredTicks} tick 稳态窗)\n\n" +
            $"| 指标 | 毫秒/tick |\n|---|---|\n| 平均 | {avg:F4} |\n| P50 | {p50:F4} |\n| P95 | {p95:F4} |\n| 最大 | {max:F4} |\n\n" +
            $"- 单位 {Units},按 deploy.bases 分布,预热 {WarmupTicks} tick(计时窗不含生成与下令)\n" +
            $"- Release,迷雾不冻结,计时是推进成功的 session.Step(运动、迷雾、认知同步、校验码)\n" +
            $"- 迷雾刷新 {session.Telemetry.FogRefreshes} 次,并行工人 {session.Config.Sim.ParallelWorkerCount}\n" +
            $"- 开测时 1 分钟 load {load1.ToString("F2", CultureInfo.InvariantCulture)},核数 {cores},等待 {loadWait.ElapsedMilliseconds} ms;load average 开测 `{loadAtWindow}`,测后 `{loadAfter}`\n" +
            $"- 开测规则:1 分钟负载高于核数则每 5 秒再读,最多 180 秒,到期仍高也计时,不重跑\n" +
            $"- 硬门(仅优化构建):P95 < {BudgetMs:F0} ms\n" +
            $"- 子步 {session.SubSteps}\n" +
            $"- 阶段平均(毫秒/tick): {phaseAvgLine}\n" +
            $"- P95 那一 tick 的阶段: {p95Line}\n" +
            $"- 最慢 tick 的阶段: {maxLine}\n" +
            $"- {spikeLine}\n");
        TestContext.Out.WriteLine(
            $"50k tick: avg={avg:F4}ms p50={p50:F4}ms p95={p95:F4}ms max={max:F4}ms fog={session.Telemetry.FogRefreshes} workers={session.Config.Sim.ParallelWorkerCount} substeps={session.SubSteps} loadAtWindow={loadAtWindow} loadWaitMs={loadWait.ElapsedMilliseconds} loadAfter={loadAfter}");
        TestContext.Out.WriteLine($"50k phases avg: {phaseAvgLine}");
        TestContext.Out.WriteLine($"50k phases p95: {p95Line}");
        TestContext.Out.WriteLine($"50k phases max: {maxLine}");
        TestContext.Out.WriteLine($"50k spike: {spikeLine}");
#if DEBUG
        Assert.Inconclusive(
            $"Debug 不测 33 ms 门。avg={avg:F4} p50={p50:F4} p95={p95:F4} max={max:F4}");
#else
        Assert.That(p95, Is.LessThan(BudgetMs),
            $"50k 稳态 P95 {p95:F4} ms,平均 {avg:F4} ms,最大 {max:F4} ms,未低于 {BudgetMs:F0} ms");
#endif
    }

    private static void Advance(
        CrowdSimSession session,
        Stopwatch watch,
        int count,
        double[]? elapsed,
        CrowdSimulationPhaseTicks[]? phases,
        TickNote[]? notes)
    {
        int ran = 0, guard = 64 * count + 64;
        while (ran < count)
        {
            if (guard-- <= 0) throw new InvalidOperationException("基准推进停摆过长。");
            int fog0 = (int)session.Telemetry.FogRefreshes;
            int plans0 = (int)session.Telemetry.Plans;
            int rebakes0 = (int)session.Telemetry.Rebakes;
            int hash0 = session.Movement?.Hash.Reused ?? 0;
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            long alloc0 = GC.GetTotalAllocatedBytes(precise: false);
            watch.Restart();
            bool advanced = session.Step(out _);
            watch.Stop();
            if (!advanced) continue;
            if (elapsed != null) elapsed[ran] = watch.Elapsed.TotalMilliseconds;
            if (phases != null) phases[ran] = session.Telemetry.LastPhaseTicks;
            if (notes != null)
            {
                notes[ran] = new TickNote
                {
                    Gc0 = GC.CollectionCount(0) - gc0,
                    Gc1 = GC.CollectionCount(1) - gc1,
                    Gc2 = GC.CollectionCount(2) - gc2,
                    Alloc = GC.GetTotalAllocatedBytes(precise: false) - alloc0,
                    Fog = (int)session.Telemetry.FogRefreshes - fog0,
                    Plans = (int)session.Telemetry.Plans - plans0,
                    Rebakes = (int)session.Telemetry.Rebakes - rebakes0,
                    HashReused = (session.Movement?.Hash.Reused ?? 0) - hash0,
                };
            }

            ran++;
        }
    }

    private struct TickNote
    {
        public int Gc0;
        public int Gc1;
        public int Gc2;
        public long Alloc;
        public int Fog;
        public int Plans;
        public int Rebakes;
        public int HashReused;
    }

    private static CrowdSimulationPhaseTicks AveragePhases(CrowdSimulationPhaseTicks[] phases)
    {
        var sum = default(CrowdSimulationPhaseTicks);
        for (int i = 0; i < phases.Length; i++) sum.Accumulate(in phases[i]);
        return sum;
    }

    private static string FormatPhases(CrowdSimulationPhaseTicks ticks, int divisor)
    {
        double d = divisor;
        return
            $"motor={Ms(ticks.Motor) / d:F3} intent={Ms(ticks.Intent) / d:F3} sep={Ms(ticks.Separation) / d:F3} " +
            $"hash={Ms(ticks.Hash) / d:F3} sum={Ms(ticks.Checksum) / d:F3} gather={Ms(ticks.Gather) / d:F3} " +
            $"scatter={Ms(ticks.Scatter) / d:F3} fog={Ms(ticks.Fog) / d:F3} belief={Ms(ticks.Belief) / d:F3} " +
            $"plan={Ms(ticks.Plan) / d:F3} leader={Ms(ticks.Leader) / d:F3} refresh={Ms(ticks.Refresh) / d:F3} " +
            $"cmd={Ms(ticks.Command) / d:F3} rebake={Ms(ticks.Rebake) / d:F3} accounted={Ms(ticks.Accounted) / d:F3} " +
            $"parWork={Ms(ticks.ParallelWork) / d:F3} parWall={Ms(ticks.ParallelWall) / d:F3} " +
            $"gWall={Ms(ticks.GatherWall) / d:F3} gWork={Ms(ticks.GatherWork) / d:F3} " +
            $"sWall={Ms(ticks.SolveWall) / d:F3} sWork={Ms(ticks.SolveWork) / d:F3} " +
            $"iWall={Ms(ticks.IntentWall) / d:F3} iWork={Ms(ticks.IntentWork) / d:F3} " +
            $"mWall={Ms(ticks.MotorWall) / d:F3} mWork={Ms(ticks.MotorWork) / d:F3}";
    }

    private static string DescribeSpike(
        int[] order,
        double[] elapsed,
        CrowdSimulationPhaseTicks[] phases,
        TickNote[] notes,
        int p95Rank)
    {
        int n = order.Length;
        int slow = Math.Max(1, n / 10);
        int gc0All = 0, gc0Slow = 0, gc1Slow = 0, gc2Slow = 0;
        long allocSlow = 0;
        int fogSlow = 0, plansSlow = 0, rebakeSlow = 0, hashReuseSlow = 0;
        for (int i = 0; i < n; i++)
        {
            if (notes[i].Gc0 > 0) gc0All++;
        }

        for (int r = n - slow; r < n; r++)
        {
            var note = notes[order[r]];
            if (note.Gc0 > 0) gc0Slow++;
            if (note.Gc1 > 0) gc1Slow++;
            if (note.Gc2 > 0) gc2Slow++;
            allocSlow += note.Alloc;
            fogSlow += note.Fog;
            plansSlow += note.Plans;
            rebakeSlow += note.Rebakes;
            hashReuseSlow += note.HashReused;
        }

        var p95 = phases[order[p95Rank]];
        var peak = phases[order[^1]];
        var mid = phases[order[n / 2]];
        string p95Excess = Excess(p95, mid);
        string maxExcess = Excess(peak, mid);
        var peakNote = notes[order[^1]];
        return
            $"slowest {slow}: gc0 {gc0Slow}/{slow} (window gc0 ticks {gc0All}/{n}) gc1 {gc1Slow} gc2 {gc2Slow} " +
            $"alloc {allocSlow} fog+ {fogSlow} plans+ {plansSlow} rebake+ {rebakeSlow} hashReuse {hashReuseSlow}; " +
            $"p95 excess vs p50 [{p95Excess}]; max excess vs p50 [{maxExcess}]; " +
            $"max tick gc0={peakNote.Gc0} gc1={peakNote.Gc1} gc2={peakNote.Gc2} alloc={peakNote.Alloc} fog={peakNote.Fog} plans={peakNote.Plans} rebake={peakNote.Rebakes} hashReuse={peakNote.HashReused} wall={elapsed[order[^1]]:F3}";
    }

    private static string Excess(CrowdSimulationPhaseTicks slow, CrowdSimulationPhaseTicks mid)
    {
        (string Name, double Delta)[] rows =
        {
            ("motor", Ms(slow.Motor - mid.Motor)),
            ("intent", Ms(slow.Intent - mid.Intent)),
            ("sep", Ms(slow.Separation - mid.Separation)),
            ("hash", Ms(slow.Hash - mid.Hash)),
            ("sum", Ms(slow.Checksum - mid.Checksum)),
            ("gather", Ms(slow.Gather - mid.Gather)),
            ("scatter", Ms(slow.Scatter - mid.Scatter)),
            ("fog", Ms(slow.Fog - mid.Fog)),
            ("belief", Ms(slow.Belief - mid.Belief)),
            ("plan", Ms(slow.Plan - mid.Plan)),
            ("leader", Ms(slow.Leader - mid.Leader)),
            ("refresh", Ms(slow.Refresh - mid.Refresh)),
            ("parWall", Ms(slow.ParallelWall - mid.ParallelWall)),
        };
        Array.Sort(rows, static (a, b) => b.Delta.CompareTo(a.Delta));
        return $"{rows[0].Name} {rows[0].Delta:+0.000;-0.000} {rows[1].Name} {rows[1].Delta:+0.000;-0.000} {rows[2].Name} {rows[2].Delta:+0.000;-0.000}";
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static double Load1Minute(string loadavg)
        => double.Parse(loadavg.Split(' ', 2)[0], CultureInfo.InvariantCulture);
}
