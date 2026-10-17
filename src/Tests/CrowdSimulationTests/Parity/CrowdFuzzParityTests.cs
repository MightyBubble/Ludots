using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using Schedulers;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 模糊测试双端对拍：夹具由 <c>scripts/exportFuzzTrace.mjs</c>（正本 <c>tools/crowdsimulation-export/</c>）从 Web <c>fuzzIter</c> 导出，
/// 逐 op 逐步记录 + 逐 tick 逐单位 trace。比较全部走 <see cref="CrowdWebFuzzTraceCompare"/>：句柄身份、结构状态、
/// 导航版本、领队路径、逐单位位置两道门（逐轴 1 厘米 / 逐轴绝对误差均值 4 厘米）。
/// 每个种子只比到 <see cref="FixtureCase.StrictGreenOps"/>：再往后两端在重烘导航版本与运动状态上真实分叉
/// （当前已知分歧清单锁定在 <see cref="CrowdFuzzTraceParityTests"/>），后续行为修复收线后前缀随之外推。
/// </summary>
public sealed class CrowdFuzzParityTests
{
    /// <summary>本 fixture 独占并拥有单线程调度器；拆除时恢复进入前的实例（可能是别人的），只释放自建实例。</summary>
    private CrowdSchedulerLease? _schedulerLease;

    [OneTimeSetUp]
    public void OwnSingleWorkerScheduler() => _schedulerLease = new CrowdSchedulerLease("CrowdFuzzParity", 1);

    [OneTimeTearDown]
    public void ReleaseOwnedScheduler()
    {
        _schedulerLease?.Dispose();
        _schedulerLease = null;
    }

    private readonly record struct FixtureCase(long Seed, string File, int StrictGreenOps);

    private static readonly FixtureCase[] Fixtures =
    {
        new(11111, "fuzz-trace-seed11111.json", 25),
        new(424242, "fuzz-trace-seed424242.json", 10),
        new(9001, "fuzz-trace-seed9001.json", 18),
    };

    [TestCaseSource(nameof(FixtureCases))]
    public void WebTrace_StrictCompare_GreenForPrefix(long seed, string fileName, int strictGreenOps)
    {
        var fixture = LoadFixture(fileName);
        Assert.That(fixture.Seed, Is.EqualTo(seed));
        Assert.That(fixture.OpCount, Is.GreaterThanOrEqualTo(strictGreenOps));

        var result = RunFuzz(fixture, strictGreenOps, out var ticks);
        var report = CrowdWebFuzzTraceCompare.Compare(
            fixture.Steps.Take(strictGreenOps).ToList(), fixture.Ticks.Take(strictGreenOps * 2).ToList(),
            result.Steps, ticks);
        Assert.That(report.Green, Is.True, report.Describe());
        Assert.That(report.ComparedOps, Is.EqualTo(strictGreenOps));
        Assert.That(report.ComparedTicks, Is.EqualTo(strictGreenOps * 2));
    }

    /// <summary>
    /// 顺序组合：别的 fixture（经 <see cref="CrowdTestScheduler.Ensure"/>）先建调度器 → 本类租约接管并释放 →
    /// 回到别人的实例；反向：清场到 null 后本类租约先建先放 → 回 null → 别的 fixture 再建。两个方向结束后
    /// 各跑一段真实会话，证明恢复出的调度器仍可并行步进，没有跨 fixture 污染。全程结束后回到本 fixture
    /// 自己的调度器；本测试经 Ensure 自建的实例由本测试自己收尾，绝不动别人的。
    /// </summary>
    [Test]
    public void SchedulerLease_ComposesWithOtherFixturesInBothOrders()
    {
        var saved = World.SharedJobScheduler;
        World.SharedJobScheduler = null;
        JobScheduler? lastEnsureOwned = null;
        try
        {
            CrowdTestScheduler.Ensure();
            var firstEnsureOwned = World.SharedJobScheduler;
            Assert.That(firstEnsureOwned, Is.Not.Null);
            using (var lease = new CrowdSchedulerLease("CrowdFuzzCompositionA", 1))
            {
                Assert.That(World.SharedJobScheduler, Is.SameAs(lease.Owned));
                StepFreshSessionAFewTicks();
            }

            Assert.That(World.SharedJobScheduler, Is.SameAs(firstEnsureOwned), "租约释放后必须回到别的 fixture 的实例。");
            StepFreshSessionAFewTicks();

            World.SharedJobScheduler = null;
            firstEnsureOwned.Dispose();
            using (var lease = new CrowdSchedulerLease("CrowdFuzzCompositionB", 1))
            {
                Assert.That(World.SharedJobScheduler, Is.SameAs(lease.Owned));
                StepFreshSessionAFewTicks();
            }

            Assert.That(World.SharedJobScheduler, Is.Null, "清场到 null 后建租约，释放后应回到 null。");
            CrowdTestScheduler.Ensure();
            lastEnsureOwned = World.SharedJobScheduler;
            Assert.That(lastEnsureOwned, Is.Not.Null);
            StepFreshSessionAFewTicks();
        }
        finally
        {
            World.SharedJobScheduler = saved;
            lastEnsureOwned?.Dispose();
        }
    }

