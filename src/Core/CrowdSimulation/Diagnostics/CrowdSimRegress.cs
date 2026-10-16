using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

public sealed class CrowdSimRegressScenario
{
    public required string Id { get; init; }
    public string? UnavailableReason { get; init; }
    public JsonArray? Script { get; init; }
    public int Ticks { get; init; }
    public bool Movement { get; init; }
    public bool TruthNavFrozen { get; init; }
    public IReadOnlyList<string>? ExpectAbsentTemplates { get; init; }
    public IReadOnlyList<string>? ExpectPresentTemplates { get; init; }
}

public sealed class CrowdSimRegressHost : IDisposable
{
    public required CrowdSimSession Session { get; init; }
    public double BakeMs { get; init; }
    public Action? Release { get; init; }

    public void Dispose() => Release?.Invoke();
}

public sealed class CrowdSimRegressRequest
{
    public required IReadOnlyList<CrowdSimRegressScenario> Scenarios { get; init; }
    public required int Repeat { get; init; }
    public double? BudgetMs { get; init; }
    public CrowdFuzzPlan? Fuzz { get; init; }
    public required Func<CrowdSimRegressScenario, CrowdSimRegressHost> OpenScenario { get; init; }
    public Func<CrowdSimRegressHost>? OpenFuzz { get; init; }
}

public sealed class CrowdSimRegressScenarioReport
{
    public required string Id { get; init; }
    public required bool Passed { get; init; }
    public required bool Unavailable { get; init; }
    public string? Error { get; init; }
    public double BakeMs { get; init; }
    public double AvgTickMs { get; init; }
    public double P95TickMs { get; init; }
    public double MaxTickMs { get; init; }
    public ulong Checksum { get; init; }
    public required IReadOnlyList<string> Differing { get; init; }
}

public sealed class CrowdSimRegressReport
{
    public required bool Passed { get; init; }
    public string? Error { get; init; }
    public required IReadOnlyList<CrowdSimRegressScenarioReport> Scenarios { get; init; }
    public CrowdFuzzResult? Fuzz { get; init; }
    public string? FuzzError { get; init; }
}

/// <summary>
/// 无头重复跑同一脚本，比较终点合计和分区，并可选接一段模糊操作。
/// 不读文件。会话由调用方按场景打开，烘焙耗时由调用方填进 <see cref="CrowdSimRegressHost.BakeMs"/>。
/// </summary>
public static class CrowdSimRegress
{
    public static CrowdSimRegressReport Run(CrowdSimRegressRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Repeat < 1)
        {
            return new CrowdSimRegressReport
            {
                Passed = false,
                Error = "重复次数需 ≥ 1。",
                Scenarios = Array.Empty<CrowdSimRegressScenarioReport>(),
            };
        }

        var scenarios = new List<CrowdSimRegressScenarioReport>(request.Scenarios.Count);
        bool passed = true;
        for (int i = 0; i < request.Scenarios.Count; i++)
        {
            var report = RunScenario(request, request.Scenarios[i]);
            scenarios.Add(report);
            if (!report.Passed) passed = false;
        }

        CrowdFuzzResult? fuzz = null;
        string? fuzzError = null;
        if (request.Fuzz != null && request.Fuzz.OpCount > 0)
        {
            if (request.OpenFuzz == null)
            {
                passed = false;
                fuzzError = "没有模糊测试会话。";
            }
            else
            {
                try
                {
                    using var host = request.OpenFuzz();
                    fuzz = CrowdFuzzRunner.Run(host.Session, request.Fuzz);
                    if (!fuzz.Passed) passed = false;
                }
                catch (Exception ex)
                {
                    passed = false;
                    fuzzError = ex.Message;
                }
            }
        }

