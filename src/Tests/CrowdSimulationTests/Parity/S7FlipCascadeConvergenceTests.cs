using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 残余分叉收敛证明(L53,结论:翻转级联假设被否定,根 = 参考端 f32 存储 × 密集接触放大)。
/// 方法:同脚本同真值跑两遍——基线遍不动;注入遍在每个"结构字段逐位一致而 mode 不同"的
/// tick 末把 C# 单位 mode 强制为真值 mode(mode 是带滞后的行为输入,Intent 按上一 tick 的
/// mode 取迟滞门限,注入会被下一 tick 真实消费)。
/// 实测证据(2026-10-09,s1337 真值,360 tick × 120 单位):
/// ① 基线:mode 翻 13 处、持久分叉 32 单位、max 2407.23cm、末 30 帧 max 1258.77cm、
///    p50 0.0693cm(与 S7 主门实测同数);
/// ② 注入:只发生 2 次注入(翻转点路径依赖——首注后轨迹改道,其余 11 处不再按基线时序
///    出现),max 仍 2407.23cm——翻转级联假设否定;
/// ③ 首 &gt;1cm 分叉:tick 40 单位 37(句柄 25),该点 mode 两端相等(1/1)——分叉起点
///    与 mode 翻转无关,与 ② 互证;
/// ④ L18 法马达中间量逐语句对照(单位 37,tick 36–44,双端临时探针已删):mode/blend/
///    state 翻转同 tick 同值(blend 1→0.1667、mode 1→0 在 t=41 双端同现),接触数至
///    分叉起点逐 tick 相同(2,0,4,0,9,12);位置差 0.0005m ≈ 参考端 f32 存储在 4937m
///    量级的量化步(f32 eps ≈ 0.00059m),速度差 ~1e-3 m/s,槽位差 0.0007m(参考端 double
///    vs Fix64 算术差);t=40 密集接触事件(ct 9→12,结构 op 绕行的群体压缩)把亚毫米
///    输入差放大:0.27cm(t40)→1.7cm(t41)→沿各自接触链增至米级。
/// 定案:残余分叉根 = 参考端 f32 状态存储的固有量化差,在结构 op 绕行密集接触簇中放大;
/// mode 翻转是同源共症状(分叉使槽位视线骑线,非因)。与 S5 L18 同类(行为口径记档,
/// S7 主门的位置带 15000/3000 即为此设)。
/// 断言按"记档钉"写:数值漂移 = 残余类变化,须重跑本证明并更新注释与 PR 记档。
/// </summary>
public sealed class S7FlipCascadeConvergenceTests
{
    [Test]
    public void ModeFlipInjection_ResidualDivergenceConvergesUnderBand()
    {
        var baseline = Run(inject: false);
        var injected = Run(inject: true);

        TestContext.Out.WriteLine(
            $"基线(不注入): mode翻 {baseline.ModeFlips} 处, 持久分叉 {baseline.PersistentUnits} 单位, max {baseline.MaxCm:F2}cm, 末30帧 max {baseline.FinalMaxCm:F2}cm, p50 {baseline.P50:F4}cm;" +
            $"注入: mode注入 {injected.Injections} 次, max {injected.MaxCm:F2}cm, 末30帧 max {injected.FinalMaxCm:F2}cm, p50 {injected.P50:F4}cm");
        foreach (var kv in baseline.FirstCross)
        {
            TestContext.Out.WriteLine($"基线首叉(> {kv.Key}cm): {kv.Value}");
        }

        Assert.That(baseline.StructuralMismatches, Is.EqualTo(0), "基线结构字段必须逐位一致(证明前提)");
        Assert.That(baseline.ModeFlips, Is.EqualTo(13),
            "mode 翻转数漂移(记档钉 13):残余类变化,重跑本证明并更新注释与 PR 记档");
        Assert.That(baseline.PersistentUnits, Is.EqualTo(32),
            "持久分叉单位数漂移(记档钉 32):残余类变化,重跑本证明并更新注释与 PR 记档");
        Assert.That(injected.StructuralMismatches, Is.EqualTo(0), "注入后结构字段仍须逐位一致");
        Assert.That(injected.Injections, Is.LessThan(baseline.ModeFlips),
            "注入次数应少于基线翻转数(翻转点路径依赖,负结果的签名;若不再成立说明注入语义变了)");
        Assert.That(injected.MaxCm, Is.GreaterThan(1.0),
            "注入后 max 收敛到 ≤1cm:翻转级联假设成立的新证据——推翻本测试记档,重跑并改写结论");
        Assert.That(baseline.P50, Is.LessThanOrEqualTo(0.5), "基线 p50 须在 S7 主门分层带内(0.5cm)");
    }

