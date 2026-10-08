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
/// 位置逐 tick 逐单位 |Δ| ≤ MaxTrajectoryBandCm;末 30 tick 再收束 ≤ FinalBandCm。
/// 带值校准(2026-10-08,S5-b 避让开启口径,600 tick × 241 单位,seed 1337):
/// p50=0.07cm、p99=17.3cm、p99.9=35.1cm、max=136.1cm、末 30 tick max=35.1cm;
/// 状态机全程零不一致。避让开启后位置尾差是参考端 f32 存储在 11 km 坐标上的量化噪声
/// (f32 绝对分辨率 ~2cm)被推挤接触逐步放大,属浮点派生量带宽;结构级分歧(马达转向
/// 溢出曾达 22 m)照常以米-公里级偏差触警。避让关闭口径的旧带值 100/10 作废。
/// </summary>
public sealed class S5TrajectoryTruthTests
{
    /// <summary>逐 tick 轨迹位置带宽(厘米):实测 max 136.1cm,留约 3.7 倍余量。</summary>
    private const double MaxTrajectoryBandCm = 500.0;
    /// <summary>末态收束带(厘米):实测末 30 tick max 35.1cm,留约 2.9 倍余量。</summary>
    private const double FinalBandCm = 100.0;
    private const int FinalTicks = 30;

    [Test]
    public void S5_Trajectory_MatchesWebBaseline()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30));
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;

        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s5-trajectory-truth.json")))!;
        var script = meta["script"]!.AsArray()
            .Select(e => new CrowdCommand(e!["tick"]!.GetValue<int>(), (JsonNode)e["cmd"]!.DeepClone()))
            .ToArray();
        session.Commands.Schedule(script);

        var bin = File.ReadAllBytes(Path.Combine(dir, "s5-trajectory.bin"));
        int cursor = 4; // magic
        int ticks = BitConverter.ToInt32(bin, cursor); cursor += 4;

        double maxDeltaCm = 0, finalMaxDeltaCm = 0;
        var deltas = new List<double>();
        int stateMismatches = 0;
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

                var entity = session.Units.EntityAt(i);
                uint myHandle = session.Units.HandleAt(i);
                Assert.That(myHandle, Is.EqualTo(handle), $"tick {t} 单位 {i} 句柄错位");
                var pos = session.World.Get<Ludots.Core.Components.WorldPositionCm>(entity).Value;
                double d = Math.Max(Math.Abs(pos.X.ToDouble() - txCm), Math.Abs(pos.Y.ToDouble() - tyCm));
                deltas.Add(d);
                if (d > maxDeltaCm) maxDeltaCm = d;
                if (t >= ticks - FinalTicks && d > finalMaxDeltaCm) finalMaxDeltaCm = d;

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
        TestContext.Out.WriteLine(
            $"S5 轨迹偏差(厘米): p50={p50:F4} p99={p99:F2} p99.9={p999:F2} max={maxDeltaCm:F2} 末{FinalTicks}帧max={finalMaxDeltaCm:F2};状态机不一致 {stateMismatches} 处");
        Assert.That(stateMismatches, Is.EqualTo(0), $"状态机字段逐位不一致:{firstStateMismatch}");
        Assert.That(maxDeltaCm, Is.LessThanOrEqualTo(MaxTrajectoryBandCm),
            $"轨迹逐 tick 位置最大偏差 {maxDeltaCm:F2}cm 超带宽 {MaxTrajectoryBandCm}cm(p99.9={p999:F2})");
        Assert.That(finalMaxDeltaCm, Is.LessThanOrEqualTo(FinalBandCm),
            $"末 {FinalTicks} tick 未收束:最大偏差 {finalMaxDeltaCm:F2}cm 超 {FinalBandCm}cm");
    }
}
