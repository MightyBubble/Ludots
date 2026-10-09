using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// HPA* 抽象图寻路(hpa.js findPath 移植):起点簇内 Dijkstra 到本簇全部入口,
/// 抽象图 A*(块内边 + 跳跃链接边),进入终点簇的节点再接终点簇内 Dijkstra 收尾。
/// 返回 cluster 走廊 + 途经的入口格;代价为零成本启发 × minCost(octile)。
/// </summary>
public static class HpaQuery
{
    private const int GoalKey = -2;

    /// <summary>查询暂存(RT-03:每 worker 一份;与烘焙用的图内暂存互不相干)。</summary>
    public sealed class Scratch
    {
        public Scratch(int clusterCells)
        {
            StartDist = new Fix64[clusterCells];
            GoalDist = new Fix64[clusterCells];
        }

        public Fix64[] StartDist { get; }
        public Fix64[] GoalDist { get; }
        public NavMinHeap Heap { get; } = new(512);
        public Dictionary<int, Fix64> G { get; } = new();
        public Dictionary<int, int> Parent { get; } = new();
        public HashSet<int> Closed { get; } = new();
    }

    public sealed class Result
    {
        public required int[] Clusters { get; init; }
        public required int[] Cells { get; init; }
        public required Fix64 Cost { get; init; }
        public required int Expanded { get; init; }
    }

    /// <summary>簇内 8 连通 Dijkstra(clusterDijkstra 移植),dist 按簇内局部下标填满。</summary>
    public static void ClusterDijkstra(NavContext nav, int cl, int startCell, Fix64[] dist, NavMinHeap heap)
    {
        var hpa = nav.Hpa!;
        int s = hpa.ClusterSize, c = hpa.ClustersPerSide, n = nav.CellCount;
        int x0 = cl % c * s, y0 = cl / c * s;
        int x1 = Math.Min(n, x0 + s), y1 = Math.Min(n, y0 + s);
        Array.Fill(dist, Fix64.MaxValue);
        heap.Clear();
        dist[NavGridSteps.LocalIndex(startCell, n, s)] = Fix64.Zero;
        heap.Push(Fix64.Zero, startCell);
        while (heap.Size > 0)
        {
            var k = heap.PeekKey();
            int u = heap.Pop();
            int ux = u % n, uy = u / n;
            if (k > dist[(uy - y0) * s + (ux - x0)]) continue;
            for (int d = 0; d < 8; d++)
            {
                if (!NavGridSteps.CanStep(nav.Passable, n, ux, uy, d)) continue;
                int nx2 = ux + NavGridSteps.DX[d], ny2 = uy + NavGridSteps.DY[d];
                if (nx2 < x0 || nx2 >= x1 || ny2 < y0 || ny2 >= y1) continue;
                int v = ny2 * n + nx2, vl = (ny2 - y0) * s + (nx2 - x0);
                var nd = k + NavGridSteps.StepCost(nav.Cost, u, v, d);
                if (nd < dist[vl]) { dist[vl] = nd; heap.Push(nd, v); }
            }
        }
    }

    public static Result? FindPath(NavContext nav, int startCell, int goalCell, Scratch s)
    {
        var hpa = nav.Hpa!;
        int n = nav.CellCount, n2 = n * n;
        int cs = hpa.ClusterSize, cc = hpa.ClustersPerSide;
        var blocks = hpa.Blocks;
        int sc = NavGridSteps.ClusterOf(startCell, n, cs, cc), gc = NavGridSteps.ClusterOf(goalCell, n, cs, cc);
        var inf = Fix64.MaxValue / 4;

        ClusterDijkstra(nav, sc, startCell, s.StartDist, s.Heap);
        if (sc == gc)
        {
            var d = s.StartDist[NavGridSteps.LocalIndex(goalCell, n, cs)];
            if (d < inf)
            {
                return new Result { Clusters = new[] { sc }, Cells = new[] { startCell, goalCell }, Cost = d, Expanded = 0 };
            }
        }

        ClusterDijkstra(nav, gc, goalCell, s.GoalDist, s.Heap);

        var g = s.G; var parent = s.Parent; var closed = s.Closed; var heap = s.Heap;
        g.Clear(); parent.Clear(); closed.Clear(); heap.Clear();
        var hMul = nav.MinCost;
        var sb = blocks[sc];
        for (int i = 0; i < sb.Cells.Length; i++)
        {
            int c = sb.Cells[i];
            if (c >= n2) continue; // 桥面节点不会被纯地面的起点 Dijkstra 到达
            var d = s.StartDist[NavGridSteps.LocalIndex(c, n, cs)];
            if (d >= inf) continue;
            g[c] = d; parent[c] = -1;
            heap.Push(d + NavGridSteps.Octile(c, goalCell, n) * hMul, c);
        }

        var best = Fix64.MaxValue;
        int expanded = 0, u = 0;
        var gu = Fix64.Zero;
        void Relax(int v, Fix64 w)
        {
            if (closed.Contains(v)) return;
            var ng = gu + w;
            if (!g.TryGetValue(v, out var gv) || ng < gv)
            {
                g[v] = ng; parent[v] = u;
                heap.Push(ng + NavGridSteps.Octile(v % n2, goalCell, n) * hMul, v);
            }
        }

        while (heap.Size > 0)
        {
            u = heap.Pop();
            if (u == GoalKey) break;
            if (!closed.Add(u)) continue;
            expanded++;
            gu = g[u];
            // 键 ≥ N² 是桥面节点:终点在地面,它们永远不会结束搜索
            int cl = NavGridSteps.ClusterOf(u % n2, n, cs, cc);
            var b = blocks[cl];
            int i = b.Index[u];
            if (cl == gc && u < n2)
            {
                var gd = s.GoalDist[NavGridSteps.LocalIndex(u, n, cs)];
                if (gd < inf && gu + gd < best)
                {
                    best = gu + gd; parent[GoalKey] = u; heap.Push(best, GoalKey);
                }
            }

            for (int k = b.AdjStart[i]; k < b.AdjStart[i + 1]; k++) Relax(b.AdjTo[k], b.AdjCost[k]);
            if (hpa.LinkOut.TryGetValue(u, out var lo))
            {
                foreach (var (to, cost) in lo) Relax(to, cost);
            }
        }

        if (best >= inf) return null;
        var path = new List<int>();
        for (int v = parent[GoalKey]; v != -1; v = parent[v]) path.Add(v);
        path.Reverse();
        var clusters = new List<int> { sc };
        var cells = new List<int> { startCell };
        foreach (int k2 in path)
        {
            int c = k2 % n2;
            cells.Add(c);
            int cl = NavGridSteps.ClusterOf(c, n, cs, cc);
            if (clusters[clusters.Count - 1] != cl) clusters.Add(cl);
        }

        cells.Add(goalCell);
        if (clusters[clusters.Count - 1] != gc) clusters.Add(gc);
        return new Result { Clusters = clusters.ToArray(), Cells = cells.ToArray(), Cost = best, Expanded = expanded };
    }
}
