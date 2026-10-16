using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Schedulers;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 模糊测试双端对拍：夹具由 <c>tools/crowdsimulation-export/exportFuzzParity.mjs</c> 从 Web <c>fuzzIter</c> 导出。
/// 比对 rng 序列、逐步 op 参数、存活数、校验结果；位置 L1 统计按行为带宽。
/// </summary>
public sealed class CrowdFuzzParityTests
{
    [OneTimeSetUp]
    public void PinMovementJobsToSingleWorker()
    {
        World.SharedJobScheduler = new JobScheduler(new JobScheduler.Config
        {
            ThreadPrefixName = "CrowdFuzzParity",
            ThreadCount = 1,
            MaxExpectedConcurrentJobs = 16,
            StrictAllocationMode = false,
        });
    }

    private static readonly (long Seed, string File)[] Fixtures =
    {
        (11111, "fuzz-parity-seed11111.json"),
        (424242, "fuzz-parity-seed424242.json"),
        (9001, "fuzz-parity-seed9001.json"),
    };

    [TestCaseSource(nameof(FixtureCases))]
    public void WebFixture_RngOpsAndBehavior_Match(long seed, string fileName)
    {
        var fixture = LoadFixture(fileName);
        Assert.That(fixture.Seed, Is.EqualTo(seed));

        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        TestDefaults.EnableFog(session);

        var blast = fixture.Blast;
        var result = CrowdWebFuzzRunner.Run(session, fixture.OpCount, blast, fixture.Seed);
        Assert.That(result.Ok, Is.True, result.Errors is { Count: > 0 } ? string.Join("; ", result.Errors) : "模糊运行失败");
        Assert.That(result.Steps, Has.Count.EqualTo(fixture.Steps.Count));

        for (int i = 0; i < fixture.Steps.Count; i++)
        {
            var expected = fixture.Steps[i];
            var actual = result.Steps[i];
            Assert.That(actual.Op.Kind, Is.EqualTo(expected.Op.Kind), $"第 {i + 1} 步 op 种类");
            Assert.That(actual.RngDraws, Is.EqualTo(expected.RngDraws).AsCollection, $"第 {i + 1} 步 rng");
            AssertOpParams(expected.Op, actual.Op, i + 1);
            Assert.That(actual.Units, Is.EqualTo(expected.Units), $"第 {i + 1} 步存活数");
            Assert.That(actual.ValidateOk, Is.EqualTo(expected.ValidateOk), $"第 {i + 1} 步 validateOk");
            if (!expected.ValidateOk)
            {
                Assert.That(actual.ValidateErrors, Is.EqualTo(expected.ValidateErrors).AsCollection, $"第 {i + 1} 步校验错误");
            }

            long delta = Math.Abs(actual.PositionStatL1Cm - expected.PositionStatL1Cm);
            Assert.That(delta, Is.LessThanOrEqualTo(CrowdWebFuzzRunner.PositionStatBandwidthCm),
                $"第 {i + 1} 步位置 L1 统计差 {delta} cm（带宽 {CrowdWebFuzzRunner.PositionStatBandwidthCm}）");
        }
    }

    private static IEnumerable<TestCaseData> FixtureCases()
    {
        foreach (var (seed, file) in Fixtures)
        {
            yield return new TestCaseData(seed, file).SetName($"Seed{seed}_MatchWebFixture");
        }
    }

    private static void AssertOpParams(CrowdWebFuzzOpRecord expected, CrowdWebFuzzOpRecord actual, int step)
        => CrowdWebFuzzOpParamAssert.Equal(expected, actual, step);

    private static CrowdWebFuzzParityFixture LoadFixture(string fileName)
    {
        string path = Path.Combine(
            RepoRoot(),
            "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress",
            fileName);
        return CrowdWebFuzzParityFixture.Load(path);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string catalog = Path.Combine(
                dir.FullName,
                "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/catalog.json");
            if (File.Exists(catalog)) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到回归场景表。");
    }

    [Test]
    [Explicit("维护：本地用 exportFuzzParity.mjs 重新导出夹具")]
    public void GenerateFixtureSkeleton_Seed11111()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        TestDefaults.EnableFog(session);
        var tools = JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepoRoot(), "mods/capabilities/navigation/CrowdSimulationUiMod/assets/CrowdSimulationDebug.json")))!["tools"]!.AsObject();
        var blast = new CrowdWebFuzzBlast(
            Fix64.FromDouble(tools["ringScale"]!.GetValue<double>()),
            Fix64.FromDouble(tools["impulse"]!.GetValue<double>()));
        var result = CrowdWebFuzzRunner.Run(session, 40, blast, 11111);
        Assert.That(result.Ok, Is.True);
        var fixture = new CrowdWebFuzzParityFixture
        {
            ParitySchema = CrowdWebFuzzParityFixture.SchemaV2,
            Seed = 11111,
            OpCount = 40,
            Blast = blast,
            Steps = result.Steps,
        };
        string path = Path.Combine(
            RepoRoot(),
            "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/fuzz-parity-seed11111.json");
        fixture.Save(path);
        TestContext.Out.WriteLine($"Wrote {path}");
    }
}
