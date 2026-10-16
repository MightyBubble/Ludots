using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 模糊测试双端对拍：夹具由 Web 导出脚本写入（同种子、同 catalog 参数、逐步 op 序列）。
/// C# 按夹具 op 顺序回放，结构性字段（存活数、状态断面）须与夹具一致。
/// </summary>
public sealed class CrowdFuzzParityTests
{
    private const long Seed = 11111;
    private const int OpCount = 40;

    [Test]
    public void Seed11111_RecordedOps_MatchWebFixture()
    {
        var fixture = LoadFixture();
        Assert.That(fixture.Seed, Is.EqualTo(Seed));
        Assert.That(fixture.OpCount, Is.EqualTo(OpCount));

        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true;

        var (result, trace) = CrowdFuzzRunner.RunWithTrace(session, fixture.Plan);
        Assert.That(result.Passed, Is.True, result.Error);
        Assert.That(trace, Has.Count.EqualTo(fixture.Steps.Count));

        for (int i = 0; i < fixture.Steps.Count; i++)
        {
            var expected = fixture.Steps[i];
            var actual = trace[i];
            Assert.That(actual.OpId, Is.EqualTo(expected.OpId), $"第 {i + 1} 步 op");
            Assert.That(actual.Units, Is.EqualTo(expected.Units), $"第 {i + 1} 步存活数");
            Assert.That(actual.StateDigest, Is.EqualTo(expected.StateDigest),
                $"第 {i + 1} 步状态断面（结构性字段，须与 Web 基线一致）");
        }
    }

    [Test]
    [Explicit("用 Web exportFuzzParity.mjs 重新生成夹具后，可本地对照 C# 轨迹是否仍一致")]
    public void Seed11111_RunWithTrace_MatchesSelfFixture()
    {
        var fixture = LoadFixture();
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true;

        var (result, trace) = CrowdFuzzRunner.RunWithTrace(session, fixture.Plan);
        Assert.That(result.Passed, Is.True, result.Error);
        for (int i = 0; i < fixture.Steps.Count; i++)
        {
            Assert.That(trace[i].OpId, Is.EqualTo(fixture.Steps[i].OpId));
            Assert.That(trace[i].Units, Is.EqualTo(fixture.Steps[i].Units));
            Assert.That(trace[i].StateDigest, Is.EqualTo(fixture.Steps[i].StateDigest));
            Assert.That(trace[i].PositionDigest, Is.EqualTo(fixture.Steps[i].PositionDigest));
        }
    }

    private static CrowdFuzzParityFixture LoadFixture()
    {
        string path = Path.Combine(
            RepoRoot(),
            "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/fuzz-parity-seed11111.json");
        return CrowdFuzzParityFixture.Load(path);
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
    [Explicit("维护：用 C# 轨迹写出夹具骨架；Web exportFuzzParity.mjs 重新导出后须与本测试对拍一致")]
    public void GenerateFuzzParityFixture()
    {
        var plan = CatalogPlan(Seed, OpCount);
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true;

        var (result, trace) = CrowdFuzzRunner.RunWithTrace(session, plan);
        Assert.That(result.Passed, Is.True, result.Error);

        var fixture = new CrowdFuzzParityFixture
        {
            ParitySchema = "crowd-fuzz-parity-v1",
            Seed = Seed,
            OpCount = OpCount,
            Plan = plan,
            Steps = trace,
        };
        string path = Path.Combine(
            RepoRoot(),
            "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/fuzz-parity-seed11111.json");
        fixture.Save(path);
        TestContext.Out.WriteLine($"Wrote {path}");
    }

    internal static CrowdFuzzPlan CatalogPlan(long seed, int opCount)
    {
        string path = Path.Combine(
            RepoRoot(),
            "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/catalog.json");
        return CrowdSimRegressCatalog.Parse(JsonNode.Parse(File.ReadAllText(path))!.AsObject()).CreateFuzzPlan(seed, opCount);
    }
}
