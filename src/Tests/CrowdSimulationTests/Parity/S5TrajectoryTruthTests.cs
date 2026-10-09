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
/// S5 轨迹对拍(带宽口径——浮点派生量的家规,与 S3-c 同族):
/// 状态机字段(state/mode/level/order)逐 tick 逐单位逐位一致;
/// 位置逐 tick 逐单位分层门(L24)——p50/p99/p99.9 三档 + 沿航向有符号均值(系统性偏置
/// 探测:全体单位同向漂移时 max 抓不到、均值抓得到;航向基 = 参考端逐 tick 位移,静止单位
/// 无航向不进均值)+ max 带宽与末 30 tick 收束带(max/末态只拦卡墙/飞图级结构分歧)。
/// 带值校准(2026-10-08 F01 整改复测 + 2026-10-09 L24 分层复测,S5-b 避让开启口径,
/// 600 tick × 241 单位,seed 1337):p50=0.07cm、p99=17.3cm、p99.9=35.1cm、max=136.1cm、
/// 末 30 tick max=35.1cm;状态机全程零不一致。位置尾差定性(F01 三轮整改 L18 修订,
/// L21 记档再修订):首个分歧点 tick 11 单位 163 的马达中间量逐语句对照 + 逐接触对照
/// 证明——接触集/公式/分支逐句同构,输入差来自参考端 f32 状态存储(位置量化格 2^-10m;
/// 生成位置全精度,首次马达步即落格),经马达 spd≈687.5 因子放大翻转停车线(rest-stop)
/// 分支;去量化实验(参考端全部 f32 缓冲→f64、删显式 fround)未覆盖全部 f32 路径
/// (restX/Y、slot、blend、stall、jump*、motor.js 显式 fround),不收敛不构成"非 f32
/// 来源"的反证;Fix64 每运算舍入约 2^-32 cm,比观测差小约 10 个数量级,排除为主因——
/// 残余来源记档为参考端 f32 存储(2026-10-09 甲方裁决:行为对拍口径)。
/// 接触计数硬门(L19):不一致 10/144234 样本(0.007%,全部 ±1 骑线),容忍线 0.05% + 幅度
/// ±1;失败断言输出全量不一致清单。系统性分歧(接触集大面积错)会同时击穿两条。
/// </summary>
public sealed class S5TrajectoryTruthTests
{
    /// <summary>逐 tick 轨迹位置带宽(厘米):实测 max 136.1cm,留约 3.7 倍余量。</summary>
    private const double MaxTrajectoryBandCm = 500.0;
    /// <summary>末态收束带(厘米):实测末 30 tick max 35.1cm,留约 2.9 倍余量。</summary>
    private const double FinalBandCm = 100.0;
    private const int FinalTicks = 30;
    /// <summary>分层门(L24)p50(厘米):换数学固有误差 ≈ f32 格 0.1cm 档,实测 0.068,留约 7 倍。</summary>
    private const double P50BandCm = 0.5;
    /// <summary>分层门(L24)p99(厘米):实测 17.3,留约 2.9 倍。</summary>
    private const double P99BandCm = 50.0;
    /// <summary>分层门(L24)p99.9(厘米):实测 35.1,留约 2.9 倍。</summary>
    private const double P999BandCm = 100.0;
    /// <summary>沿航向有符号均值门(厘米,L24):全体单位同向系统性漂移的探测门,实测值见输出行。</summary>
    private const double HeadingMeanBandCm = 0.1;

    [Test]
    public void S5_Trajectory_MatchesWebBaseline()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true; // F02:旧真值在迷雾冻结口径下录制(参考端 S5 导出虽未置门,冻结与活雾在此场景行为等价——认知集恒真相)

        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s5-trajectory-truth.json")))!;
        var script = meta["script"]!.AsArray()
            .Select(e => new CrowdCommand(e!["tick"]!.GetValue<int>(), (JsonNode)e["cmd"]!.DeepClone()))
            .ToArray();
        session.Commands.Schedule(session, script);

        var bin = File.ReadAllBytes(Path.Combine(dir, "s5-trajectory.bin"));
        int cursor = 4; // magic
        int ticks = BitConverter.ToInt32(bin, cursor); cursor += 4;