    private sealed record Stats(
        double MaxCm, double FinalMaxCm, double P50, int ModeFlips, int Injections, int PersistentUnits, int StructuralMismatches,
        IReadOnlyDictionary<double, string> FirstCross);

    private static Stats Run(bool inject)
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true;
        session.VerifyIncrementalNav = true;

        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s7-rebake-truth.json")))!;
        var script = meta["script"]!.AsArray()
            .Select(e => new CrowdCommand(e!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(e!["cmd"]!)))
            .ToArray();
        session.Commands.Schedule(session, script);

        var bin = File.ReadAllBytes(Path.Combine(dir, "s7-rebake.bin"));
        int cursor = 4; // magic
        int ticks = BitConverter.ToInt32(bin, cursor); cursor += 4;
        const int finalTicks = 30;
        const double divergeCm = 1.0;   // 持久分叉判定(与 L26 定性口径一致)
        const int persistStreak = 10;

        double maxCm = 0, finalMaxCm = 0;
        var deltas = new List<double>();
        var streak = new Dictionary<uint, int>();
        var persistent = new HashSet<uint>();
        var firstCross = new Dictionary<double, string>();
        int modeFlips = 0, injections = 0, structural = 0;
        string? firstStructural = null;
        for (int t = 0; t < ticks; t++)
        {
            string? hash = session.Step();
            int guard = 0;
            while (hash == null)
            {
                if (guard++ > 4096) throw new InvalidOperationException($"tick {t} 停摆未恢复。");
                hash = session.Step();
            }

            int truthCount = BitConverter.ToInt32(bin, cursor); cursor += 4;
            Assert.That(session.Units.Count, Is.EqualTo(truthCount), $"tick {t} 单位数不一致");
            for (int i = 0; i < truthCount; i++)
            {
                uint handle = BitConverter.ToUInt32(bin, cursor); cursor += 4;
                double txCm = BitConverter.ToDouble(bin, cursor); cursor += 8;
                double tyCm = BitConverter.ToDouble(bin, cursor); cursor += 8;
                byte tState = bin[cursor++]; byte tMode = bin[cursor++]; byte tLevel = bin[cursor++];
                uint tOrder = BitConverter.ToUInt32(bin, cursor); cursor += 4;
                cursor += 2; // contacts 本证明不消费

                var entity = session.Units.EntityAt(i);
                Assert.That(session.Units.HandleAt(i), Is.EqualTo(handle), $"tick {t} 单位 {i} 句柄错位");

                var pos = session.World.Get<Ludots.Core.Components.WorldPositionCm>(entity).Value;
                double d = Math.Max(Math.Abs(pos.X.ToDouble() - txCm), Math.Abs(pos.Y.ToDouble() - tyCm));
                deltas.Add(d);
                if (d > maxCm) maxCm = d;
                if (t >= ticks - finalTicks && d > finalMaxCm) finalMaxCm = d;

                var st = session.World.Get<CrowdSimulationUnitState>(entity);
                foreach (double th in new[] { 0.1, 1.0, 100.0 })
                {
                    if (d > th && !firstCross.ContainsKey(th))
                    {
                        firstCross[th] = $"tick {t} 单位 {i}(句柄 {handle:x}) d={d:F4}cm mode 我{st.Mode}/参考{tMode}";
                    }
                }

                int s = d > divergeCm ? streak.GetValueOrDefault(handle) + 1 : 0;
                streak[handle] = s;
                if (s == persistStreak) persistent.Add(handle);

                if (st.State != tState || st.Level != tLevel || st.Order != tOrder)
                {
                    structural++;
                    firstStructural ??= $"tick {t} 单位 {i}(句柄 {handle:x})";
                }
                else if (st.Mode != tMode)
                {
                    modeFlips++;
                    if (inject)
                    {
                        st.Mode = tMode;
                        session.World.Set(entity, st);
                        injections++;
                    }
                }
            }
        }

        Assert.That(cursor, Is.EqualTo(bin.Length), "s7-rebake.bin 末尾有多余字节");
        Assert.That(structural, Is.EqualTo(0), $"结构字段不一致(证明前提破坏,首个:{firstStructural})");
        deltas.Sort();
        return new Stats(maxCm, finalMaxCm, deltas[deltas.Count / 2], modeFlips, injections, persistent.Count, structural, firstCross);
    }
}
