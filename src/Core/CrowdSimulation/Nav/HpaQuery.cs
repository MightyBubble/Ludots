using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// HPA* 抽象图寻路(hpa.js findPath 移植):起点 tile 内 Dijkstra 到本 tile 全部入口,
/// 抽象图 A*(块内边 + 跳跃链接边),进入终点 tile 的节点再接终点 tile 内 Dijkstra 收尾。
/// 返回 tile 走廊 + 途经的入口格;代价为零成本启发 × minCost(octile)。
/// </summary>
public static class HpaQuery
{
    private const int GoalKey = -2;

    /// <summary>查询暂存(RT-03:每 worker 一份;与烘焙用的图内暂存互不相干)。</summary>
    public sealed class Scratch
    {
        public Scratch(int navCellsPerTile)
        {
            StartDist = new Fix64[navCellsPerTile];
            GoalDist = new Fix64[navCellsPerTile];
        }

        public Fix64[] StartDist { get; }
        public Fix64[] GoalDist { get; }
        public NavMinHeap Heap { get; } = new(512);
        public Dictionary<int, Fix64> GScore { get; } = new();
        public Dictionary<int, int> Parent { get; } = new();
        public HashSet<int> Closed { get; } = new();
    }

    public sealed class Result
    {
        public required int[] TileIds { get; init; }
        public required int[] Cells { get; init; }
        public required Fix64 Cost { get; init; }
        public required int Expanded { get; init; }
    }

    /// <summary>tile 内 8 连通 Dijkstra(clusterDijkstra 移植),dist 按 tile 内局部下标填满。</summary>
    public static void TileDijkstra(NavContext nav, int cl, int startCell, Fix64[] dist, NavMinHeap heap)
    {
        var hpa = nav.Hpa!;
        int navCellsPerTile = hpa.NavCellsPerTile, navTilesPerSide = hpa.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide;
        int x0 = cl % navTilesPerSide * navCellsPerTile, y0 = cl / navTilesPerSide * navCellsPerTile;
        int x1 = Math.Min(navCellsPerSide, x0 + navCellsPerTile), y1 = Math.Min(navCellsPerSide, y0 + navCellsPerTile);
        Array.Fill(dist, Fix64.MaxValue);
        heap.Clear();
        dist[NavGridSteps.LocalIndex(startCell, navCellsPerSide, navCellsPerTile)] = Fix64.Zero;
        heap.Push(Fix64.Zero, startCell);
        while (heap.Size > 0)
        {
            var k = heap.PeekKey();
            int u = heap.Pop();
            int ux = u % navCellsPerSide, uy = u / navCellsPerSide;
            if (k > dist[(uy - y0) * navCellsPerTile + (ux - x0)]) continue;
            for (int d = 0; d < 8; d++)
            {
                if (!NavGridSteps.CanStep(nav.Passable, navCellsPerSide, ux, uy, d)) continue;
                int nx2 = ux + NavGridSteps.DX[d], ny2 = uy + NavGridSteps.DY[d];
                if (nx2 < x0 || nx2 >= x1 || ny2 < y0 || ny2 >= y1) continue;
                int v = ny2 * navCellsPerSide + nx2, vl = (ny2 - y0) * navCellsPerTile + (nx2 - x0);
                var nd = k + NavGridSteps.StepCost(nav.Cost, u, v, d);
                if (nd < dist[vl]) { dist[vl] = nd; heap.Push(nd, v); }
            }
        }
    }

    public static Result? FindPath(NavContext nav, int startCell, int goalCell, Scratch s)
    {
        var hpa = nav.Hpa!;
        int navCellsPerSide = nav.NavCellsPerSide, n2 = navCellsPerSide * navCellsPerSide;
        int navCellsPerTile = hpa.NavCellsPerTile, navTilesPerSide = hpa.NavTilesPerSide;
        var blocks = hpa.Blocks;
        int sc = NavGridSteps.TileOf(startCell, navCellsPerSide, navCellsPerTile, navTilesPerSide), gc = NavGridSteps.TileOf(goalCell, navCellsPerSide, navCellsPerTile, navTilesPerSide);
        var inf = Fix64.MaxValue / 4;

        TileDijkstra(nav, sc, startCell, s.StartDist, s.Heap);
        if (sc == gc)
        {
            var d = s.StartDist[NavGridSteps.LocalIndex(goalCell, navCellsPerSide, navCellsPerTile)];
            if (d < inf)
            {
                return new Result { TileIds = new[] { sc }, Cells = new[] { startCell, goalCell }, Cost = d, Expanded = 0 };
            }
        }

        TileDijkstra(nav, gc, goalCell, s.GoalDist, s.Heap);

        var g = s.GScore; var parent = s.Parent; var closed = s.Closed; var heap = s.Heap;
        g.Clear(); parent.Clear(); closed.Clear(); heap.Clear();
        var hMul = nav.MinCost;
        var sb = blocks[sc];
        // Cells 尾部是增量重烘留下的空位。活节点数是 CellCount，扫 Length 会把空位里的 0 当成格号。
        for (int i = 0; i < sb.CellCount; i++)
        {
            int c = sb.Cells[i];
            if (c >= n2) continue; // 桥面节点不会被纯地面的起点 Dijkstra 到达
            var d = s.StartDist[NavGridSteps.LocalIndex(c, navCellsPerSide, navCellsPerTile)];
            if (d >= inf) continue;
            g[c] = d; parent[c] = -1;
            heap.Push(d + NavGridSteps.Octile(c, goalCell, navCellsPerSide) * hMul, c);
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
                heap.Push(ng + NavGridSteps.Octile(v % n2, goalCell, navCellsPerSide) * hMul, v);
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
            int cl = NavGridSteps.TileOf(u % n2, navCellsPerSide, navCellsPerTile, navTilesPerSide);
            var b = blocks[cl];
            int i = b.Index[u];
            if (cl == gc && u < n2)
            {
                var gd = s.GoalDist[NavGridSteps.LocalIndex(u, navCellsPerSide, navCellsPerTile)];
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
        var tileIds = new List<int> { sc };
        var cells = new List<int> { startCell };
        foreach (int k2 in path)
        {
            int c = k2 % n2;
            cells.Add(c);
            int cl = NavGridSteps.TileOf(c, navCellsPerSide, navCellsPerTile, navTilesPerSide);
            if (tileIds[tileIds.Count - 1] != cl) tileIds.Add(cl);
        }

        cells.Add(goalCell);
        if (tileIds[tileIds.Count - 1] != gc) tileIds.Add(gc);
        return new Result { TileIds = tileIds.ToArray(), Cells = cells.ToArray(), Cost = best, Expanded = expanded };
    }
}
