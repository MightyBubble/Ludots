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
/// 50k 单位 tick 基准(自造场景:同图种 1337,spawnAt 50000 → 远点行军,200 tick 稳态窗):
/// 逐 tick 墙钟计时,输出 平均/P50/P95/最大。数字写入 artifacts/benchmarks/
/// crowd-kernel-50k-tick/(报告 md + trace.jsonl),供改造前后对照;场景确定(种子/脚本
/// 固定),计时允许机器噪声——前后对照需同机同窗。
/// </summary>
public sealed class CrowdKernel50KBenchmarkTests
{
    private const int Units = 50000;
    private const int WarmupTicks = 40;
    private const int MeasuredTicks = 200;

    [Test]
    public void CrowdKernel_50K_TickTiming()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true;

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

        Assert.That(session.Units.Count, Is.EqualTo(Units), "基准单位数");
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
            $"{{\"units\":{Units},\"ticks\":{MeasuredTicks},\"avgMs\":{avg:F4},\"p50Ms\":{p50:F4},\"p95Ms\":{p95:F4},\"maxMs\":{max:F4}}}\n");
        File.WriteAllText(Path.Combine(dir, "benchmark-report.md"),
            $"# 50k 单位 tick 基准(自造场景:种子 1337,spawnAt 50000 → 远点行军,{MeasuredTicks} tick 稳态窗)\n\n" +
            $"| 指标 | 毫秒/tick |\n|---|---|\n| 平均 | {avg:F4} |\n| P50 | {p50:F4} |\n| P95 | {p95:F4} |\n| 最大 | {max:F4} |\n\n" +
            $"- 单位 {Units},预热 {WarmupTicks} tick(计时窗不含生成/下令)\n" +
            $"- 逐 tick 墙钟(含停摆重试开销),前后对照需同机同窗\n");
        TestContext.Out.WriteLine(
            $"50k tick: avg={avg:F4}ms p50={p50:F4}ms p95={p95:F4}ms max={max:F4}ms (units={session.Units.Count})");
    }
}
