using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Fog;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 迷雾面命令格化对拍:circle/poly 逐格硬门(沙盒正本导出器 areaCells 定点树 ↔
/// C# CrowdFogArea.AreaCells),包围盒一并比对。平方溢出界约 463 m;rect1km 与
/// s7fog 的 reveal 同参;biteCirclePyth(300-400-500 勾股对齐)与
/// bitePolyGridEdge(顶点恰落格边界)是构造性咬合用例——双精度树各自给出不同格集,
/// 撤掉定点必红。
/// 负向:形状坐标/半径超 ±46340 m 抛合同异常,不许静默回绕。
/// </summary>
public sealed class CrowdFogAreaParityTests
{
    [Test]
    public void AreaCells_MatchSandboxTruth_CellByCell()
    {
        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s7area-truth.json")))!;
        int f = meta["fogCellsPerSide"]!.GetValue<int>();
        int fogCellSizeCm = meta["fogCellSizeCm"]!.GetValue<int>();
        foreach (var c in meta["cases"]!.AsArray())
        {
            string name = c!["name"]!.GetValue<string>();
            var value = (JsonArray)c["value"]!;
            CrowdFogShape shape = c["kind"]!.GetValue<string>() switch
            {
                "rect" => new CrowdFogShape { Rect = Numbers(value) },
                "circle" => new CrowdFogShape { Circle = Numbers(value) },
                _ => new CrowdFogShape { Poly = value.Select(p => Numbers(p!.AsArray())).ToArray() },
            };
            var (cells, box) = CrowdFogArea.AreaCells(f, fogCellSizeCm, shape);
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

    private static double[] Numbers(JsonArray a)
    {
        var r = new double[a.Count];
        for (int i = 0; i < a.Count; i++) r[i] = a[i]!.GetValue<double>();
        return r;
    }
}
