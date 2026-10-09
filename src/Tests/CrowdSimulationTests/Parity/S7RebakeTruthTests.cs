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
/// 逐 tick 真值沿用 S5 口径——位置分层门(p50 + max 带宽 + 末态收束)+ contacts 双容忍线
/// (0.05% + ±2 骑线;未收紧到 S5 的 ±1——实测唯一不一致样本 36989 个位置对齐样本
/// 中的 1 个,幅度 ≤2,判为同 tick 两邻居各翻一次的骑线叠合,收紧即假门);状态机拆两档:state / level / order 逐位(结构字段,
/// 零容忍),mode 走双容忍(0.15% + 仅 mode 单字段翻,见下)。
/// 行为不变量门:位置带 150m 拦不住"绕错路"级分歧,补五条行为门——
/// ① 每单位终态(state/level/order)逐位;② 首达 tick 分布直方图(从未到达计独立桶)两侧
/// 相同;③ 阻挡格停留数为零(逐 tick 检查两侧单位位置不在阻挡格;放置 op 当帧末态豁免——
/// 挤离在重烘阶段 1 = 放置后下一 tick 执行,evicted/stuck 已被 ops 报告硬门钉住;跳跃中
/// 单位离网直线可越阻挡、上层 Level≠0 走桥面语义,不查地面阻挡格;参考端位置查同一份
/// 栅格,两侧 Blocked 逐格一致由 ops 报告的结构组件表硬门保证);④ 终态 Unreachable 的
/// 单位集合(不可达集合)逐位一致;⑤ 重规划次数由 ops 报告 orders 字段的逐字段硬门覆盖
/// (见下,不另设门)。位置带降为结构性兜底:只拦整组卡墙/飞图级分歧。
/// mode 容忍的定位依据(墙面重规划场景,360 tick × 120 单位):
/// 重规划落帧时阵型槽位从已漂移位置重排(领队路径与成员位置两侧一致,槽位差 ≤0.2m),
/// 单位的槽位视线判定(mode = LOS + 射程 + 路线比)在这些骑线上翻转——
/// 实测 13/43200 = 0.030%,全部为 mode 单字段翻(state/level/order 全程逐位一致);
/// 与 S5 首个分歧(tick 11 马达停车线骑线)同源:双算术体系在离散判定点翻转离散事件。
/// 每次结构 op 的重烘焙报告逐字段硬门(执行 tick / 类别 / 报告 tick / tiles / contexts /
/// costOnly / hits / misses / orders / refreshes / evicted / stuck / 受影响 tile 并集升序)。
/// 口径:参考端以 __S7_TRUTH_NAV__ 镜像补丁冻结迷雾(S7-a 内核无迷雾,F02 另单),
/// 组全程停在 truth 导航,队伍反应 = reactRebake 真值直反应;DB-03 增量 vs 全量一致性
/// 检查(VerifyIncrementalNav)在每次 op 后随路运行。
/// </summary>
public sealed class S7RebakeTruthTests
{
    /// <summary>逐 tick 轨迹位置带宽(厘米):行为门落地后仅作结构性兜底——只拦整组卡墙/飞图级
    /// 分歧,路线级分歧由行为门拦。实测 max 2407.2cm、p99.9 1701.3cm、末 30 tick 1258.8cm
    /// (重规划落帧的槽位视线骑线翻——少数单位跟槽位或流场,路径分叉,量级 = 米级绕行差),
    /// 带值维持结构性兜底档不收紧。</summary>
    private const double MaxTrajectoryBandCm = 15000.0;
    /// <summary>末态收束带(厘米):实测末 30 tick max 1258.8cm,带值维持结构性兜底档不收紧。</summary>
    private const double FinalBandCm = 3000.0;
    private const int FinalTicks = 30;
    /// <summary>分层门 p50(厘米):实测 0.069,与 S5(0.068)同固有误差档,同带 0.5。
    /// 放置/重烘当帧的墙触推离两侧必须同值——开放格缓存按版本失效是其中的隐含合同
    /// (CrowdWalls.OpenCellCache:重烘对 Walk 原地腐蚀,失效只认引用会读到旧开放表,
    /// 放置当帧马达跳过亚格推出,p50 抬到 8cm 档)。</summary>
    private const double P50BandCm = 0.5;
    /// <summary>mode 双容忍:不一致率上限(实测 0.030%,留 5 倍余量)。</summary>
    private const double MaxModeMismatchRate = 0.0015;
    /// <summary>位置对齐判定(厘米):|Δ| ≤ 此值的样本视为"同输入",contacts 在其上走逐位硬门。</summary>
    private const double AlignedDeltaCm = 1.0;

    private string _argMax = "?";

