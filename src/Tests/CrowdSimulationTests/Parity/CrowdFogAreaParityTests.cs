using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Fog;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 迷雾面命令格化对拍(L51):circle/poly 形状两端(沙盒正本导出器 areaCells 定点树 ↔
/// C# CrowdFogArea.AreaCells)逐格逐包围盒硬门。千米级圆/多边形在旧厘米域树里 r·r 静默
/// 回绕(Q31.32 厘米域平方溢出界 ~463 m),本门是格单位归一树的首批两端证据;rect1km 与
/// s7fog 的 reveal 同参,钉 rect 路径回归。真值 s7area-truth.json 由正本导出器生成。
/// 负向:形状坐标/半径超 ±46340 m 抛合同异常,不许静默回绕。
/// </summary>
public sealed class CrowdFogAreaParityTests
{
    [Test]
    public void AreaCells_MatchSandboxTruth_CellByCell()
    {
        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s7area-truth.json")))!;
        int f = meta["F"]!.GetValue<int>();
        int fcsCm = (int)(meta["fcsM"]!.GetValue<double>() * 100);
        foreach (var c in meta["cases"]!.AsArray())
        {
            string name = c!["name"]!.GetValue<string>();
            var value = (JsonArray)c["value"]!;
            CrowdFogShape shape = c["kind"]!.GetValue<string>() switch
            {
                "rect" => new CrowdFogShape { Rect = Cm(value) },
                "circle" => new CrowdFogShape { Circle = Cm(value) },
                _ => new CrowdFogShape { Poly = value.Select(p => Cm(p!.AsArray())).ToArray() },
            };
            var (cells, box) = CrowdFogArea.AreaCells(f, fcsCm, shape);
            Assert.That(cells, Is.EqualTo(c["cells"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray()).AsCollection,
                $"{name} 格集合与沙盒不一致");
            Assert.That(new[] { box.X0, box.Y0, box.X1, box.Y1 },
                Is.EqualTo(c["box"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray()).AsCollection,
                $"{name} 包围盒与沙盒不一致");
        }
    }

    [Test]
    public void AreaCells_OutOfSafeSquareDomain_Throws()
    {
        var bigCircle = new CrowdFogShape { Circle = new[] { 1000000.0, 1000000.0, 4640000.0 } }; // r = 46400 m
        Assert.Throws<InvalidOperationException>(
            () => CrowdFogArea.AreaCells(64, 25000, bigCircle), "半径超 ±46340 m 必须显式抛错");
        var farPoly = new CrowdFogShape { Poly = new[] { new[] { 0.0, 0.0 }, new[] { 5000000.0, 0.0 }, new[] { 0.0, 1000.0 } } };
        Assert.Throws<InvalidOperationException>(
            () => CrowdFogArea.AreaCells(64, 25000, farPoly), "顶点超 ±46340 m 必须显式抛错");
    }

    /// <summary>真值形状以米存储,C# 载荷是厘米:×100( authored 整数米,双精度精确往返)。</summary>
    private static double[] Cm(JsonArray a)
    {
        var r = new double[a.Count];
        for (int i = 0; i < a.Count; i++) r[i] = a[i]!.GetValue<double>() * 100.0;
        return r;
    }
}
