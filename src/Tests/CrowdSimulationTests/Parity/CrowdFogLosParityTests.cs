using System;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// LOS 高度线恰等阈值咬合(L52):工程化高度/不透明表注入真实 CrowdFog.Sight(定点树),
/// 与沙盒正本导出器 sightFix 同表同例对拍。咬合例 V=15−2^-32 落在定点积(4.9999999981)
/// 与双精度积(4.9999999999…)之间——双精度实现判定可见、定点判定阻挡,撤掉定点必红;
/// 恰等例(V=15)钉"严格大于截断积"的阈值语义。眼高 10m、hAvg[to]=40m、rise=30、
/// 采样 s=2 格(行 10 列 11)、f=1/6。高度/不透明以 raw(格值×2^32)注入,两端逐位同表。
/// </summary>
public sealed class CrowdFogLosParityTests
{
    [Test]
    public void Sight_EngineeredHeights_MatchSandboxTruth()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        var fog = session.Fog!;

        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s7los-truth.json")))!;
        int f = meta["F"]!.GetValue<int>();
        Assert.That(f, Is.EqualTo(fog.F), "真值 fog 格数与会话不一致");
        int cx = meta["from"]![0]!.GetValue<int>(), cy = meta["from"]![1]!.GetValue<int>();
        int tx = meta["to"]![0]!.GetValue<int>(), ty = meta["to"]![1]!.GetValue<int>();
        long hAvgTRaw = meta["hAvgTRaw"]!.GetValue<long>();

        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var hAvgField = typeof(CrowdFog).GetField("_hAvg", flags)!;
        var hMaxField = typeof(CrowdFog).GetField("_hMax", flags)!;
        var opaqueField = typeof(CrowdFog).GetField("_opaque", flags)!;
        var sight = typeof(CrowdFog).GetMethod("Sight", flags)!;

        foreach (var c in meta["cases"]!.AsArray())
        {
            string name = c!["name"]!.GetValue<string>();
            var hAvg = new Fix64[f * f];
            var hMax = new Fix64[f * f];
            var opaque = new byte[f * f];
            hAvg[ty * f + tx] = Fix64.FromRaw(hAvgTRaw);
            foreach (var kv in c["hMaxRaw"]!.AsObject())
            {
                hMax[int.Parse(kv.Key)] = Fix64.FromRaw(kv.Value!.GetValue<long>());
            }

            int oc = c["opaque"]!.GetValue<int>();
            if (oc != 0) opaque[oc] = 1;
            hAvgField.SetValue(fog, hAvg);
            hMaxField.SetValue(fog, hMax);
            opaqueField.SetValue(fog, opaque);

            bool got = (bool)sight.Invoke(fog, new object[] { cx, cy, tx, ty })!;
            Assert.That(got, Is.EqualTo(c["expect"]!.GetValue<bool>()), $"{name} 视线判定与沙盒不一致");
        }
    }
}
