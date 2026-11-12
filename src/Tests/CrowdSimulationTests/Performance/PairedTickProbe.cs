using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 同机对照用的探针。只调用基线提交上也有的入口，脚本会把这个文件拷进基线工作树再编译。
/// 单位数读环境变量 PROBE_UNITS。默认不进整套测试。
/// </summary>
[TestFixture, Explicit]
public sealed class PairedTickProbe
{
    [Test]
    public void Probe()
    {
        int units = int.Parse(Environment.GetEnvironmentVariable("PROBE_UNITS") ?? "12000");
        var runs = new double[5];
        long maxAlloc = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            (runs[i], long alloc) = Measure(units);
            if (alloc > maxAlloc) maxAlloc = alloc;
        }

        Array.Sort(runs);
        string runsText = string.Join(",", Array.ConvertAll(runs, static v => v.ToString("R", CultureInfo.InvariantCulture)));
        TestContext.Out.WriteLine(
            $"PROBE {units} median {runs[2].ToString("F4", CultureInfo.InvariantCulture)} runs {runsText} maxAlloc {maxAlloc}");
    }

    private static (double AvgMs, long AllocBytes) Measure(int units)
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(
            session.Navs, runtime, 1, TimeSpan.FromSeconds(180), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        TestDefaults.EnableFog(session);
        session.BlockOnDueReplies = true;
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            new(0, CrowdSimCommand.Parse(JsonNode.Parse(
                $"{{\"type\":\"spawnAt\",\"player\":1,\"xCm\":480000,\"yCm\":560000,\"count\":{units},\"template\":\"crowd_simulation.unit.infantry.r100\"}}")!)),
            new(2, CrowdSimCommand.Parse(JsonNode.Parse(
                """{"type":"order","player":1,"xCm":1200000,"yCm":800000,"shape":"box"}""")!)),
        });

        const int warmup = 40;
        const int measured = 200;
        int ran = 0;
        int guard = 64 * (warmup + measured) + 64;
        var watch = new Stopwatch();
        double sum = 0;
        long alloc = 0;
        while (ran < warmup + measured)
        {
            if (guard-- <= 0) throw new InvalidOperationException($"{units} 基准推进停摆过长。");
            long beforeAlloc = ran >= warmup ? GC.GetAllocatedBytesForCurrentThread() : 0;
            watch.Restart();
            bool advanced = session.Step(out _);
            watch.Stop();
            if (!advanced) continue;
            if (ran >= warmup)
            {
                sum += watch.Elapsed.TotalMilliseconds;
                alloc += GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
            }

            ran++;
        }

        if (session.Fog == null) throw new InvalidOperationException("no fog");
        return (sum / measured, alloc);
    }
}
