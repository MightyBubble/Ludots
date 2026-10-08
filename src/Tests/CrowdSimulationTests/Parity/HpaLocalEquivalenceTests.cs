using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Mathematics.FixedPoint;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// hpaLocal 等价基准:簇内全对快路径(PrepareLocal 邻接表 + LocalDijkstra,烘焙与增量共用)
/// 与逐格推导的 ClusterDijkstra(HpaQuery 查询路径)在真实地图导航上下文上逐位一致——
/// 对全部 cluster 的全部簇内边(i, j, d):ClusterDijkstra(cells[i]) 到 cells[j] 的距离必须
/// 与 d 逐位相等;地面节点对里不在 Intra 表的,必须不可达(完备性)。
/// </summary>
[TestFixture]
public class HpaLocalEquivalenceTests
{
    [Test]
    public void LocalFastPath_MatchesClusterDijkstraBitwise()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        var nav = session.Navs[runtime.Profiles[0].NavContextId];
        var hpa = nav.Hpa!;
        int s = hpa.ClusterSize, n = nav.CellCount;
        var dist = new Fix64[s * s];
        var heap = new NavMinHeap(512);

        int edges = 0, clusters = 0;
        for (int cl = 0; cl < hpa.Blocks.Length; cl++)
        {
            var cells = hpa.Cells[cl];
            int groundCount = 0;
            while (groundCount < cells.Length && cells[groundCount] < nav.CellCount * nav.CellCount) groundCount++;
            if (groundCount == 0) continue;
            clusters++;

            // 同簇节点对的全对距离:快路径的 Intra 表 vs 查询路径的 ClusterDijkstra。
            // Intra 只存列表序 i<j 的对(参考端同形);完备性也只对 j > i 要求。
            for (int i = 0; i < groundCount; i++)
            {
                HpaQuery.ClusterDijkstra(nav, cl, cells[i], dist, heap);
                var row = new HashSet<int>();
                for (int k = 0; k < hpa.Intra[cl].Length; k += 2)
                {
                    int a = hpa.Intra[cl][k], b = hpa.Intra[cl][k + 1];
                    if (a != i || b >= groundCount) continue;
                    var expected = hpa.IntraDist[cl][k / 2];
                    var actual = dist[NavGridSteps.LocalIndex(cells[b], n, s)];
                    Assert.That(actual, Is.EqualTo(expected),
                        $"cluster {cl} 节点对 ({cells[i]},{cells[b]}): 快路径 {expected} ≠ ClusterDijkstra {actual}");
                    row.Add(b);
                    edges++;
                }

                // 完备性:列表序在本节点之后的同簇地面节点,可达者必须已在 Intra 表的该行里
                for (int j = i + 1; j < groundCount; j++)
                {
                    bool isReachable = dist[NavGridSteps.LocalIndex(cells[j], n, s)] < Fix64.MaxValue / 4;
                    Assert.That(row.Contains(j) || !isReachable,
                        $"cluster {cl} 节点对 ({cells[i]},{cells[j]}) 可达但不在 Intra 表");
                }
            }
        }

        Assert.That(edges, Is.GreaterThan(1000), $"簇内边采样不足:{edges}");
        Assert.That(clusters, Is.GreaterThan(100), $"有地面节点的簇数不足:{clusters}");
    }
}
