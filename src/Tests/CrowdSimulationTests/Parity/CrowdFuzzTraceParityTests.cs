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
/// 基线更新只许前向（棘轮）：每门首分歧 op 只许上推，全绿时把该门棘轮推到 41；回退、删除仍被跟踪的门、
/// 或绕开棘轮的新分歧都会被棘轮守卫钉红。完整 40 op 零分歧门禁的激活方式就是三种子全部棘轮推到 41。
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

    private const int FullOpWindowOps = 40;

    /// <summary>棘轮终态：首分歧 op 下限 41 = 40 op 窗口内零分歧。</summary>
    private const int GreenAllOpsFloor = FullOpWindowOps + 1;

    private static readonly SeedBaseline[] Baselines =
    {
        new(11111, new[]
        {
            new DivergenceKey(CrowdFuzzTraceGate.OpSequence, 56, 28, null, null, "units"),
            new DivergenceKey(CrowdFuzzTraceGate.OpParams, 56, 28, null, null, "op.params.x"),
            new DivergenceKey(CrowdFuzzTraceGate.Identity, 55, 28, 670, null, "units.handle"),
            new DivergenceKey(CrowdFuzzTraceGate.Structural, 53, 27, 333, null, "units.mode"),
            new DivergenceKey(CrowdFuzzTraceGate.NavVersion, 53, 27, null, null, "navs[0].version"),
            new DivergenceKey(CrowdFuzzTraceGate.LeaderPath, 61, 31, null, 2, "leader.x"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionUnit, 51, 26, 331, null, "units.x"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionMean, 53, 27, null, null, "units.x.mean"),
        }),
        new(424242, new[]
        {
            new DivergenceKey(CrowdFuzzTraceGate.OpSequence, 60, 30, null, null, "units"),
            new DivergenceKey(CrowdFuzzTraceGate.OpParams, 60, 30, null, null, "op.params.x"),
            new DivergenceKey(CrowdFuzzTraceGate.Identity, 59, 30, 1293, null, "units.handle"),
            new DivergenceKey(CrowdFuzzTraceGate.Structural, 34, 17, 1327, null, "units.mode"),
            new DivergenceKey(CrowdFuzzTraceGate.NavVersion, 29, 15, null, null, "navs[0].version"),
            new DivergenceKey(CrowdFuzzTraceGate.LeaderPath, 33, 17, null, 4, "leaders[4#0].path.count"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionUnit, 22, 11, 439, null, "units.y"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionMean, 29, 15, null, null, "units.x.mean"),
        }),
        new(9001, new[]
        {
            new DivergenceKey(CrowdFuzzTraceGate.OpSequence, 64, 32, null, null, "units"),
            new DivergenceKey(CrowdFuzzTraceGate.OpParams, 66, 33, null, null, "op.params.x"),
            new DivergenceKey(CrowdFuzzTraceGate.Identity, 63, 32, 262158, null, "units.handle"),
            new DivergenceKey(CrowdFuzzTraceGate.Structural, 55, 28, 262257, null, "units.state"),
            new DivergenceKey(CrowdFuzzTraceGate.NavVersion, 45, 23, null, null, "navs[0].version"),
            new DivergenceKey(CrowdFuzzTraceGate.LeaderPath, 71, 36, null, 8, "leaders[8#0].path.count"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionUnit, 37, 19, 262224, null, "units.x"),
            new DivergenceKey(CrowdFuzzTraceGate.PositionMean, 57, 29, null, null, "units.x.mean"),
        }),
    };

    /// <summary>棘轮下限：种子 × 门 → 首个分歧允许出现的最早 op；只许上推，回退由守卫用例钉红。</summary>
    private readonly record struct RatchetFloor(long Seed, CrowdFuzzTraceGate Gate, int MinFirstDivergenceOp);

    private static readonly RatchetFloor[] RatchetFloors =
    {
        new(11111, CrowdFuzzTraceGate.OpSequence, 28),
        new(11111, CrowdFuzzTraceGate.OpParams, 28),
        new(11111, CrowdFuzzTraceGate.Identity, 28),
        new(11111, CrowdFuzzTraceGate.Structural, 27),
        new(11111, CrowdFuzzTraceGate.NavVersion, 27),
        new(11111, CrowdFuzzTraceGate.LeaderPath, 31),
        new(11111, CrowdFuzzTraceGate.PositionUnit, 26),
        new(11111, CrowdFuzzTraceGate.PositionMean, 27),
        new(424242, CrowdFuzzTraceGate.OpSequence, 30),
        new(424242, CrowdFuzzTraceGate.OpParams, 30),
        new(424242, CrowdFuzzTraceGate.Identity, 30),
        new(424242, CrowdFuzzTraceGate.Structural, 17),
        new(424242, CrowdFuzzTraceGate.NavVersion, 15),
        new(424242, CrowdFuzzTraceGate.LeaderPath, 17),
        new(424242, CrowdFuzzTraceGate.PositionUnit, 11),
        new(424242, CrowdFuzzTraceGate.PositionMean, 15),
        new(9001, CrowdFuzzTraceGate.OpSequence, 32),
        new(9001, CrowdFuzzTraceGate.OpParams, 33),
        new(9001, CrowdFuzzTraceGate.Identity, 32),
        new(9001, CrowdFuzzTraceGate.Structural, 28),
        new(9001, CrowdFuzzTraceGate.NavVersion, 23),
        new(9001, CrowdFuzzTraceGate.LeaderPath, 36),
        new(9001, CrowdFuzzTraceGate.PositionUnit, 19),
        new(9001, CrowdFuzzTraceGate.PositionMean, 29),
    };

    [TestCaseSource(nameof(BaselineCases))]
    public void WebTrace_Full40Ops_DivergencesMatchRecordedBaseline(long seed)
    {
        var baseline = Baselines.Single(b => b.Seed == seed);
        var fixture = CrowdFuzzParityTests.LoadFixture($"fuzz-trace-seed{seed}.json");
        Assert.That(fixture.Seed, Is.EqualTo(seed));
        Assert.That(fixture.OpCount, Is.EqualTo(FullOpWindowOps));

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

    /// <summary>
    /// 棘轮守卫：基线相对棘轮下限只许前向——每门首分歧 op 不许低于下限；下限仍在窗口内时该门的分歧不许从基线消失
    /// （门全绿时必须同提交把下限推到 41）；基线里的分歧必须有棘轮行；最早分歧 op 与绿前缀表不许矛盾。
    /// </summary>
    [Test]
    public void Ratchet_BaselinesStayAboveFloors_OnlyForwardProgressAllowed()
    {
        var violations = EvaluateRatchet(Baselines, RatchetFloors);
        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void Ratchet_HandLoweredBaseline_IsFlagged()
    {
        var lowered = CloneBaselines();
        int i = lowered.FindIndex(b => b.Seed == 11111);
        var divergences = lowered[i].Divergences.ToArray();
        int g = Array.FindIndex(divergences, d => d.Gate == CrowdFuzzTraceGate.PositionUnit);
        divergences[g] = divergences[g] with { Op = divergences[g].Op - 1 };
        lowered[i] = lowered[i] with { Divergences = divergences };

        var violations = EvaluateRatchet(lowered, RatchetFloors);
        Assert.That(violations, Has.Some.Contains("低于棘轮下限"), "人为回退基线必须被棘轮钉红。");
    }

    [Test]
    public void Ratchet_DeletedStillTrackedGate_IsFlagged()
    {
        var trimmed = CloneBaselines();
        int i = trimmed.FindIndex(b => b.Seed == 11111);
        trimmed[i] = trimmed[i] with
        {
            Divergences = trimmed[i].Divergences.Where(d => d.Gate != CrowdFuzzTraceGate.PositionUnit).ToArray(),
        };

        var violations = EvaluateRatchet(trimmed, RatchetFloors);
        Assert.That(violations, Has.Some.Contains("从基线消失"), "下限仍在窗口内时删除该门的分歧必须被钉红。");
    }

    [Test]
    public void Ratchet_MissingFloorRow_IsFlagged()
    {
        var floors = RatchetFloors
            .Where(f => !(f.Seed == 11111 && f.Gate == CrowdFuzzTraceGate.PositionUnit))
            .ToArray();

        Assert.That(EvaluateRatchet(Baselines, floors), Has.Some.Contains("没有棘轮行"), "删棘轮行必须被钉红。");
    }

    private static IReadOnlyList<string> EvaluateRatchet(IReadOnlyList<SeedBaseline> baselines, IReadOnlyList<RatchetFloor> floors)
    {
        var violations = new List<string>();
        var baselineSeeds = baselines.Select(b => b.Seed).ToHashSet();
        foreach (long floorSeed in floors.Select(f => f.Seed).Distinct())
        {
            if (!baselineSeeds.Contains(floorSeed))
            {
                violations.Add($"种子 {floorSeed} 在棘轮里但不在基线里。");
            }
        }

        foreach (var baseline in baselines)
        {
            var seedFloors = floors.Where(f => f.Seed == baseline.Seed).ToArray();
            if (seedFloors.Length == 0)
            {
                violations.Add($"种子 {baseline.Seed} 在基线里但棘轮未跟踪。");
            }

            foreach (var floor in seedFloors)
            {
                var entry = baseline.Divergences
                    .Where(d => d.Gate == floor.Gate)
                    .Cast<DivergenceKey?>()
                    .FirstOrDefault();
                if (entry is null)
                {
                    if (floor.MinFirstDivergenceOp <= FullOpWindowOps)
                    {
                        violations.Add(
                            $"种子 {baseline.Seed} 门 {floor.Gate} 的分歧从基线消失，但棘轮下限 {floor.MinFirstDivergenceOp} 仍在 {FullOpWindowOps} op 窗口内；该门全绿时必须同提交把下限推到 {GreenAllOpsFloor}。");
                    }
                }
                else if (entry.Value.Op < floor.MinFirstDivergenceOp)
                {
                    violations.Add($"种子 {baseline.Seed} 门 {floor.Gate} 基线首分歧 op {entry.Value.Op} 低于棘轮下限 {floor.MinFirstDivergenceOp}（回归或手改）。");
                }
            }

            foreach (var gate in baseline.Divergences
                .Where(d => seedFloors.All(f => f.Gate != d.Gate))
                .Select(d => d.Gate)
                .Distinct())
            {
                violations.Add($"种子 {baseline.Seed} 门 {gate} 的分歧没有棘轮行。");
            }

            int earliestOp = baseline.Divergences.Length == 0 ? GreenAllOpsFloor : baseline.Divergences.Min(d => d.Op);
            int? greenPrefix = CrowdFuzzParityTests.TryStrictGreenOpsFor(baseline.Seed);
            if (greenPrefix is null)
            {
                violations.Add($"种子 {baseline.Seed} 在绿前缀表里没有记录。");
            }
            else if (earliestOp < greenPrefix.Value + 1)
            {
                violations.Add(
                    $"种子 {baseline.Seed} 基线最早分歧 op {earliestOp} 与绿前缀 {greenPrefix.Value} 矛盾（前缀测试声称前 {greenPrefix.Value} op 全绿）。");
            }
        }

        return violations;
    }

    private static List<SeedBaseline> CloneBaselines()
        => Baselines.Select(b => b with { Divergences = b.Divergences.ToArray() }).ToList();

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
