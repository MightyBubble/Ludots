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
        var watch = new Stopwatch();
        Advance(session, watch, WarmupTicks, null);
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
        Advance(session, watch, MeasuredTicks, elapsed);
        string loadAfter = File.ReadAllText("/proc/loadavg").Trim();
        Assert.That(session.Units.Count, Is.EqualTo(Units), "基准单位数");
        Assert.That(session.TruthNavFrozen, Is.False);
        Assert.That(session.Telemetry.FogRefreshes, Is.GreaterThan(0), "计时窗必须走过迷雾刷新");
        Array.Sort(elapsed);
        double avg = 0;
        foreach (var v in elapsed) avg += v;
        avg /= MeasuredTicks;
        double p50 = elapsed[MeasuredTicks / 2];
        double p95 = elapsed[(int)(MeasuredTicks * 0.95)];
        double max = elapsed[^1];

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
            $"- 硬门(仅优化构建):P95 < {BudgetMs:F0} ms\n");
        TestContext.Out.WriteLine(
            $"50k tick: avg={avg:F4}ms p50={p50:F4}ms p95={p95:F4}ms max={max:F4}ms fog={session.Telemetry.FogRefreshes} workers={session.Config.Sim.ParallelWorkerCount} loadAtWindow={loadAtWindow} loadWaitMs={loadWait.ElapsedMilliseconds} loadAfter={loadAfter}");
#if DEBUG
        Assert.Inconclusive(
            $"Debug 不测 33 ms 门。avg={avg:F4} p50={p50:F4} p95={p95:F4} max={max:F4}");
#else
        Assert.That(p95, Is.LessThan(BudgetMs),
            $"50k 稳态 P95 {p95:F4} ms,平均 {avg:F4} ms,最大 {max:F4} ms,未低于 {BudgetMs:F0} ms");
#endif
    }

    private static void Advance(CrowdSimSession session, Stopwatch watch, int count, double[]? elapsed)
    {
        int ran = 0, guard = 64 * count + 64;
        while (ran < count)
        {
            if (guard-- <= 0) throw new InvalidOperationException("基准推进停摆过长。");
            watch.Restart();
            bool advanced = session.Step(out _);
            watch.Stop();
            if (!advanced) continue;
            if (elapsed != null) elapsed[ran] = watch.Elapsed.TotalMilliseconds;
            ran++;
        }
    }

    private static double Load1Minute(string loadavg)
        => double.Parse(loadavg.Split(' ', 2)[0], CultureInfo.InvariantCulture);
}