        double maxDeltaCm = 0, finalMaxDeltaCm = 0;
        var deltas = new List<double>();
        // L24 沿航向有符号投影的累计:航向基 = 参考端逐 tick 位移,静止单位(位移 < 1e-6cm)无航向不进均值
        var lastTruth = new Dictionary<uint, (double X, double Y)>();
        double headingProjSum = 0;
        int headingSamples = 0;
        int stateMismatches = 0;
        int contactMismatches = 0;
        int contactMaxDelta = 0;
        var contactSamples_ = new List<string>();
        int contactSamples = 0;
        string? firstStateMismatch = null;
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
                // L12-④ 硬门:接触计数整数可比,不依赖位置带宽。两侧算术体系(Fix64 与参考端
                // f32/f64 混合)在"马达停车线/接触边界"的不连续点上有 1e-4 量级骑线翻转(首个:
                // tick 15),实测翻转率 0.007%、幅度全部 ±1;系统性求解分歧会同时击穿两条容忍线。
                int contactDelta = Math.Abs(session.Movement!.Contacts[i] - tContacts);
                if (contactDelta > 0)
                {
                    contactMismatches++;
                    contactMaxDelta = Math.Max(contactMaxDelta, contactDelta);
                    // L19:全量清单(不只首个),失败断言整体列出
                    contactSamples_.Add($"tick {t} 单位 {i}: 我 {session.Movement.Contacts[i]} vs 参考 {tContacts}");
                }
                uint myHandle = session.Units.HandleAt(i);
                Assert.That(myHandle, Is.EqualTo(handle), $"tick {t} 单位 {i} 句柄错位");
                var pos = session.World.Get<Ludots.Core.Components.WorldPositionCm>(entity).Value;
                double d = Math.Max(Math.Abs(pos.X.ToDouble() - txCm), Math.Abs(pos.Y.ToDouble() - tyCm));
                deltas.Add(d);
                if (d > maxDeltaCm) maxDeltaCm = d;
                if (t >= ticks - FinalTicks && d > finalMaxDeltaCm) finalMaxDeltaCm = d;
                if (lastTruth.TryGetValue(handle, out var prev))
                {
                    double hx = txCm - prev.X, hy = tyCm - prev.Y;
                    double hl = Math.Sqrt(hx * hx + hy * hy);
                    if (hl > 1e-6)
                    {
                        headingProjSum += ((pos.X.ToDouble() - txCm) * hx + (pos.Y.ToDouble() - tyCm) * hy) / hl;
                        headingSamples++;
                    }
                }
                lastTruth[handle] = (txCm, tyCm);

                contactSamples++;
                var st = session.World.Get<CrowdSimulationUnitState>(entity);
                if (st.State != tState || st.Mode != tMode || st.Level != tLevel || st.Order != tOrder)
                {
                    stateMismatches++;
                    firstStateMismatch ??= $"tick {t} 单位 {i}(句柄 {handle:x}): 我({st.State},{st.Mode},{st.Level},{st.Order}) vs 参考({tState},{tMode},{tLevel},{tOrder})";
                }
            }
        }

        deltas.Sort();
        double p999 = deltas[(int)(deltas.Count * 0.999)];
        double p99 = deltas[(int)(deltas.Count * 0.99)];
        double p50 = deltas[deltas.Count / 2];
        // L19:接触计数容忍线的证据链(F01 三轮整改 L18,L21 记档修订)——残余差定性为参考端
        // f32 状态存储(逐接触对照:同集合同公式,dx/dy 差 = f32 量化格 2^-10m;去量化实验未
        // 覆盖全部 f32 路径,不构成反证;Fix64 每运算舍入 2^-32 cm 比观测差小约 10 个数量级、
        // 排除为主因),经马达 spd 因子放大翻转停车线等离散分支;逐位相等在现数值系下不可达,
        // 故保留双容忍线,系统性分歧(接触集大面积错)仍会同时击穿两条。
        string contactList = contactSamples_.Count > 8
            ? string.Join("; ", contactSamples_.Take(8)) + $" …共 {contactSamples_.Count} 条"
            : string.Join("; ", contactSamples_);
        double headingMean = headingSamples == 0 ? 0 : headingProjSum / headingSamples;
        TestContext.Out.WriteLine(
            $"S5 轨迹偏差(厘米): p50={p50:F4} p99={p99:F2} p99.9={p999:F2} max={maxDeltaCm:F2} 末{FinalTicks}帧max={finalMaxDeltaCm:F2}" +
            $" 沿航向均值={headingMean:F4}({headingSamples} 样本);状态机不一致 {stateMismatches} 处");
        Assert.That(stateMismatches, Is.EqualTo(0), $"状态机字段逐位不一致:{firstStateMismatch}");
        Assert.That(contactMismatches, Is.LessThanOrEqualTo(contactSamples * 5 / 10000),
            $"接触计数硬门:不一致 {contactMismatches}/{contactSamples} 样本超 0.05% 容忍(全清单:{contactList})");
        Assert.That(contactMaxDelta, Is.LessThanOrEqualTo(1),
            $"接触计数硬门:单样本幅度 {contactMaxDelta} 超过骑线翻转上限 ±1(全清单:{contactList})");
        Assert.That(p50, Is.LessThanOrEqualTo(P50BandCm),
            $"分层门(L24):p50={p50:F4}cm 超 {P50BandCm}cm(换数学固有误差档)");
        Assert.That(p99, Is.LessThanOrEqualTo(P99BandCm),
            $"分层门(L24):p99={p99:F2}cm 超 {P99BandCm}cm");
        Assert.That(p999, Is.LessThanOrEqualTo(P999BandCm),
            $"分层门(L24):p99.9={p999:F2}cm 超 {P999BandCm}cm");
        Assert.That(Math.Abs(headingMean), Is.LessThanOrEqualTo(HeadingMeanBandCm),
            $"沿航向有符号均值 |{headingMean:F4}|cm 超 {HeadingMeanBandCm}cm——全体单位同向系统性漂移");
        Assert.That(maxDeltaCm, Is.LessThanOrEqualTo(MaxTrajectoryBandCm),
            $"轨迹逐 tick 位置最大偏差 {maxDeltaCm:F2}cm 超带宽 {MaxTrajectoryBandCm}cm(p99.9={p999:F2})");
        Assert.That(finalMaxDeltaCm, Is.LessThanOrEqualTo(FinalBandCm),
            $"末 {FinalTicks} tick 未收束:最大偏差 {finalMaxDeltaCm:F2}cm 超 {FinalBandCm}cm");
    }
}
