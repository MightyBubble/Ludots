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
/// 同脚本同真值跑两遍:基线遍不改 mode;注入遍在"结构字段逐位一致而 mode 不同"的样本上
/// 把 C# 单位 mode 写成真值 mode(Intent 按上一 tick 的 mode 取视线滞回,注入会被下一 tick 消费)。
/// 锁住的是注入后的结果,不是某一次的样本个数:最大偏差仍高于厘米门槛(把 mode 对齐并不会把
/// 位置差收进 1cm),mode 不一致样本率不超过 S7 主门的双容忍。持久分叉头数只打印,不是合同。
/// </summary>
public sealed class S7ModeFlipInjectionTests
{
    /// <summary>持久分叉与"注入后仍未收进 1cm"共用的厘米门槛。</summary>
    private const double ResidualFloorCm = 1.0;

    [Test]
    public void InjectingMode_ResidualStaysAboveCentimeter_CountsStayBounded()
    {
        var baseline = Run(inject: false);
        var injected = Run(inject: true);

        TestContext.Out.WriteLine(
            $"基线(不注入): mode不一致 {baseline.ModeFlips}/{baseline.Samples}, 持久分叉 {baseline.PersistentUnits} 单位, max {baseline.MaxCm:F2}cm, 末30帧 max {baseline.FinalMaxCm:F2}cm, p50 {baseline.P50:F4}cm, 槽位抽查间隔 {baseline.SlotCheckInterval};" +
            $"注入: mode注入 {injected.Injections} 次, max {injected.MaxCm:F2}cm, 末30帧 max {injected.FinalMaxCm:F2}cm, p50 {injected.P50:F4}cm");
        foreach (var kv in baseline.FirstCross)
        {
            TestContext.Out.WriteLine($"基线首叉(> {kv.Key}cm): {kv.Value}");
        }

        Assert.That(baseline.StructuralMismatches, Is.EqualTo(0), "基线结构字段必须逐位一致(证明前提)");
        // 槽位视线每 SlotCheckInterval tick 才重判,判定粘住到下一次重判,所以不一致按样本累计。
        // 上界与 S7 主门同一条样本率;下界 0,规格不要求必须出现不一致。
        int modeCeiling = (int)Math.Floor(baseline.Samples * S7RebakeTruthTests.MaxModeMismatchRate);
        Assert.That(baseline.ModeFlips, Is.InRange(0, modeCeiling),
            $"mode 不一致样本 {baseline.ModeFlips} 超出 0..{modeCeiling}（{baseline.Samples} 样本 × {S7RebakeTruthTests.MaxModeMismatchRate}）");
        Assert.That(injected.StructuralMismatches, Is.EqualTo(0), "注入后结构字段仍须逐位一致");
        if (baseline.ModeFlips > 0)
        {
            Assert.That(injected.Injections, Is.LessThan(baseline.ModeFlips),
                "存在 mode 不一致时,注入次数应少于基线不一致数(后续不一致点随轨迹改变,不再按基线时序出现)");
        }
        else
        {
            Assert.That(injected.Injections, Is.EqualTo(0), "基线没有 mode 不一致时不应发生注入");
        }

        Assert.That(injected.MaxCm, Is.GreaterThan(ResidualFloorCm),
            $"注入 mode 后最大偏差 {injected.MaxCm:F2}cm 已落到 ≤{ResidualFloorCm}cm");
        Assert.That(baseline.P50, Is.LessThanOrEqualTo(S7RebakeTruthTests.P50BandCm),
            $"基线 p50 须在 S7 主门分层带内({S7RebakeTruthTests.P50BandCm}cm)");
    }

    private sealed record Stats(
        double MaxCm, double FinalMaxCm, double P50, int ModeFlips, int Injections, int PersistentUnits, int StructuralMismatches,
        int Samples, int SlotCheckInterval,
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
            if (!session.Step(out _))
            {
                int guard = 0;
                while (!session.Step(out _))
                {
                    if (guard++ > 4096) throw new InvalidOperationException($"tick {t} 停摆未恢复。");
                }
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

                int s = d > ResidualFloorCm ? streak.GetValueOrDefault(handle) + 1 : 0;
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
        return new Stats(
            maxCm, finalMaxCm, deltas[deltas.Count / 2], modeFlips, injections, persistent.Count, structural,
            deltas.Count, session.Config.Movement.SlotCheckInterval, firstCross);
    }
}
