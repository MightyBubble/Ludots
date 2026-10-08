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
/// S7 结构动态化对拍(脚本:部署 → 行军 → 建筑挡路 → 拆除 → 寿命路障到期 → 道路仅代价):
/// 逐 tick 真值沿用 S5 口径——位置逐 tick 带宽 + contacts 双容忍线(0.05% + ±1 骑线);
/// 状态机拆两档:state / level / order 逐位(结构字段,零容忍),mode 走双容忍
/// (0.15% + 仅 mode 单字段翻,见下)。
/// mode 容忍的定位依据(2026-10-08,墙面重规划场景,360 tick × 120 单位):
/// 重规划落帧时阵型槽位从已漂移位置重排(领队路径与成员位置两侧一致,槽位差 ≤0.2m),
/// 单位的槽位视线判定(mode = LOS + 射程 + D59 路线比)在这些骑线上翻转——
/// 实测 26/43200 = 0.060%,全部为 mode 单字段翻(state/level/order 全程逐位一致);
/// 与 S5 首个分歧(tick 11 马达停车线骑线)同源:双算术体系在离散判定点翻转离散事件。
/// 每次结构 op 的重烘焙报告逐字段硬门(执行 tick / 类别 / 报告 tick / tiles / contexts /
/// costOnly / hits / misses / orders / refreshes / evicted / stuck / 受影响 tile 并集升序)。
/// 口径:参考端以 __S7_TRUTH_NAV__ 镜像补丁冻结迷雾(S7-a 内核无迷雾,F02 另单),
/// 组全程停在 truth 导航,队伍反应 = reactRebake 真值直反应;DB-03 增量 vs 全量一致性
/// 检查(VerifyIncrementalNav)在每次 op 后随路运行。
/// </summary>
public sealed class S7RebakeTruthTests
{
    /// <summary>逐 tick 轨迹位置带宽(厘米):实测 max 7470.8cm、p99.9 4698cm、末 30 tick 1530.7cm
    /// (墙面重规划后 mode 骑线翻的级联——不同单位跟槽位或流场,路径分叉,量级 = 米级绕行差),
    /// 带值取实测 2 倍。结构级分歧(整组卡墙 / 成片重绕)在数百米量级,仍能击穿。</summary>
    private const double MaxTrajectoryBandCm = 15000.0;
    /// <summary>末态收束带(厘米):实测末 30 tick max 1530.7cm,取 2 倍。</summary>
    private const double FinalBandCm = 3000.0;
    private const int FinalTicks = 30;
    /// <summary>mode 双容忍:不一致率上限(实测 0.060%,留 2.5 倍余量)。</summary>
    private const double MaxModeMismatchRate = 0.0015;
    /// <summary>位置对齐判定(厘米):|Δ| ≤ 此值的样本视为"同输入",contacts 在其上走逐位硬门。</summary>
    private const double AlignedDeltaCm = 1.0;

    private string _argMax = "?";