    /// <summary>
    /// 释放路径的异常注入：自建实例的释放动作抛错（真实停线程后抛）时，旧实例必须已恢复且仍可并行步进，
    /// 重复 Dispose 幂等、不许把已恢复的全局抹回 null。测试体抛错时 using 的释放同样必须恢复旧实例。
    /// </summary>
    [Test]
    public void SchedulerLease_DisposalThrows_PreviousInstanceStillRestoredAndReusable()
    {
        var saved = World.SharedJobScheduler;
        World.SharedJobScheduler = null;
        JobScheduler? ensureOwned = null;
        try
        {
            CrowdTestScheduler.Ensure();
            ensureOwned = World.SharedJobScheduler;
            var lease = new ThrowingDisposalLease();
            Assert.That(World.SharedJobScheduler, Is.SameAs(lease.Owned));

            Assert.Throws<InvalidOperationException>(() => lease.Dispose());
            Assert.That(World.SharedJobScheduler, Is.SameAs(ensureOwned), "释放动作抛错也必须先恢复旧实例。");

            Assert.DoesNotThrow(() => lease.Dispose(), "重复 Dispose 幂等，不得动已恢复的全局。");
            Assert.That(World.SharedJobScheduler, Is.SameAs(ensureOwned));
            StepFreshSessionAFewTicks();
        }
        finally
        {
            World.SharedJobScheduler = saved;
            ensureOwned?.Dispose();
        }
    }

    [Test]
    public void SchedulerLease_TestBodyThrows_UsingStillRestoresPrevious()
    {
        var saved = World.SharedJobScheduler;
        World.SharedJobScheduler = null;
        JobScheduler? ensureOwned = null;
        try
        {
            CrowdTestScheduler.Ensure();
            ensureOwned = World.SharedJobScheduler;
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var lease = new CrowdSchedulerLease("CrowdFuzzLeaseBodyThrow", 1);
                StepFreshSessionAFewTicks();
                throw new InvalidOperationException("模拟测试体失败");
            });

            Assert.That(World.SharedJobScheduler, Is.SameAs(ensureOwned), "测试体抛错时 using 的释放仍要恢复旧实例。");
            StepFreshSessionAFewTicks();
        }
        finally
        {
            World.SharedJobScheduler = saved;
            ensureOwned?.Dispose();
        }
    }

    private sealed class ThrowingDisposalLease : CrowdSchedulerLease
    {
        public ThrowingDisposalLease() : base("CrowdFuzzLeaseFaultInjection", 1)
        {
        }

        protected override void DisposeOwned(JobScheduler owned)
        {
            owned.Dispose();
            throw new InvalidOperationException("注入的拆除异常");
        }
    }

    private static void StepFreshSessionAFewTicks()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        CrowdDeployment.SpawnAt(session, Fix64.FromDouble(800000), Fix64.FromDouble(800000), 8, 1, 0, 0);
        session.Advance(4);
    }

    internal static CrowdWebFuzzRunResult RunFuzz(
        CrowdWebFuzzTraceFixture fixture, int opCount, out List<CrowdWebFuzzTickTrace> ticks)
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        TestDefaults.EnableFog(session);
        ticks = new List<CrowdWebFuzzTickTrace>(opCount * 2);
        var result = CrowdWebFuzzRunner.Run(session, opCount, fixture.Blast, fixture.Palette, fixture.Seed, ticks);
        Assert.That(result.Ok, Is.True, result.Errors is { Count: > 0 } ? string.Join("; ", result.Errors) : "模糊运行失败");
        Assert.That(result.Steps, Has.Count.EqualTo(opCount));
        Assert.That(ticks, Has.Count.EqualTo(opCount * 2));
        return result;
    }

    private static IEnumerable<TestCaseData> FixtureCases()
    {
        foreach (var c in Fixtures)
        {
            yield return new TestCaseData(c.Seed, c.File, c.StrictGreenOps)
                .SetName($"Seed{c.Seed}_StrictTraceGreen_First{c.StrictGreenOps}Ops");
        }
    }

    /// <summary>种子的绿前缀 op 数；null = 该种子不在绿前缀表里（供棘轮交叉锚定）。</summary>
    internal static int? TryStrictGreenOpsFor(long seed)
    {
        foreach (var f in Fixtures)
        {
            if (f.Seed == seed) return f.StrictGreenOps;
        }

        return null;
    }

    internal static CrowdWebFuzzTraceFixture LoadFixture(string fileName)
    {
        string path = Path.Combine(RegressDir(),
            "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress",
            fileName);
        return CrowdWebFuzzTraceFixture.Load(path);
    }

    private static string RegressDir()
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
