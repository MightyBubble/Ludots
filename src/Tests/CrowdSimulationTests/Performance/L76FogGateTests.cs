using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 换雾之后对照 l76-fog.json。容差锁死：可见格每组每采样 |Δ| ≤ max(4, ceil(0.01×前))，
/// 脚本结束时已探索 |Δ| ≤ max(8, ceil(0.01×前))，双向共享对称差为 0，
/// 单向或无关系时只被 A 照亮的格不出现在 B。tick 中位 ≤ 前×1.05，稳态分配 ≤ 前。
/// 没有雾层时 VisionSystem.Update 中位 ≤ 0.05 ms，0 单位和 5 万单位都过这条线。
/// </summary>
[TestFixture]
public sealed class L76FogGateTests
{
    [Test]
    public void FogCountsSharesAndTickCost_StayWithinBaseline()
    {
        JsonNode baseline = JsonNode.Parse(File.ReadAllText(BaselinePath()))!;
        AssertCounts(baseline["fog"]!, L76FogBaselineHarness.SampleTruth("s7fog-truth.json"));
        AssertCounts(baseline["recycle"]!, L76FogBaselineHarness.SampleTruth("s7crecycle-truth.json"));
        AssertShare(L76FogBaselineHarness.SampleShare());
        AssertPerf(baseline["perf"]!, L76FogBaselineHarness.SamplePerf());
    }

    [Test]
    public void MismatchedLayerCellSize_NamesBothNumbers()
    {
        var layers = new FogLayerRegistry();
        layers.Register(CrowdFogLayerBinding.LayerKey, 100, 20);
        InvalidOperationException mismatch = Assert.Throws<InvalidOperationException>(
            () => CrowdFogLayerBinding.Require(layers, 25000, 4))!;
        Assert.That(mismatch.Message, Does.Contain("100"));
        Assert.That(mismatch.Message, Does.Contain("25000"));

        FogLayerId id = CrowdFogLayerBinding.Require(layers, 100, 4);
        Assert.That(layers.IsExternalStamp(id), Is.True);
    }

    [Test]
    public void VisionSystem_WithoutFogLayer_StaysUnderIdleCeiling()
    {
        double empty = MedianUpdateMs(0);
        double crowded = MedianUpdateMs(50_000);
        TestContext.Out.WriteLine($"idle empty {empty:F4} ms, 50k {crowded:F4} ms");
        Assert.That(empty, Is.LessThanOrEqualTo(0.05));
        Assert.That(crowded, Is.LessThanOrEqualTo(0.05));
    }

    private static void AssertCounts(JsonNode before, JsonNode after)
    {
        JsonArray beforeSamples = before["samples"]!.AsArray();
        JsonArray afterSamples = after["samples"]!.AsArray();
        Assert.That(afterSamples.Count, Is.EqualTo(beforeSamples.Count));
        for (int i = 0; i < beforeSamples.Count; i++)
        {
            JsonArray beforeVisible = beforeSamples[i]!["visible"]!.AsArray();
            JsonArray afterVisible = afterSamples[i]!["visible"]!.AsArray();
            Assert.That(afterVisible.Count, Is.EqualTo(beforeVisible.Count));
            for (int g = 0; g < beforeVisible.Count; g++)
            {
                int previous = beforeVisible[g]!.GetValue<int>();
                int current = afterVisible[g]!.GetValue<int>();
                int tolerance = Math.Max(4, (int)Math.Ceiling(0.01 * previous));
                Assert.That(
                    Math.Abs(current - previous),
                    Is.LessThanOrEqualTo(tolerance),
                    $"tick {beforeSamples[i]!["tick"]} 组 {g} 可见 {current}，基线 {previous}，容差 {tolerance}");
            }
        }

        JsonArray beforeEnd = before["exploredEnd"]!.AsArray();
        JsonArray afterEnd = after["exploredEnd"]!.AsArray();
        Assert.That(afterEnd.Count, Is.EqualTo(beforeEnd.Count));
        for (int g = 0; g < beforeEnd.Count; g++)
        {
            int previous = beforeEnd[g]!.GetValue<int>();
            int current = afterEnd[g]!.GetValue<int>();
            int tolerance = Math.Max(8, (int)Math.Ceiling(0.01 * previous));
            Assert.That(
                Math.Abs(current - previous),
                Is.LessThanOrEqualTo(tolerance),
                $"已探索结束 组 {g} {current}，基线 {previous}，容差 {tolerance}");
        }
    }