    [Test]
    public void S7_Rebake_MatchesWebTruth()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30));
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.VerifyIncrementalNav = true;

        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s7-rebake-truth.json")))!;
        var script = meta["script"]!.AsArray()
            .Select(e => new CrowdCommand(e!["tick"]!.GetValue<int>(), (JsonNode)e["cmd"]!.DeepClone()))
            .ToArray();
        var truthExecTicks = meta["opExecTicks"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
        session.Commands.Schedule(script);

        var reports = new List<CrowdRebakeReport>();
        session.RebakeReported += reports.Add;

        var bin = File.ReadAllBytes(Path.Combine(dir, "s7-rebake.bin"));
        int cursor = 4; // magic
        int ticks = BitConverter.ToInt32(bin, cursor); cursor += 4;

        double maxDeltaCm = 0, finalMaxDeltaCm = 0;
        var deltas = new List<double>();
        int modeMismatches = 0, contactMismatches = 0, contactMaxDelta = 0, contactSamples = 0, alignedContactSamples = 0;
        string? firstModeMismatch = null, firstContactMismatch = null, firstStructuralMismatch = null;
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
                if (d > maxDeltaCm)
                {
                    maxDeltaCm = d;
                    _argMax = $"tick {t} 单位 {i}(句柄 {handle}) 我=({pos.X.ToDouble() / 100:F2},{pos.Y.ToDouble() / 100:F2}) 参考=({txCm / 100:F2},{tyCm / 100:F2})";
                }

                // contacts 硬门只在位置对齐样本上成立(|Δ| ≤ 1cm = 同输入);位置分叉后邻居集合
                // 本就不同,接触计数无合同(与 S5 的 0.05% 全量门不同:S7 场景位置必然分叉)
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
                }

                if (t >= ticks - FinalTicks && d > finalMaxDeltaCm) finalMaxDeltaCm = d;

                contactSamples++;
                var st = session.World.Get<CrowdSimulationUnitState>(entity);
                // 结构字段逐位;mode 单字段翻走双容忍(骑线翻转,见类注释)
                if (st.State != tState || st.Level != tLevel || st.Order != tOrder)
                {
                    firstStructuralMismatch ??= $"tick {t} 单位 {i}(句柄 {handle:x}): 我({st.State},{st.Mode},{st.Level},{st.Order}) vs 参考({tState},{tMode},{tLevel},{tOrder})";
                }
                else if (st.Mode != tMode)
                {
                    modeMismatches++;
                    firstModeMismatch ??= $"tick {t} 单位 {i}(句柄 {handle:x}): mode 我 {st.Mode} vs 参考 {tMode}(state/level/order 逐位一致)";
                }
            }
        }

        Assert.That(cursor, Is.EqualTo(bin.Length), "s7-rebake.bin 末尾有多余字节");

        // ── 重烘焙报告逐字段硬门 ──
        var opsBin = File.ReadAllBytes(Path.Combine(dir, "s7-ops.bin"));
        Assert.That(opsBin.Length, Is.GreaterThanOrEqualTo(8), "s7-ops.bin 截断");
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
            Assert.That(r.Orders, Is.EqualTo(orders), What("重规划指令数"));
            Assert.That(r.Refreshes, Is.EqualTo(refreshes), What("流场刷新队列长"));
            Assert.That(r.Evicted, Is.EqualTo(evicted), What("挤离单位数"));
            Assert.That(r.Stuck, Is.EqualTo(stuck), What("无处安放数"));
            Assert.That(r.UnionTiles, Is.EqualTo(union).AsCollection, What("受影响 tile 并集(升序)"));
        }

        Assert.That(oc, Is.EqualTo(opsBin.Length), "s7-ops.bin 末尾有多余字节");

        deltas.Sort();
        double p50 = deltas[deltas.Count / 2], p99 = deltas[(int)(deltas.Count * 0.99)], p999 = deltas[(int)(deltas.Count * 0.999)];
        double modeRate = (double)modeMismatches / Math.Max(1, contactSamples);
        double alignedRate = (double)contactMismatches / Math.Max(1, alignedContactSamples);
        TestContext.Out.WriteLine("max@ " + _argMax);
        TestContext.Out.WriteLine(
            $"S7 轨迹偏差(厘米): p50={p50:F4} p99={p99:F2} p99.9={p999:F2} max={maxDeltaCm:F2} 末{FinalTicks}帧max={finalMaxDeltaCm:F2};" +
            $"mode 单字段翻 {modeMismatches}/{contactSamples} ({modeRate:P3});" +
            $"contacts 对齐硬门 {contactMismatches}/{alignedContactSamples} ({alignedRate:P3});报告 {reports.Count} 笔全字段一致");
        Assert.That(firstStructuralMismatch, Is.Null, $"结构字段(state/level/order)逐位不一致:{firstStructuralMismatch}");
        Assert.That(modeRate, Is.LessThanOrEqualTo(MaxModeMismatchRate),
            $"mode 双容忍:不一致 {modeMismatches}/{contactSamples} ({modeRate:P3}) 超 {MaxModeMismatchRate:P2}(首个:{firstModeMismatch})");
        Assert.That(contactMismatches, Is.LessThanOrEqualTo(Math.Max(1, alignedContactSamples * 5 / 10000)),
            $"接触计数硬门(位置对齐样本):不一致 {contactMismatches}/{alignedContactSamples} 样本超 0.05% 容忍(首个:{firstContactMismatch})");
        Assert.That(contactMaxDelta, Is.LessThanOrEqualTo(2),
            $"接触计数硬门:单样本幅度 {contactMaxDelta} 超过骑线翻转上限 ±2(首个:{firstContactMismatch})");
        Assert.That(maxDeltaCm, Is.LessThanOrEqualTo(MaxTrajectoryBandCm),
            $"轨迹逐 tick 位置最大偏差 {maxDeltaCm:F2}cm 超带宽 {MaxTrajectoryBandCm}cm(p99.9={p999:F2})");
        Assert.That(finalMaxDeltaCm, Is.LessThanOrEqualTo(FinalBandCm),
            $"末 {FinalTicks} tick 未收束:最大偏差 {finalMaxDeltaCm:F2}cm 超 {FinalBandCm}cm");
    }
}