    [Test]
    public void S7_Rebake_MatchesWebTruth()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true; // F02:S7 真值在 __S7_TRUTH_NAV__ 冻结口径下录制,同构置位
        session.VerifyIncrementalNav = true;

        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s7-rebake-truth.json")))!;
        var script = meta["script"]!.AsArray()
            .Select(e => new CrowdCommand(e!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(e!["cmd"]!)))
            .ToArray();
        var truthExecTicks = meta["opExecTicks"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
        session.Commands.Schedule(session, script);

        var reports = new List<CrowdRebakeReport>();
        session.RebakeReported += reports.Add;

        // 阻挡格停留门的豁免集:放置 op 落格当帧,仓内单位要等下一 tick 的重烘
        // 阶段 1 才被挤离(evicted/stuck 由 ops 报告硬门钉住)
        var placeExecTicks = script
            .Where(c => c.Cmd.Kind == CrowdSimCommandKind.PlaceStructure)
            .Select(c => c.Tick).ToHashSet();
        var store = session.Structures!;
        int gridN = store.CellCount, cellSize = store.CellSizeCm;
        byte[] blockedGrid = store.Blocked;

        var bin = File.ReadAllBytes(Path.Combine(dir, "s7-rebake.bin"));
        int cursor = 4; // magic
        int ticks = BitConverter.ToInt32(bin, cursor); cursor += 4;

        double maxDeltaCm = 0, finalMaxDeltaCm = 0;
        var deltas = new List<double>();
        // 行为门记账:终态取每句柄最后一次出现;首达 tick = 首次进 Arrived(可再唤醒,
        // 到达语义只记首次)
        var myFinal = new Dictionary<uint, (byte State, byte Level, uint Order)>();
        var truthFinal = new Dictionary<uint, (byte State, byte Level, uint Order)>();
        var myArrived = new Dictionary<uint, int>();
        var truthArrived = new Dictionary<uint, int>();
        int dwellMine = 0, dwellTruth = 0;
        string? firstDwellMine = null, firstDwellTruth = null;
        int modeMismatches = 0, contactMismatches = 0, contactMaxDelta = 0, contactSamples = 0, alignedContactSamples = 0;
        string? firstModeMismatch = null, firstContactMismatch = null, firstStructuralMismatch = null;
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

                myFinal[handle] = (st.State, st.Level, st.Order);
                if (st.State == (byte)CrowdUnitState.Arrived) myArrived.TryAdd(handle, t);
                truthFinal[handle] = (tState, tLevel, tOrder);
                if (tState == (byte)CrowdUnitState.Arrived) truthArrived.TryAdd(handle, t);

                // 阻挡格停留:放置当帧豁免(见 placeExecTicks);跳跃中单位离网直线
                // 可越阻挡、上层 Level≠0 走桥面语义,不查地面阻挡格;参考端位置查同一份栅格
                // (两侧 Blocked 逐格一致由 ops 报告的结构组件表硬门保证)
                if (!placeExecTicks.Contains(t))
                {
                    int cx = (int)(pos.X.ToDouble() / cellSize), cy = (int)(pos.Y.ToDouble() / cellSize);
                    if (st.State != (byte)CrowdUnitState.Jump && st.Level == 0 &&
                        cx >= 0 && cy >= 0 && cx < gridN && cy < gridN && blockedGrid[cy * gridN + cx] != 0)
                    {
                        dwellMine++;
                        firstDwellMine ??= $"tick {t} 单位 {i}(句柄 {handle:x}): 我方 ({pos.X.ToDouble() / 100:F2},{pos.Y.ToDouble() / 100:F2})m 在阻挡格 ({cx},{cy})";
                    }

                    int tcx = (int)(txCm / cellSize), tcy = (int)(tyCm / cellSize);
                    if (tState != (byte)CrowdUnitState.Jump && tLevel == 0 &&
                        tcx >= 0 && tcy >= 0 && tcx < gridN && tcy < gridN && blockedGrid[tcy * gridN + tcx] != 0)
                    {
                        dwellTruth++;
                        firstDwellTruth ??= $"tick {t} 单位 {i}(句柄 {handle:x}): 参考端 ({txCm / 100:F2},{tyCm / 100:F2})m 在阻挡格 ({tcx},{tcy})";
                    }
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
            Assert.That(r.Orders, Is.EqualTo(orders), What("重规划指令数")); // 重规划次数:本行即门
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

        // ── 行为门(数据全部来自 s7-rebake.bin 逐 tick 记录与运行侧同构记账) ──
        // ① 每单位终态(state/level/order)逐位
        var finalMismatches = new List<string>();
        foreach (uint h in myFinal.Keys.Concat(truthFinal.Keys).Distinct())
        {
            bool hasM = myFinal.TryGetValue(h, out var m);
            bool hasR = truthFinal.TryGetValue(h, out var r);
            if (!hasM || !hasR || m != r)
                finalMismatches.Add($"句柄 {h:x}: 我 {(hasM ? m.ToString() : "无此单位")} vs 参考 {(hasR ? r.ToString() : "无此单位")}");
        }

        // ② 首达 tick 分布直方图(-1 桶 = 从未到达),两侧相同
        var myHist = myArrived.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        var truthHist = truthArrived.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        myHist[-1] = myFinal.Count - myArrived.Count;
        truthHist[-1] = truthFinal.Count - truthArrived.Count;
        var histDiff = new List<string>();
        foreach (int k in myHist.Keys.Concat(truthHist.Keys).Distinct())
        {
            myHist.TryGetValue(k, out int m);
            truthHist.TryGetValue(k, out int r);
            if (m != r) histDiff.Add($"到达 tick {k}: 我 {m} vs 参考 {r}");
        }

        // ④ 不可达集合 = 终态 Unreachable 的单位,逐位一致
        var myUnreachable = myFinal.Where(kv => kv.Value.State == (byte)CrowdUnitState.Unreachable).Select(kv => kv.Key).ToHashSet();
        var truthUnreachable = truthFinal.Where(kv => kv.Value.State == (byte)CrowdUnitState.Unreachable).Select(kv => kv.Key).ToHashSet();
        var unreachDiff = myUnreachable.Concat(truthUnreachable).Where(h => myUnreachable.Contains(h) ^ truthUnreachable.Contains(h)).ToList();

        TestContext.Out.WriteLine("max@ " + _argMax);
        TestContext.Out.WriteLine(
            $"S7 轨迹偏差(厘米): p50={p50:F4} p99={p99:F2} p99.9={p999:F2} max={maxDeltaCm:F2} 末{FinalTicks}帧max={finalMaxDeltaCm:F2};" +
            $"mode 单字段翻 {modeMismatches}/{contactSamples} ({modeRate:P3});" +
            $"contacts 对齐硬门 {contactMismatches}/{alignedContactSamples} ({alignedRate:P3});报告 {reports.Count} 笔全字段一致;" +
            $"行为门:首达 {myArrived.Count}/{myFinal.Count} 直方图差异 {histDiff.Count} 桶;阻挡格停留 我{dwellMine}/参考{dwellTruth};" +
            $"不可达 我{myUnreachable.Count}/参考{truthUnreachable.Count}");
        Assert.That(firstStructuralMismatch, Is.Null, $"结构字段(state/level/order)逐位不一致:{firstStructuralMismatch}");
        Assert.That(modeRate, Is.LessThanOrEqualTo(MaxModeMismatchRate),
            $"mode 双容忍:不一致 {modeMismatches}/{contactSamples} ({modeRate:P3}) 超 {MaxModeMismatchRate:P2}(首个:{firstModeMismatch})");
        Assert.That(contactMismatches, Is.LessThanOrEqualTo(Math.Max(1, alignedContactSamples * 5 / 10000)),
            $"接触计数硬门(位置对齐样本):不一致 {contactMismatches}/{alignedContactSamples} 样本超 0.05% 容忍(首个:{firstContactMismatch})");
        Assert.That(contactMaxDelta, Is.LessThanOrEqualTo(2),
            $"接触计数硬门:单样本幅度 {contactMaxDelta} 超过骑线翻转上限 ±2(首个:{firstContactMismatch})");
        Assert.That(finalMismatches, Is.Empty,
            $"L22 行为门①:单位终态(state/level/order)不一致:{string.Join("; ", finalMismatches.Take(4))}");
        Assert.That(histDiff, Is.Empty,
            $"L22 行为门②:首达 tick 分布不一致(含从未到达桶):{string.Join("; ", histDiff.Take(4))}");
        Assert.That(dwellMine + dwellTruth, Is.EqualTo(0),
            $"L22 行为门③:阻挡格停留非零 我 {dwellMine} / 参考 {dwellTruth}(首个 我:{firstDwellMine} 参考:{firstDwellTruth})");
        Assert.That(unreachDiff, Is.Empty,
            $"L22 行为门④:不可达集合不一致(终态 Unreachable):{string.Join(", ", unreachDiff.Take(8).Select(h => $"{h:x}"))}");
        Assert.That(p50, Is.LessThanOrEqualTo(P50BandCm),
            $"分层门(L24):p50={p50:F4}cm 超 {P50BandCm}cm");
        Assert.That(maxDeltaCm, Is.LessThanOrEqualTo(MaxTrajectoryBandCm),
            $"轨迹逐 tick 位置最大偏差 {maxDeltaCm:F2}cm 超带宽 {MaxTrajectoryBandCm}cm(结构性兜底门,p99.9={p999:F2})");
        Assert.That(finalMaxDeltaCm, Is.LessThanOrEqualTo(FinalBandCm),
            $"末 {FinalTicks} tick 未收束:最大偏差 {finalMaxDeltaCm:F2}cm 超 {FinalBandCm}cm");
    }
}
