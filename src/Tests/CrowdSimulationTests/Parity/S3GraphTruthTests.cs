using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S3-b 对拍:拼装 NavMesh(跨 tile 邻接与边界 portal)与 HPA* 抽象图的拓扑逐位一致;
/// 代价数组(浮点派生量)按带宽比较并报告最大分歧——两套数学体系的最后位差异不是缺陷。
/// </summary>
[TestFixture]
public class S3GraphTruthTests
{
    public static readonly string[] Seeds = { "s1337", "s2024", "s7" };

    [Test]
    public void AssembleAndHpa_MatchWebTruth([ValueSource(nameof(Seeds))] string seed)
    {
        var runtime = S1SurfaceTruthTests.LoadRuntime(seed);
        var surface = S1SurfaceTruthTests.ReadSurface(seed);
        var mapSurface = S1SurfaceTruthTests.LoadMapSurface(seed);
        var grid = SurfaceGrid.Build(runtime, surface, mapSurface.Blockers);
        var heightAsset = ContinuousHeightmapBinary.Read(File.OpenRead(
            Path.Combine(S1Dir(seed), "terrain", $"crowd_simulation_{seed}.height")));
        var heights = NavHeightField.FromHeightmap(heightAsset, runtime.NavCellCount, runtime.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtime);

        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, runtime.Hpa.ClusterSize,
            runtime.Navmesh.MinRegionArea.ToDouble(), runtime.Navmesh.MaxSimplificationError.ToDouble(),
            runtime.Navmesh.MaxEdgeLen.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);

        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine(S1Dir(seed), "CrowdSimulation", "parity", "s3b-graph-truth.json")))!;
        var truthContexts = truth["contexts"]!.AsArray();
        var costStream = File.ReadAllBytes(Path.Combine(S1Dir(seed), "CrowdSimulation", "parity", "s3b-costs.bin"));

        var topology = new List<byte>();
        topology.AddRange(Encoding.ASCII.GetBytes("LS3B"));
        WriteI32(topology, runtime.NavCellCount);
        // 上下文数 = 去重后的导航上下文数(写入在循环前无法预知,先占位后补)
        topology.AddRange(new byte[4]);
        double maxCostDiff = 0;
        int costCursor = 0;
        var seen = new HashSet<int>();
        int ctxIndex = 0;
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            foreach (var clearance in runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtime, grid, heights, deck, a, clearance, cache);
                if (!seen.Add(nav.Id)) continue;

                var mesh = NavMeshAssembler.Assemble(nav.Tiles!, nav.Cost, nav.CellCount, runtime.Hpa.ClusterSize);
                var hpa = HpaGraph.Build(nav, runtime);
                var flat = hpa.Flatten();

                // 拓扑入流(与导出器 LS3B 同序)
                WriteI32(topology, nav.Id);
                WriteI32(topology, mesh.Count); WriteI32(topology, mesh.RegionCount);
                WriteI32s(topology, mesh.PolyStart); WriteI32s(topology, mesh.PolyVerts); WriteI32s(topology, mesh.PolyTile);
                WriteI32s(topology, mesh.NeiStart); WriteI32s(topology, mesh.Nei);
                WriteI32s(topology, mesh.PolyOf);
                WriteI32s(topology, mesh.Vx); WriteI32s(topology, mesh.Vy);
                WriteI32s(topology, mesh.PaX); WriteI32s(topology, mesh.PaY);
                WriteI32s(topology, mesh.PbX); WriteI32s(topology, mesh.PbY);
                WriteI32(topology, flat.NodeCell.Length); WriteI32(topology, hpa.EdgeCount);
                WriteI32s(topology, flat.NodeCell); WriteU8s(topology, flat.NodeLayer); WriteI32s(topology, flat.NodeCluster);
                WriteI32s(topology, flat.AdjStart); WriteI32s(topology, flat.AdjTo);

                // 代价带宽比较(先 polyCost 后 adjCost,与导出端 writeCosts 同序,各带长度前缀)
                maxCostDiff = Math.Max(maxCostDiff, CompareCostSection(mesh.PolyCost.Select(v => v.ToDouble()).ToArray(), costStream, ref costCursor));
                maxCostDiff = Math.Max(maxCostDiff, CompareCostSection(flat.AdjCost.Select(v => v.ToDouble()).ToArray(), costStream, ref costCursor));

                var t = truthContexts[ctxIndex]!;
                Assert.That(mesh.Count, Is.EqualTo(t["polys"]!.GetValue<int>()), $"{t["navId"]} 多边形数");
                Assert.That(flat.NodeCell.Length, Is.EqualTo(t["hpaNodes"]!.GetValue<int>()), $"{t["navId"]} HPA 节点数");
                Assert.That(hpa.EdgeCount, Is.EqualTo(t["hpaEdges"]!.GetValue<int>()), $"{t["navId"]} HPA 边数");
                ctxIndex++;
            }
        }

        // 先补写占位的上下文数,再算哈希(顺序反了就会拿着未回填的流去对拍)
        var ccBytes = BitConverter.GetBytes(ctxIndex);
        for (int i = 0; i < 4; i++) topology[8 + i] = ccBytes[i];
        var hash = S2NavTruthTests.Fnv(topology.ToArray());
        var expected = truth["fnv1a"]!.GetValue<string>();
        if (hash != expected)
        {
            string dumpDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s3b-diff", seed);
            Directory.CreateDirectory(dumpDir);
            File.WriteAllBytes(Path.Combine(dumpDir, "topology.bin"), topology.ToArray());
        }

        Assert.That(hash, Is.EqualTo(expected), $"S3-b 拓扑与 Web 不一致(seed {seed});代价最大分歧 {maxCostDiff:E2}");
        // 代价带宽:Fix64 与 f32/f64 两条算路的最后位差;超过 1e-3 视为真分歧
        Assert.That(maxCostDiff, Is.LessThan(1e-3), $"S3-b 代价分歧超带宽(seed {seed}): {maxCostDiff:E2}");
        TestContext.Out.WriteLine($"seed {seed}: 拓扑一致,代价最大分歧 {maxCostDiff:E2}");
    }

    private static double CompareCosts(IEnumerable<double> mine, byte[] truth, ref int cursor)
    {
        double maxDiff = 0;
        foreach (var v in mine)
        {
            double w = BitConverter.ToDouble(truth, cursor);
            cursor += 8;
            maxDiff = Math.Max(maxDiff, Math.Abs(v - w));
        }

        return maxDiff;
    }

    /// <summary>导出端每个代价段带 i32 长度前缀,读之前先跳过。</summary>
    private static double CompareCostSection(IReadOnlyList<double> mine, byte[] truth, ref int cursor)
    {
        int declared = BitConverter.ToInt32(truth, cursor);
        cursor += 4;
        if (declared != mine.Count)
        {
            throw new InvalidOperationException($"代价段长度不一致: {mine.Count} ≠ {declared}");
        }

        return CompareCosts(mine, truth, ref cursor);
    }

    private static void WriteI32(List<byte> buffer, int v) => buffer.AddRange(BitConverter.GetBytes(v));
    private static void WriteI32s(List<byte> buffer, int[] arr) { WriteI32(buffer, arr.Length); foreach (var v in arr) WriteI32(buffer, v); }
    private static void WriteU8s(List<byte> buffer, byte[] arr) { WriteI32(buffer, arr.Length); buffer.AddRange(arr); }

    private static string S1Dir(string seed) => Path.Combine("assets", "s1", seed);
}