    private static void AssertShare(JsonNode share)
    {
        JsonNode bidirectional = share["bidirectional"]!;
        Assert.That(bidirectional["sameGroup"]!.GetValue<bool>(), Is.True);
        Assert.That(bidirectional["symmetricDiff"]!.GetValue<int>(), Is.EqualTo(0));

        JsonNode noRelation = share["noRelation"]!;
        Assert.That(noRelation["sameGroup"]!.GetValue<bool>(), Is.False);
        Assert.That(noRelation["onlyAInB"]!.GetValue<int>(), Is.EqualTo(0));

        JsonNode oneWay = share["oneWay"]!;
        Assert.That(oneWay["sameGroup"]!.GetValue<bool>(), Is.False);
        Assert.That(oneWay["onlyAInB"]!.GetValue<int>(), Is.EqualTo(0));
    }

    private static void AssertPerf(JsonNode before, JsonNode after)
    {
        double before12 = before["tick12kMedianMs"]!.GetValue<double>();
        double after12 = after["tick12kMedianMs"]!.GetValue<double>();
        double before50 = before["tick50kMedianMs"]!.GetValue<double>();
        double after50 = after["tick50kMedianMs"]!.GetValue<double>();
        long allocBefore12 = before["steadyAlloc12kMedian"]!.GetValue<long>();
        long allocAfter12 = after["steadyAlloc12kMedian"]!.GetValue<long>();
        long allocBefore50 = before["steadyAlloc50kMedian"]!.GetValue<long>();
        long allocAfter50 = after["steadyAlloc50kMedian"]!.GetValue<long>();
        TestContext.Out.WriteLine(
            $"12k {after12:F4} ms (基线 {before12:F4}, 上限 {before12 * 1.05:F4}) alloc {allocAfter12} (基线 {allocBefore12})");
        TestContext.Out.WriteLine(
            $"50k {after50:F4} ms (基线 {before50:F4}, 上限 {before50 * 1.05:F4}) alloc {allocAfter50} (基线 {allocBefore50})");
        Assert.That(after12, Is.LessThanOrEqualTo(before12 * 1.05));
        Assert.That(after50, Is.LessThanOrEqualTo(before50 * 1.05));
        Assert.That(allocAfter12, Is.LessThanOrEqualTo(allocBefore12));
        Assert.That(allocAfter50, Is.LessThanOrEqualTo(allocBefore50));
    }

    private static double MedianUpdateMs(int units)
    {
        using World world = World.Create();
        var layers = new FogLayerRegistry();
        var fields = new FogFieldStore();
        var knowledge = new KnowledgeProjectionStore();
        var system = new VisionSystem(
            world,
            new GameSession(),
            layers,
            fields,
            new VisionResolver(layers, fields),
            new FogKnowledgeProjector(knowledge),
            knowledge,
            new PlayerOwnedEntityObserverResolver(new PlayerEntityLookup()));
        for (int i = 0; i < units; i++)
        {
            world.Create(WorldPositionCm.FromCm(i % 4000, i / 4000));
        }

        for (int i = 0; i < 30; i++) system.Update(0.02f);
        var samples = new double[5];
        for (int run = 0; run < samples.Length; run++)
        {
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) system.Update(0.02f);
            watch.Stop();
            samples[run] = watch.Elapsed.TotalMilliseconds / 200.0;
        }

        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static string BaselinePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string path = Path.Combine(dir.FullName, "src", "Tests", "CrowdSimulationTests", "Baselines", "l76-fog.json");
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到 l76-fog.json。");
    }
}