        return new CrowdSimRegressReport
        {
            Passed = passed,
            Scenarios = scenarios,
            Fuzz = fuzz,
            FuzzError = fuzzError,
        };
    }

    /// <summary>最近秩。空样本为 0。下标是 ceil(fraction × n) − 1。</summary>
    public static double NearestRank(ReadOnlySpan<double> samples, double fraction)
    {
        if (samples.Length == 0) return 0;
        var sorted = new double[samples.Length];
        samples.CopyTo(sorted);
        Array.Sort(sorted);
        int index = (int)Math.Ceiling(fraction * sorted.Length) - 1;
        if (index < 0) index = 0;
        if (index >= sorted.Length) index = sorted.Length - 1;
        return sorted[index];
    }

    private static CrowdSimRegressScenarioReport RunScenario(
        CrowdSimRegressRequest request, CrowdSimRegressScenario scenario)
    {
        if (scenario.UnavailableReason != null)
        {
            return new CrowdSimRegressScenarioReport
            {
                Id = scenario.Id,
                Passed = false,
                Unavailable = true,
                Error = scenario.UnavailableReason,
                Differing = Array.Empty<string>(),
            };
        }

        if (scenario.Script == null || scenario.Ticks < 0)
        {
            return new CrowdSimRegressScenarioReport
            {
                Id = scenario.Id,
                Passed = false,
                Unavailable = false,
                Error = "场景没有指令脚本。",
                Differing = Array.Empty<string>(),
            };
        }

        var samples = new List<double>(scenario.Ticks * request.Repeat);
        var differing = new List<string>();
        double bake = 0;
        ulong checksum = 0;
        List<CrowdSimPartition>? firstPartitions = null;
        bool have = false;
        string? error = null;
        try
        {
            for (int repeat = 0; repeat < request.Repeat; repeat++)
            {
                using var host = request.OpenScenario(scenario);
                bake = repeat == 0 ? host.BakeMs : Math.Max(bake, host.BakeMs);
                var session = host.Session;
                session.Presentation.TruthNavFrozen = scenario.TruthNavFrozen;
                session.DiagnosticDigest = true;
                session.BlockOnDueReplies = true;
                var script = CrowdReferenceScript.Load(scenario.Script, session.Units.Capacity);
                script.ValidateStructure(session);
                int guard = scenario.Ticks * CrowdSimulationLimits.AdvanceStallGuardFactor
                    + CrowdSimulationLimits.AdvanceStallGuardFactor;
                int ran = 0;
                while (ran < scenario.Ticks)
                {
                    if (guard-- <= 0)
                    {
                        throw new InvalidOperationException("会话推进停摆过长:路径服务答复未到。");
                    }

                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    bool ok = script.Step(session, out _);
                    if (!ok) continue;
                    samples.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    ran++;
                }

                CheckTemplates(session, scenario);
                CrowdSimStateDigest end = scenario.Ticks == 0
                    ? CrowdSimChecksum.Compute(session)
                    : session.LastDigest;
                var partitions = new List<CrowdSimPartition>();
                CrowdSimPartitions.Copy(session, partitions);
                if (!have)
                {
                    have = true;
                    checksum = end.Total;
                    firstPartitions = partitions;
                }
                else
                {
                    var found = new List<string>();
                    CrowdSimPartitions.CollectDifferences(firstPartitions!, partitions, found);
                    for (int n = 0; n < found.Count; n++)
                    {
                        if (!differing.Contains(found[n])) differing.Add(found[n]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        double avg = 0;
        double max = 0;
        if (samples.Count > 0)
        {
            double sum = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                sum += samples[i];
                if (samples[i] > max) max = samples[i];
            }

            avg = sum / samples.Count;
        }

        double p95 = NearestRank(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(samples), 0.95);
        if (request.BudgetMs is double budget && p95 > budget)
        {
            string budgetError =
                $"p95 {p95.ToString("0.###", CultureInfo.InvariantCulture)} ms 超过预算 {budget.ToString("0.###", CultureInfo.InvariantCulture)} ms";
            error = error == null ? budgetError : error + " " + budgetError;
        }

        if (differing.Count > 0)
        {
            string names = "分区不一致: " + string.Join(",", differing);
            error = error == null ? names : error + " " + names;
        }

        return new CrowdSimRegressScenarioReport
        {
            Id = scenario.Id,
            Passed = error == null,
            Unavailable = false,
            Error = error,
            BakeMs = bake,
            AvgTickMs = avg,
            P95TickMs = p95,
            MaxTickMs = max,
            Checksum = checksum,
            Differing = differing,
        };
    }

    private static void CheckTemplates(CrowdSimSession session, CrowdSimRegressScenario scenario)
    {
        if (scenario.ExpectAbsentTemplates == null && scenario.ExpectPresentTemplates == null) return;
        var store = session.Structures ?? throw new InvalidOperationException("场景要核对结构模板，会话没有结构仓。");
        if (scenario.ExpectAbsentTemplates != null)
        {
            for (int i = 0; i < scenario.ExpectAbsentTemplates.Count; i++)
            {
                string template = scenario.ExpectAbsentTemplates[i];
                if (HasTemplate(store, template))
                {
                    throw new InvalidOperationException($"模板 {template} 仍在场。");
                }
            }
        }

        if (scenario.ExpectPresentTemplates != null)
        {
            for (int i = 0; i < scenario.ExpectPresentTemplates.Count; i++)
            {
                string template = scenario.ExpectPresentTemplates[i];
                if (!HasTemplate(store, template))
                {
                    throw new InvalidOperationException($"模板 {template} 不在场。");
                }
            }
        }
    }

    private static bool HasTemplate(CrowdStructuresStore store, string templateId)
    {
        int template = store.TemplateIndexOf(templateId);
        if (template < 0) throw new InvalidOperationException($"未知结构模板 \"{templateId}\"。");
        var ids = store.EntityIds;
        for (int i = 0; i < ids.Count; i++)
        {
            if (store.TryGetTemplate(ids[i], out int live) && live == template) return true;
        }

        return false;
    }
}
