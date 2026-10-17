using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Diagnostics;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 全量 40 op 对拍的已知分歧锁定：把每种子每道门的首个分歧（门/tick/op/单位/指令/字段）钉成基线，
/// 任何提前（回归）或推迟（行为修复收线）都会变红，届时更新本基线并在提交说明里写明。
/// 完整分歧报告（含两端值与均值明细）写到 <c>TestContext.WorkDirectory/crowd-fuzz-trace/</c>，
/// 供后续行为修复任务定位。判据本身的红绿由 <see cref="CrowdFuzzTraceNegativeTests"/> 负向用例背书。
/// </summary>
public sealed class CrowdFuzzTraceParityTests
{
    private CrowdSchedulerLease? _schedulerLease;

    [OneTimeSetUp]
    public void OwnSingleWorkerScheduler() => _schedulerLease = new CrowdSchedulerLease("CrowdFuzzTraceParity", 1);

    [OneTimeTearDown]
    public void ReleaseOwnedScheduler()
    {
        _schedulerLease?.Dispose();
        _schedulerLease = null;
    }

    private readonly record struct DivergenceKey(CrowdFuzzTraceGate Gate, int Tick, int Op, uint? Handle, int? OrderId, string Field);

    private readonly record struct SeedBaseline(long Seed, DivergenceKey[] Divergences);

    private static readonly SeedBaseline[] Baselines =
    {
        new(11111, new[]
        {
            new DivergenceKey(CrowdFuzzTraceGate.OpSequence, 56, 28, null, null, "units"),
            new DivergenceKey(CrowdFuzzTraceGate.OpParams, 56, 28, null, null, "op.params.x"),
            new DivergenceKey(CrowdFuzzTraceGate.Identity, 55, 28, 670, null, "units.handle"),
            new DivergenceKey(CrowdFuzzTraceGate.Structural, 53, 27, 333, null, "units.mode"),
            new DivergenceKey(CrowdFuzzTraceGate.LeaderPath, 61, 31, null, 2, "leader.x"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionUnit, 51, 26, 331, null, "units.x"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionMean, 53, 27, null, null, "units.x.mean"),
        }),
        new(424242, new[]
        {
            new DivergenceKey(CrowdFuzzTraceGate.OpSequence, 60, 30, null, null, "units"),
            new DivergenceKey(CrowdFuzzTraceGate.OpParams, 64, 32, null, null, "op.params.x"),
            new DivergenceKey(CrowdFuzzTraceGate.Identity, 59, 30, 775, null, "units.handle"),
            new DivergenceKey(CrowdFuzzTraceGate.Structural, 53, 27, 788, null, "units.mode"),
            new DivergenceKey(CrowdFuzzTraceGate.LeaderPath, 33, 17, null, 4, "leaders[4#0].path.count"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionUnit, 22, 11, 439, null, "units.y"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionMean, 34, 17, null, null, "units.x.mean"),
        }),
        new(9001, new[]
        {
            new DivergenceKey(CrowdFuzzTraceGate.OpSequence, 64, 32, null, null, "units"),
            new DivergenceKey(CrowdFuzzTraceGate.OpParams, 66, 33, null, null, "op.params.x"),
            new DivergenceKey(CrowdFuzzTraceGate.Identity, 63, 32, 262158, null, "units.handle"),
            new DivergenceKey(CrowdFuzzTraceGate.Structural, 55, 28, 262257, null, "units.state"),
            new DivergenceKey(CrowdFuzzTraceGate.LeaderPath, 71, 36, null, 8, "leaders[8#0].path.count"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionUnit, 37, 19, 262224, null, "units.x"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionMean, 57, 29, null, null, "units.x.mean"),
        }),
    };

    [TestCaseSource(nameof(BaselineCases))]
    public void WebTrace_Full40Ops_DivergencesMatchRecordedBaseline(long seed)
    {
        var baseline = Baselines.Single(b => b.Seed == seed);
        var fixture = CrowdFuzzParityTests.LoadFixture($"fuzz-trace-seed{seed}.json");
        Assert.That(fixture.Seed, Is.EqualTo(seed));
        Assert.That(fixture.OpCount, Is.EqualTo(40));

        var result = CrowdFuzzParityTests.RunFuzz(fixture, fixture.OpCount, out var ticks);
        var report = CrowdWebFuzzTraceCompare.Compare(fixture.Steps, fixture.Ticks, result.Steps, ticks);
        Assert.That(report.ComparedOps, Is.EqualTo(40));
        Assert.That(report.ComparedTicks, Is.EqualTo(80));

        var actual = report.Divergences
            .Select(d => new DivergenceKey(d.Gate, d.Tick, d.OpIndex, d.Handle, d.OrderId, d.Field))
            .ToArray();
        var expected = baseline.Divergences;
        var expectedText = string.Join(Environment.NewLine, expected.Select(k => $"  {k}"));
        var actualText = string.Join(Environment.NewLine, actual.Select(k => $"  {k}"));
        Assert.That(actual, Is.EqualTo(expected).AsCollection,
            $"已知分歧基线变动（提前=回归，推迟=行为修复收线，都要求更新本基线并记录原因）。\n期望:\n{expectedText}\n实际:\n{actualText}");

        string reportPath = WriteReport(seed, fixture, report);
        TestContext.Out.WriteLine($"分歧报告: {reportPath}");
    }

    private static IEnumerable<TestCaseData> BaselineCases()
    {
        foreach (var b in Baselines)
        {
            yield return new TestCaseData(b.Seed)
                .SetName($"Seed{b.Seed}_Full40Ops_KnownDivergenceBaseline");
        }
    }

    private static string WriteReport(long seed, CrowdWebFuzzTraceFixture fixture, CrowdFuzzTraceReport report)
    {
        var divergences = new JsonArray();
        foreach (var d in report.Divergences)
        {
            divergences.Add(new JsonObject
            {
                ["gate"] = d.Gate.ToString(),
                ["tick"] = d.Tick,
                ["op"] = d.OpIndex,
                ["handle"] = d.Handle,
                ["orderId"] = d.OrderId,
                ["field"] = d.Field,
                ["web"] = d.Expected,
                ["cs"] = d.Actual,
                ["detail"] = d.Detail,
            });
        }

        var doc = new JsonObject
        {
            ["seed"] = seed,
            ["opCount"] = fixture.OpCount,
            ["comparedOps"] = report.ComparedOps,
            ["comparedTicks"] = report.ComparedTicks,
            ["contentSha256"] = fixture.ContentSha256,
            ["generationCommand"] = fixture.GenerationCommand,
            ["generatedBy"] = fixture.GeneratedBy,
            ["divergences"] = divergences,
        };

        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "crowd-fuzz-trace");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"report-seed{seed}.json");
        File.WriteAllText(path, doc.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
