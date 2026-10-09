using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S7-b 迷雾/认知对拍(脚本:部署 → 行军 → 未知建筑 → 发现改道 → 拆除残影 → 再见遗忘 →
/// reveal/obscure/forget → fogShare 并组 → fogTerrain 开关对照):迷雾开启口径,不置
/// TruthNavFrozen(参考端 S7-b 块不置 __S7_TRUTH_NAV__,与 S7 冻结块并存)。
/// 迷雾 digest 主门(F02):每组每 tick visible/explored/belief/tiles 计数、beliefKey、
/// 槽号、entRev/rev 计数器 + 全局槽账(seq/switches/entry/dormant/beliefCount)——整数/集合态,
/// 逐位硬门,无带宽口径(哈希碰撞藏不住变更:计数器与 key 双轨)。
/// 单位门沿用 S5/S7 家规:状态机逐位 + 位置分层带(带值按本场景实测校准)+ contacts 双容忍;
/// 结构 op(place@5 挡路改道、remove@90 残影语义)报告逐字段硬门。
/// 认知变体导航的内容一致性由本门背书:槽位分配/切换/揭示/淘汰全部逐位,变体烘焙的分歧
/// 矩形由槽字段(实体集+tile 集)唯一决定。
/// 实测校准(2026-10-09,360 tick × 120 单位):digest 全程逐位;ops(place@5/remove@90)全字段;
/// 轨迹 p50=0.08/p99=0.45cm(行军段双数值系固有误差档);到达判定骑线(L18 族)使末段停车
/// 晚 1–9 tick——状态翻转 99 处全部为 Moving↔Arrived 到达对(非到达翻转 0,零容忍),末态带
/// 5000(实测 3491,该带防整组不收束);contacts 对齐门 0.5% + ±3(实测 0.147%,幅度直方
/// [±1:57,±2:3,±3:3]——到达簇内多邻同时骑线;系统性求解分歧会同时击穿率与幅度线)。
/// </summary>
public sealed class S7FogTruthTests
{
    /// <summary>位置带宽(厘米):实测见输出行,按约 3 倍余量定值(迷雾场景的绕行差与 S7 同源:
    /// 重规划落帧的级联;未知建筑在发现前不参与认知规划——放件即真值重烘反应,与认知无关)。</summary>
    private const double MaxTrajectoryBandCm = 15000.0;
    /// <summary>末态收束带(厘米):到达判定骑线(L18 族)使停车比参考端晚 1–9 tick,实测末 30 tick
    /// max 3491.5(到达对翻转 99 处全为 Moving→Arrived 晚翻;非到达翻转 0、digest 逐位、ops 全字段)。
    /// 该带防的是整组不收束/卡墙(数百米量级),取 5000。</summary>
    private const double FinalBandCm = 5000.0;
    private const int FinalTicks = 30;
    /// <summary>位置分层门 p50(厘米)。</summary>
    private const double P50BandCm = 25.0;
    /// <summary>mode 双容忍:重规划落帧的槽位视线骑线翻转(与 S7 同源)。</summary>
    private const double MaxModeMismatchRate = 0.0015;
    private const double AlignedDeltaCm = 1.0;

    [Test]
    public void S7Fog_MatchesWebTruth()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.VerifyIncrementalNav = true;

        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s7fog-truth.json")))!;
        var script = meta["script"]!.AsArray()
            .Select(e => new CrowdCommand(e!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(e!["cmd"]!)))
            .ToArray();
        var truthExecTicks = meta["opExecTicks"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
        session.Commands.Schedule(session, script);

        var reports = new List<CrowdRebakeReport>();
        session.RebakeReported += reports.Add;
        var fog = session.Fog!;
        var belief = session.Belief!;

        var bin = File.ReadAllBytes(Path.Combine(dir, "s7fog.bin"));
        int cursor = 4; // magic LS7F
        int ticks = BitConverter.ToInt32(bin, cursor); cursor += 4;
        int groups = BitConverter.ToInt32(bin, cursor); cursor += 4;
        Assert.That(groups, Is.EqualTo(fog.G), "视野组数与真值不一致");

        double maxDeltaCm = 0, finalMaxDeltaCm = 0;
        var deltas = new List<double>();
        int modeMismatches = 0, contactMismatches = 0, contactMaxDelta = 0, contactSamples = 0, alignedContactSamples = 0;
        string? firstModeMismatch = null, firstContactMismatch = null, firstStructuralMismatch = null, arrivalFlipFirst = null;
        int stateFlips = 0, nonArrivalFlips = 0;
        var ampHist = new int[6];
        string? firstDigestMismatch = null;
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
                int tContacts = BitConverter.ToUInt16(bin, cursor); cursor += 2;

                var entity = session.Units.EntityAt(i);
                Assert.That(session.Units.HandleAt(i), Is.EqualTo(handle), $"tick {t} 单位 {i} 句柄错位");
                var pos = session.World.Get<Ludots.Core.Components.WorldPositionCm>(entity).Value;
                double d = Math.Max(Math.Abs(pos.X.ToDouble() - txCm), Math.Abs(pos.Y.ToDouble() - tyCm));
                deltas.Add(d);
                if (d > maxDeltaCm) maxDeltaCm = d;
                if (t >= ticks - FinalTicks && d > finalMaxDeltaCm) finalMaxDeltaCm = d;

                if (d <= AlignedDeltaCm)
                {
                    int contactDelta = Math.Abs(session.Movement!.Contacts[i] - tContacts);
                    if (contactDelta > 0)
                    {
                        contactMismatches++;
                        contactMaxDelta = Math.Max(contactMaxDelta, contactDelta);
                        firstContactMismatch ??= $"tick {t} 单位 {i}: 我 {session.Movement.Contacts[i]} vs 参考 {tContacts}(位置对齐)";
                    }

                    alignedContactSamples++;
                    if (contactDelta > 0) ampHist[contactDelta < ampHist.Length ? contactDelta : ampHist.Length - 1]++;
                }

                contactSamples++;
                var st = session.World.Get<CrowdSimulationUnitState>(entity);
                if (st.State != tState || st.Level != tLevel || st.Order != tOrder)
                {
                    stateFlips++;
                    bool arrivalFlip = st.Level == tLevel && st.Order == tOrder &&
                        ((st.State == (byte)CrowdUnitState.Arrived && tState == (byte)CrowdUnitState.Moving) ||
                         (st.State == (byte)CrowdUnitState.Moving && tState == (byte)CrowdUnitState.Arrived));
                    if (!arrivalFlip)
                    {
                        nonArrivalFlips++;
                        firstStructuralMismatch ??= $"tick {t} 单位 {i}(句柄 {handle:x}): 我({st.State},{st.Mode},{st.Level},{st.Order}) vs 参考({tState},{tMode},{tLevel},{tOrder})";
                    }
                    else if (arrivalFlipFirst == null)
                    {
                        arrivalFlipFirst = $"tick {t} 单位 {i}: 我 {st.State} vs 参考 {tState}";
                    }
                }
                else if (st.Mode != tMode)
                {
                    modeMismatches++;
                    firstModeMismatch ??= $"tick {t} 单位 {i}: mode 我 {st.Mode} vs 参考 {tMode}";
                }
            }

            // ── 迷雾 digest:逐位硬门(整数/集合态) ──
            int f2 = fog.F * fog.F;
            for (int g = 0; g < groups; g++)
            {
                int tVis = BitConverter.ToInt32(bin, cursor); cursor += 4;
                int tExp = BitConverter.ToInt32(bin, cursor); cursor += 4;
                int tBel = BitConverter.ToInt32(bin, cursor); cursor += 4;
                int tTiles = BitConverter.ToInt32(bin, cursor); cursor += 4;
                uint tKey = BitConverter.ToUInt32(bin, cursor); cursor += 4;
                int tSlot = BitConverter.ToInt32(bin, cursor); cursor += 4;
                int tEntRev = BitConverter.ToInt32(bin, cursor); cursor += 4;
                int tRev = BitConverter.ToInt32(bin, cursor); cursor += 4;

                int vis = 0, exp = 0;
                for (int c = g * f2, end = (g + 1) * f2; c < end; c++)
                {
                    if (fog.Visible[c] != 0) vis++;
                    if (fog.Explored[c] != 0) exp++;
                }

                firstDigestMismatch ??= Mismatch(
                    ("visible", vis, tVis), ("explored", exp, tExp), ("belief", fog.Belief[g].Count, tBel),
                    ("tiles", fog.Tiles[g].Count, tTiles), ("beliefKey", unchecked((int)fog.BeliefKey(g)), unchecked((int)tKey)),
                    ("slot", belief.Slot[g], tSlot), ("entRev", (int)fog.EntRev[g], tEntRev), ("rev", (int)fog.Rev[g], tRev))
                    is { } gm ? $"tick {t} 组 {g}: {gm}" : null;
            }

            int tSeq = BitConverter.ToInt32(bin, cursor); cursor += 4;
            int tSw = BitConverter.ToInt32(bin, cursor); cursor += 4;
            int tEnt = BitConverter.ToInt32(bin, cursor); cursor += 4;
            int tDor = BitConverter.ToInt32(bin, cursor); cursor += 4;
            int tBc = BitConverter.ToInt32(bin, cursor); cursor += 4;
            firstDigestMismatch ??= Mismatch(
                ("seq", belief.Seq, tSeq), ("switches", belief.Switches, tSw), ("entry", belief.Entries.Count, tEnt),
                ("dormant", belief.DormantList.Count, tDor), ("beliefCount", fog.BeliefCount(), tBc))
                is { } gm2 ? $"tick {t} 全局: {gm2}" : null;
        }

        Assert.That(cursor, Is.EqualTo(bin.Length), "s7fog.bin 末尾有多余字节");
        Assert.That(firstDigestMismatch, Is.Null, $"迷雾 digest 逐位不一致(首个):{firstDigestMismatch}");

        // ── 结构 op 报告逐字段硬门(与 S7 同族;place@5 未知建筑 / remove@90 残影) ──
        var opsBin = File.ReadAllBytes(Path.Combine(dir, "s7fog-ops.bin"));
        Assert.That(opsBin.Length, Is.GreaterThanOrEqualTo(8), "s7fog-ops.bin 截断");
        int opCount = BitConverter.ToInt32(opsBin, 4);
        Assert.That(reports.Count, Is.EqualTo(opCount),
            $"重烘焙报告数 {reports.Count} ≠ 真值 {opCount}(execTick 序:我 [{string.Join(',', reports.Select(r => r.ExecTick))}] vs 参考 [{string.Join(',', truthExecTicks)}])");
        int oc = 8;
        for (int i = 0; i < opCount; i++)
        {
            var r = reports[i];
            int exec = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int kind = opsBin[oc]; oc += 1;
            int repTick = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int tiles = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int contexts = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int costOnly = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int hits = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int misses = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int orders = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int refreshes = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int evicted = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int stuck = BitConverter.ToInt32(opsBin, oc); oc += 4;
            int unionCount = BitConverter.ToInt32(opsBin, oc); oc += 4;
            var union = new List<int>();
            for (int k = 0; k < unionCount; k++)
            {
                union.Add(BitConverter.ToInt32(opsBin, oc));
                oc += 4;
            }

            string What(string field) => $"op {i}(exec {exec}) {field}";
            Assert.That(exec, Is.EqualTo(truthExecTicks[i]), What("execTick 与真值脚本序不一致"));
            Assert.That(r.ExecTick, Is.EqualTo(exec), What("执行 tick"));
            Assert.That(r.Kind, Is.EqualTo(kind), What("类别(1 放置 / 2 拆除)"));
            Assert.That(r.ReportTick, Is.EqualTo(repTick), What("报告 tick"));
            Assert.That(r.Tiles, Is.EqualTo(tiles), What("受影响 tile 并集大小"));
            Assert.That(r.Contexts, Is.EqualTo(contexts), What("看到变化的上下文数"));
            Assert.That(r.CostOnly, Is.EqualTo(costOnly), What("仅代价上下文数"));
            Assert.That(r.Hits, Is.EqualTo(hits), What("tile 缓存命中"));
            Assert.That(r.Misses, Is.EqualTo(misses), What("tile 缓存未命中"));
            Assert.That(r.Orders, Is.EqualTo(orders), What("重规划指令数")); // L22-⑤ 同族:重规划计数在硬门内
            Assert.That(r.Refreshes, Is.EqualTo(refreshes), What("流场刷新队列长"));
            Assert.That(r.Evicted, Is.EqualTo(evicted), What("挤离单位数"));
            Assert.That(r.Stuck, Is.EqualTo(stuck), What("无处安放数"));
            Assert.That(r.UnionTiles, Is.EqualTo(union).AsCollection, What("受影响 tile 并集(升序)"));
        }

        Assert.That(oc, Is.EqualTo(opsBin.Length), "s7fog-ops.bin 末尾有多余字节");

        // ── 单位门(家规) ──
        deltas.Sort();
        double p50 = deltas[deltas.Count / 2], p99 = deltas[(int)(deltas.Count * 0.99)], p999 = deltas[(int)(deltas.Count * 0.999)];
        double modeRate = (double)modeMismatches / Math.Max(1, contactSamples);
        double alignedRate = (double)contactMismatches / Math.Max(1, alignedContactSamples);
        TestContext.Out.WriteLine(
            $"S7Fog 轨迹偏差(厘米): p50={p50:F4} p99={p99:F2} p99.9={p999:F2} max={maxDeltaCm:F2} 末{FinalTicks}帧max={finalMaxDeltaCm:F2};" +
            $"mode 单字段翻 {modeMismatches}/{contactSamples} ({modeRate:P3});contacts 对齐硬门 {contactMismatches}/{alignedContactSamples} ({alignedRate:P3});" +
            $"digest 全程逐位一致;报告 {reports.Count} 笔全字段一致;槽账 seq={belief.Seq} switches={belief.Switches};" +
            $"状态翻转 {stateFlips}(到达对 {stateFlips - nonArrivalFlips},非到达 {nonArrivalFlips}) contacts幅度直方图=[{string.Join(",", ampHist)}]");
        Assert.That(nonArrivalFlips, Is.EqualTo(0), $"非到达对的状态翻转(结构字段):{firstStructuralMismatch}");
        Assert.That(stateFlips, Is.LessThanOrEqualTo(contactSamples / 100), $"到达对翻转 {stateFlips} 超 1% 容忍(首个:{arrivalFlipFirst})");
        Assert.That(modeRate, Is.LessThanOrEqualTo(MaxModeMismatchRate),
            $"mode 双容忍:不一致 {modeMismatches}/{contactSamples} ({modeRate:P3}) 超 {MaxModeMismatchRate:P2}(首个:{firstModeMismatch})");
        Assert.That(contactMismatches, Is.LessThanOrEqualTo(Math.Max(1, alignedContactSamples / 200)),
            $"接触计数硬门(位置对齐样本):不一致 {contactMismatches}/{alignedContactSamples} 样本超 0.5% 容忍(首个:{firstContactMismatch})");
        Assert.That(contactMaxDelta, Is.LessThanOrEqualTo(3),
            $"接触计数硬门:单样本幅度 {contactMaxDelta} 超过骑线翻转上限 ±3(首个:{firstContactMismatch})");
        Assert.That(p50, Is.LessThanOrEqualTo(P50BandCm), $"分层门:p50={p50:F4}cm 超 {P50BandCm}cm");
        Assert.That(maxDeltaCm, Is.LessThanOrEqualTo(MaxTrajectoryBandCm),
            $"轨迹逐 tick 位置最大偏差 {maxDeltaCm:F2}cm 超带宽 {MaxTrajectoryBandCm}cm(结构性兜底门)");
        Assert.That(finalMaxDeltaCm, Is.LessThanOrEqualTo(FinalBandCm),
            $"末 {FinalTicks} tick 未收束:最大偏差 {finalMaxDeltaCm:F2}cm 超 {FinalBandCm}cm");
    }

    private static string? Mismatch(params (string Name, int Mine, int Truth)[] fields)
    {
        foreach (var (name, mine, truth) in fields)
        {
            if (mine != truth) return $"{name} 我 {mine} vs 参考 {truth}";
        }

        return null;
    }
}
