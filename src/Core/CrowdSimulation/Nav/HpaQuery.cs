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

    /// <summary>查询暂存(RT-03:每 worker 一份;与烘焙用的图内暂存互不相干)。
    /// Relax 是方法而不是局部函数:局部函数会按每次搜索分配一个闭包。</summary>
    public sealed class Scratch
    {
        public Scratch(int navCellsPerTile)
        {
            StartDist = new Fix64[navCellsPerTile];
            GoalDist = new Fix64[navCellsPerTile];
            GScore.EnsureCapacity(4096);
            Parent.EnsureCapacity(4096);
            Closed.EnsureCapacity(4096);
        }

        public Fix64[] StartDist { get; }
        public Fix64[] GoalDist { get; }
        public NavMinHeap Heap { get; } = new(8192);
        public Dictionary<int, Fix64> GScore { get; } = new(4096);
        public Dictionary<int, int> Parent { get; } = new(4096);
        public HashSet<int> Closed { get; } = new(4096);
        public int[] CellBuf { get; private set; } = new int[4096];
        public int CellCount;
        public int[] TileBuf { get; private set; } = new int[1024];
        public int TileCount;
        public readonly Result Found = new();
        public Fix64 Gu;
        public int U;
        public Fix64 HMul;
        public int GoalCell;
        public int NavCellsPerSide;
        public int N2;

        public void Relax(int v, Fix64 w)
        {
            if (Closed.Contains(v)) return;
            var ng = Gu + w;
            if (!GScore.TryGetValue(v, out var gv) || ng < gv)
            {
                GScore[v] = ng;
                Parent[v] = U;
                Heap.Push(ng + NavGridSteps.Octile(v % N2, GoalCell, NavCellsPerSide) * HMul, v);
            }
        }

        public void SetCells(int count)
        {
            if (CellBuf.Length < count)
            {
                var grown = new int[Math.Max(count, CellBuf.Length * 2)];
                Array.Copy(CellBuf, grown, CellCount);
                CellBuf = grown;
            }

            CellCount = count;
        }

        public void SetTiles(int count)
        {
            if (TileBuf.Length < count)
            {
                var grown = new int[Math.Max(count, TileBuf.Length * 2)];
                Array.Copy(TileBuf, grown, TileCount);
                TileBuf = grown;
            }

            TileCount = count;
        }

        public Result Publish(Fix64 cost, int expanded)
        {
            Found.TileIds = TileBuf;
            Found.TileCount = TileCount;
            Found.Cells = CellBuf;
            Found.CellCount = CellCount;
            Found.Cost = cost;
            Found.Expanded = expanded;
            return Found;
        }
    }

    public sealed class Result
    {
        public int[] TileIds = Array.Empty<int>();
        public int TileCount;
        public int[] Cells = Array.Empty<int>();
        public int CellCount;
        public Fix64 Cost;
        public int Expanded;
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
                s.SetTiles(1);
                s.TileBuf[0] = sc;
                s.SetCells(2);
                s.CellBuf[0] = startCell;
                s.CellBuf[1] = goalCell;
                return s.Publish(d, 0);
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
        int expanded = 0;
        s.HMul = hMul;
        s.GoalCell = goalCell;
        s.NavCellsPerSide = navCellsPerSide;
        s.N2 = n2;
        while (heap.Size > 0)
        {
            int u = heap.Pop();
            s.U = u;
            if (u == GoalKey) break;
            if (!closed.Add(u)) continue;
            expanded++;
            s.Gu = g[u];
            // 键 ≥ N² 是桥面节点:终点在地面,它们永远不会结束搜索
            int cl = NavGridSteps.TileOf(u % n2, navCellsPerSide, navCellsPerTile, navTilesPerSide);
            var b = blocks[cl];
            int i = b.Index[u];
            if (cl == gc && u < n2)
            {
                var gd = s.GoalDist[NavGridSteps.LocalIndex(u, navCellsPerSide, navCellsPerTile)];
                if (gd < inf && s.Gu + gd < best)
                {
                    best = s.Gu + gd; parent[GoalKey] = u; heap.Push(best, GoalKey);
                }
            }

            for (int k = b.AdjStart[i]; k < b.AdjStart[i + 1]; k++) s.Relax(b.AdjTo[k], b.AdjCost[k]);
            if (hpa.LinkOut.TryGetValue(u, out var lo))
            {
                for (int li = 0; li < lo.Count; li++) s.Relax(lo[li].To, lo[li].Cost);
            }
        }

        if (best >= inf) return null;
        int hops = 0;
        for (int v = parent[GoalKey]; v != -1; v = parent[v]) hops++;
        s.SetCells(hops + 2);
        s.CellBuf[0] = startCell;
        int write = hops;
        for (int v = parent[GoalKey]; v != -1; v = parent[v]) s.CellBuf[write--] = v % n2;
        s.CellBuf[hops + 1] = goalCell;
        s.SetTiles(1);
        s.TileBuf[0] = sc;
        int tiles = 1;
        for (int i = 1; i <= hops; i++)
        {
            int cl = NavGridSteps.TileOf(s.CellBuf[i], navCellsPerSide, navCellsPerTile, navTilesPerSide);
            if (s.TileBuf[tiles - 1] != cl)
            {
                if (tiles == s.TileBuf.Length)
                {
                    s.TileCount = tiles;
                    s.SetTiles(tiles + 1);
                }

                s.TileBuf[tiles++] = cl;
            }
        }

        if (s.TileBuf[tiles - 1] != gc)
        {
            if (tiles == s.TileBuf.Length)
            {
                s.TileCount = tiles;
                s.SetTiles(tiles + 1);
            }

            s.TileBuf[tiles++] = gc;
        }

        s.TileCount = tiles;
        return s.Publish(best, expanded);
    }
}
