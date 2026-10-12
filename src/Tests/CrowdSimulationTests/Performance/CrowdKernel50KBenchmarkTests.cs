using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 50k 单位 tick 门(种子 1337,spawnAt 50000,远点行军,200 tick 稳态窗)。
/// 迷雾不冻结:计时含运动内核、迷雾盖章、认知同步和校验码。
/// P95 必须低于 33 ms。均值、P50、最大和当时的 load average 写入
/// artifacts/benchmarks/crowd-kernel-50k-tick/。
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

        var script = new JsonArray
        {
            new JsonObject { ["tick"] = 0, ["cmd"] = new JsonObject { ["type"] = "spawnAt", ["player"] = 1, ["xCm"] = 480000, ["yCm"] = 560000, ["count"] = Units, ["unitType"] = 0, ["rIdx"] = 0 } },
            new JsonObject { ["tick"] = 2, ["cmd"] = new JsonObject { ["type"] = "order", ["player"] = 1, ["xCm"] = 1200000, ["yCm"] = 800000, ["shape"] = "box" } },
        };
        var commands = new List<CrowdCommand>();
        foreach (var e in script)
        {
            commands.Add(new CrowdCommand(e!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(e!["cmd"]!)));
        }

        session.Commands.Schedule(session, commands);

        var elapsed = new double[MeasuredTicks];
        string loadBefore = File.ReadAllText("/proc/loadavg").Trim();
        var watch = Stopwatch.StartNew();
        int ran = 0, guard = 64 * (WarmupTicks + MeasuredTicks) + 64;
        while (ran < WarmupTicks + MeasuredTicks)
        {
            if (guard-- <= 0) throw new InvalidOperationException("基准推进停摆过长。");
            watch.Restart();
            bool advanced = session.Step(out _);
            watch.Stop();
            if (!advanced) continue;
            if (ran >= WarmupTicks) elapsed[ran - WarmupTicks] = watch.Elapsed.TotalMilliseconds;
            ran++;
        }

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
            $"{{\"units\":{Units},\"ticks\":{MeasuredTicks},\"avgMs\":{avg:F4},\"p50Ms\":{p50:F4},\"p95Ms\":{p95:F4},\"maxMs\":{max:F4},\"budgetMs\":{BudgetMs},\"fogFrozen\":false,\"fogRefreshes\":{session.Telemetry.FogRefreshes},\"workers\":{session.Config.Sim.ParallelWorkerCount},\"loadBefore\":\"{loadBefore}\",\"loadAfter\":\"{loadAfter}\"}}\n");
        File.WriteAllText(Path.Combine(dir, "benchmark-report.md"),
            $"# 50k 单位 tick 门(种子 1337,spawnAt 50000,远点行军,{MeasuredTicks} tick 稳态窗)\n\n" +
            $"| 指标 | 毫秒/tick |\n|---|---|\n| 平均 | {avg:F4} |\n| P50 | {p50:F4} |\n| P95 | {p95:F4} |\n| 最大 | {max:F4} |\n\n" +
            $"- 单位 {Units},预热 {WarmupTicks} tick(计时窗不含生成与下令)\n" +
            $"- Release,迷雾不冻结,计时是推进成功的 session.Step(运动、迷雾、认知同步、校验码)\n" +
            $"- 迷雾刷新 {session.Telemetry.FogRefreshes} 次,并行工人 {session.Config.Sim.ParallelWorkerCount}\n" +
            $"- load average 测前 `{loadBefore}`,测后 `{loadAfter}`\n" +
            $"- 硬门:P95 < {BudgetMs:F0} ms\n");
        TestContext.Out.WriteLine(
            $"50k tick: avg={avg:F4}ms p50={p50:F4}ms p95={p95:F4}ms max={max:F4}ms fog={session.Telemetry.FogRefreshes} workers={session.Config.Sim.ParallelWorkerCount} loadBefore={loadBefore} loadAfter={loadAfter}");
        Assert.That(p95, Is.LessThan(BudgetMs),
            $"50k 稳态 P95 {p95:F4} ms,平均 {avg:F4} ms,最大 {max:F4} ms,未低于 {BudgetMs:F0} ms");
    }
}
