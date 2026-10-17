using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Schedulers;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 模糊测试双端对拍：夹具由 <c>tools/crowdsimulation-export/exportFuzzParity.mjs</c> 从 Web <c>fuzzIter</c> 导出。
/// 比对 rng 序列、逐步 op 参数、存活数、校验结果，位置按每轴 1 厘米口径。
/// 每个种子只比到 <see cref="FixtureCase.ComparedSteps"/>：再往后两端在指令规划（领队路径、重烘后的流场刷新）上分叉，
/// 单位位置随之分开，后续 rng 与 op 参数都跟着变，比较已无意义。
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

    private readonly record struct FixtureCase(long Seed, string File, int ComparedSteps);

    private static readonly FixtureCase[] Fixtures =
    {
        new(11111, "fuzz-parity-seed11111.json", 26),
        new(424242, "fuzz-parity-seed424242.json", 13),
        new(9001, "fuzz-parity-seed9001.json", 27),
    };

    [TestCaseSource(nameof(FixtureCases))]
    public void WebFixture_RngOpsAndBehavior_Match(long seed, string fileName, int comparedSteps)
    {
        var fixture = LoadFixture(fileName);
        Assert.That(fixture.Seed, Is.EqualTo(seed));

        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        TestDefaults.EnableFog(session);

        var blast = fixture.Blast;
        Assert.That(comparedSteps, Is.InRange(1, fixture.OpCount));
        var result = CrowdWebFuzzRunner.Run(session, comparedSteps, blast, fixture.Palette, fixture.Seed);
        Assert.That(result.Ok, Is.True, result.Errors is { Count: > 0 } ? string.Join("; ", result.Errors) : "模糊运行失败");
        Assert.That(result.Steps, Has.Count.EqualTo(comparedSteps));

        for (int i = 0; i < comparedSteps; i++)
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
            long slack = CrowdWebFuzzRunner.PositionStatSlackCmPerUnit * expected.Units;
            Assert.That(delta, Is.LessThanOrEqualTo(slack),
                $"第 {i + 1} 步位置 L1 统计差 {delta} cm（{expected.Units} 个单位，上限 {slack}）");
        }
    }

    private static IEnumerable<TestCaseData> FixtureCases()
    {
        foreach (var c in Fixtures)
        {
            yield return new TestCaseData(c.Seed, c.File, c.ComparedSteps)
                .SetName($"Seed{c.Seed}_MatchWebFixture_First{c.ComparedSteps}Steps");
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
}
