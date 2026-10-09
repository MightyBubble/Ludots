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
/// S7-c 认知槽回收复用对拍(脚本:11 轮「放置(未知)→ reveal 发现 → 拆除(残影)→ 再 reveal
/// 见地面」+ 重建 S1/S2/S3)。放置未见的实体即刻让认知 ≠ 真值(离真值槽开新槽),发现后回到
/// 真值槽,拆除留残影再开槽——每轮两槽,休眠槽超 slotCacheCapacity 后最旧先淘汰;重建命中
/// 已淘汰的认知集必须开新槽(容量回收复用)。淘汰次序 = 注册序(参考端 b.entry 是 JS Map
/// 插入序,槽号单调不复用 ⇒ 插入序即槽号升序)——次序错一步,重建命中的槽号与槽账就分叉,
/// 由迷雾 digest 的逐位硬门(每组槽号 + 全局 seq/entry/dormant)钉住。单位全程停出生点,
/// 状态机与 contacts 走零容忍口径;结构 op 报告逐字段硬门(S7 同族)。
/// </summary>
public sealed class S7FogRecycleTruthTests
{
    private const double MaxTrajectoryBandCm = 15000.0;
    private const double FinalBandCm = 5000.0;
    private const int FinalTicks = 30;
    private const double AlignedDeltaCm = 1.0;

    [Test]
    public void S7FogRecycle_MatchesWebTruth()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.VerifyIncrementalNav = true;

        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s7crecycle-truth.json")))!;
        var script = meta["script"]!.AsArray()
            .Select(e => new CrowdCommand(e!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(e!["cmd"]!)))
            .ToArray();
        var truthExecTicks = meta["opExecTicks"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
        session.Commands.Schedule(session, script);

        var reports = new List<Ludots.Core.CrowdSimulation.Structures.CrowdRebakeReport>();
        session.RebakeReported += reports.Add;
        var fog = session.Fog!;
        var belief = session.Belief!;

        var bin = File.ReadAllBytes(Path.Combine(dir, "s7crecycle.bin"));
        int cursor = 4; // magic LS7C
        int ticks = BitConverter.ToInt32(bin, cursor); cursor += 4;
        int groups = BitConverter.ToInt32(bin, cursor); cursor += 4;
        Assert.That(groups, Is.EqualTo(fog.G), "视野组数与真值不一致");

        double maxDeltaCm = 0;
        int structuralMismatches = 0, modeMismatches = 0, contactMismatches = 0, contactMaxDelta = 0, contactSamples = 0, alignedContactSamples = 0;
        string? firstMismatch = null, firstDigestMismatch = null;
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
                if (d > maxDeltaCm) maxDeltaCm = d;

                if (d <= AlignedDeltaCm)
                {
                    int contactDelta = Math.Abs(session.Movement!.Contacts[i] - tContacts);
                    if (contactDelta > 0)
                    {
                        contactMismatches++;
                        contactMaxDelta = Math.Max(contactMaxDelta, contactDelta);
                        firstMismatch ??= $"tick {t} 单位 {i}: contacts 我 {session.Movement.Contacts[i]} vs 参考 {tContacts}";
                    }

                    alignedContactSamples++;
                }

                contactSamples++;
                var st = session.World.Get<CrowdSimulationUnitState>(entity);
                if (st.State != tState || st.Level != tLevel || st.Order != tOrder || st.Mode != tMode)
                {
                    structuralMismatches++;
                    modeMismatches += st.Mode != tMode ? 1 : 0;
                    firstMismatch ??= $"tick {t} 单位 {i}(句柄 {handle:x}): 我({st.State},{st.Mode},{st.Level},{st.Order}) vs 参考({tState},{tMode},{tLevel},{tOrder})";
                }
            }

            // ── 迷雾 digest:逐位硬门(槽号/槽账钉住淘汰次序) ──
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

        Assert.That(cursor, Is.EqualTo(bin.Length), "s7crecycle.bin 末尾有多余字节");
        Assert.That(firstDigestMismatch, Is.Null, $"迷雾 digest 逐位不一致(首个):{firstDigestMismatch}");

        // ── 结构 op 报告逐字段硬门 ──
        var opsBin = File.ReadAllBytes(Path.Combine(dir, "s7crecycle-ops.bin"));
        Assert.That(opsBin.Length, Is.GreaterThanOrEqualTo(8), "s7crecycle-ops.bin 截断");
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

        Assert.That(oc, Is.EqualTo(opsBin.Length), "s7crecycle-ops.bin 末尾有多余字节");

        TestContext.Out.WriteLine(
            $"S7-c 认知槽回收:max偏差 {maxDeltaCm:F2}cm;状态机逐位 {structuralMismatches} 处不一致;" +
            $"contacts 对齐 {contactMismatches}/{alignedContactSamples};digest 全程逐位一致;报告 {reports.Count} 笔全字段一致;" +
            $"槽账终态 seq={belief.Seq} switches={belief.Switches} entry={belief.Entries.Count} dormant={belief.DormantList.Count}");
        Assert.That(structuralMismatches, Is.EqualTo(0), $"状态机逐位不一致(单位全程停驻,零容忍):{firstMismatch}");
        Assert.That(contactMaxDelta, Is.LessThanOrEqualTo(3), $"接触计数幅度 {contactMaxDelta} 超上限(首个:{firstMismatch})");
        Assert.That(maxDeltaCm, Is.LessThanOrEqualTo(MaxTrajectoryBandCm), "轨迹位置最大偏差超带宽(结构性兜底门)");
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
