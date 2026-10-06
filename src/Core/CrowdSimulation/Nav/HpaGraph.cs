using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// HPA* 抽象图(hpa.js + hpaLocal.js 移植,烘焙路径):tile = cluster,
/// 入口 = 共享边界上的开放格对(宽 run 取两端、窄 run 取中点,不超过 maxEntranceWidth),
/// 簇内边 = 簇内 Dijkstra 距离;跳跃链接成为端点间的有向抽象边;
/// 桥面层节点键 = N² + 格号,只在桥头 portal 与地面 0 代价相连。
/// 抽象图按 cluster 分块(不可变),为增量重烘焙与认知变体共享做准备。
/// </summary>
public sealed class HpaGraph
{
    public required int ClusterSize { get; init; }
    public required int ClustersPerSide { get; init; }
    public required int CellCount2 { get; init; }
    public required int MaxEntranceWidth { get; init; }

    /// <summary>每 cluster 的东边界入口(cellA, cellB)对列表。</summary>
    public required int[][] BorderE { get; init; }
    /// <summary>每 cluster 的南边界入口对列表。</summary>
    public required int[][] BorderS { get; init; }
    /// <summary>每 cluster 的桥面东/南跨界边(keyA, keyB, cost)三元组。</summary>
    public required int[][] UpE { get; init; }
    public required Fix64[][] UpECost { get; init; }
    public required int[][] UpS { get; init; }
    public required Fix64[][] UpSCost { get; init; }
    /// <summary>每 cluster 的节点格表(地面格在前,桥面节点 = N² + 格号 在后)。</summary>
    public required int[][] Cells { get; init; }
    /// <summary>簇内边:(i, j, dist) 三元组,下标为 Cells 内序号。</summary>
    public required int[][] Intra { get; init; }
    public required Fix64[][] IntraDist { get; init; }
    /// <summary>跨层 0 代价边(ground cell, deck key)对。</summary>
    public required int[][] CrossLayer { get; init; }
    /// <summary>不可变簇块:节点格 + 格→局部号 + CSR 出边(目标为格号,桥面目标带层位)。</summary>
    public required HpaClusterBlock[] Blocks { get; init; }
    /// <summary>跳跃链接的有向出边:起点格 → (目标格, 代价) 平铺。</summary>
    public required Dictionary<int, List<(int To, Fix64 Cost)>> LinkOut { get; set; }
    public required int NodeCount { get; set; }
    public required int EdgeCount { get; set; }

    // 查询暂存(不可重入:每次查询独占;并行作业须各自持有)
    public NavMinHeap Heap = new(512);
    public Fix64[] Dist => _dist;
    public Fix64[] StartDist => _startDist;
    public Fix64[] GoalDist => _goalDist;
    private Fix64[] _dist;
    private Fix64[] _startDist;
    private Fix64[] _goalDist;
    private int[] _localNb = Array.Empty<int>();
    private Fix64[] _localW = Array.Empty<Fix64>();
    private byte[] _localWant = Array.Empty<byte>();

    public static HpaGraph Build(NavContext nav, CrowdSimulationRuntimeConfig config)
    {
        int n = nav.CellCount, s = config.Hpa.ClusterSize, c = n / s, cc = c * c;
        var g = new HpaGraph
        {
            ClusterSize = s,
            ClustersPerSide = c,
            CellCount2 = n * n,
            MaxEntranceWidth = config.Hpa.MaxEntranceWidth,
            BorderE = new int[cc][],
            BorderS = new int[cc][],
            UpE = new int[cc][],
            UpECost = new Fix64[cc][],
            UpS = new int[cc][],
            UpSCost = new Fix64[cc][],
            Cells = new int[cc][],
            Intra = new int[cc][],
            IntraDist = new Fix64[cc][],
            CrossLayer = new int[cc][],
            Blocks = new HpaClusterBlock[cc],
            LinkOut = new Dictionary<int, List<(int, Fix64)>>(),
            NodeCount = 0,
            EdgeCount = 0,
        };
        g._dist = new Fix64[s * s];
        g._startDist = new Fix64[s * s];
        g._goalDist = new Fix64[s * s];
        g._localNb = new int[s * s * 8];
        g._localW = new Fix64[s * s * 8];
        g._localWant = new byte[s * s];

        for (int cl = 0; cl < cc; cl++) { ScanE(g, nav, cl); ScanS(g, nav, cl); UpScan(g, nav, cl, true); UpScan(g, nav, cl, false); }
        var lk = LinksByCluster(nav.Links, n, s, c);
        for (int cl = 0; cl < cc; cl++) BakeCluster(g, nav, cl, lk);
        for (int cl = 0; cl < cc; cl++) g.Blocks[cl] = BuildBlock(g, nav, cl);
        FinishGraph(g, nav.Links);
        return g;
    }

    // ── 边界入口扫描 ─────────────────────────────────────────────
    private static int[] ScanBorder(byte[] passable, int maxW, Func<int, int> cellA, Func<int, int> cellB, int len)
    {
        var output = new List<int>();
        int run = -1;
        for (int k = 0; k <= len; k++)
        {
            bool open = k < len && passable[cellA(k)] != 0 && passable[cellB(k)] != 0;
            if (open && run < 0) run = k;
            else if (!open && run >= 0)
            {
                int e = k - 1;
                if (e - run + 1 > maxW) { output.Add(cellA(run)); output.Add(cellB(run)); output.Add(cellA(e)); output.Add(cellB(e)); }
                else { int p = (run + e) >> 1; output.Add(cellA(p)); output.Add(cellB(p)); }
                run = -1;
            }
        }

        return output.ToArray();
    }

    private static void ScanE(HpaGraph g, NavContext nav, int cl)
    {
        int s = g.ClusterSize, c = g.ClustersPerSide, n = nav.CellCount, cx = cl % c;
        if (cx >= c - 1) { g.BorderE[cl] = Array.Empty<int>(); return; }
        int x0 = (cx + 1) * s - 1, ys = cl / c * s;
        g.BorderE[cl] = ScanBorder(nav.Passable, g.MaxEntranceWidth, k => (ys + k) * n + x0, k => (ys + k) * n + x0 + 1, s);
    }

    private static void ScanS(HpaGraph g, NavContext nav, int cl)
    {
        int s = g.ClusterSize, c = g.ClustersPerSide, n = nav.CellCount, cy = cl / c;
        if (cy >= c - 1) { g.BorderS[cl] = Array.Empty<int>(); return; }
        int y0 = (cy + 1) * s - 1, xs = cl % c * s;
        g.BorderS[cl] = ScanBorder(nav.Passable, g.MaxEntranceWidth, k => y0 * n + xs + k, k => (y0 + 1) * n + xs + k, s);
    }

    // 桥面跨界入口(仅桥面↔桥面;桥头 portal 的跨层边在 BakeCluster 的 xl 里)
    private static void UpScan(HpaGraph g, NavContext nav, int cl, bool east)
    {
        int s = g.ClusterSize, c = g.ClustersPerSide, n = nav.CellCount;
        int cx = cl % c, cy = cl / c;
        int nb = east ? (cx < c - 1 ? cl + 1 : -1) : (cy < c - 1 ? cl + c : -1);
        var list = new List<int>();
        var costs = new List<Fix64>();
        if (nb >= 0 && AnyUp(nav, cl, s) && AnyUp(nav, nb, s))
        {
            // 与 scanBorder 同一 run 逻辑,目标格函数映射到桥面可走格
            int Ga(int k) => east ? (cy * s + k) * n + cx * s + s - 1 : (cy * s + s - 1) * n + cx * s + k;
            var pairs = ScanBorder(nav.UpPass, g.MaxEntranceWidth, Ga, k => Ga(k) + (east ? 1 : n), s);
            for (int p = 0; p < pairs.Length; p += 2)
            {
                list.Add(g.CellCount2 + pairs[p]);
                list.Add(g.CellCount2 + pairs[p + 1]);
                costs.Add((nav.UpCost[pairs[p]] + nav.UpCost[pairs[p + 1]]) * Fix64.HalfValue);
            }
        }

        if (east) { g.UpE[cl] = list.ToArray(); g.UpECost[cl] = costs.ToArray(); }
        else { g.UpS[cl] = list.ToArray(); g.UpSCost[cl] = costs.ToArray(); }
    }

    // 该 cluster 是否有桥面可走格
    private static bool AnyUp(NavContext nav, int tileId, int tileSize)
    {
        int n = nav.CellCount;
        int c = n / tileSize;
        int ox = tileId % c * tileSize, oy = tileId / c * tileSize;
        for (int y = 0; y < tileSize; y++)
        {
            for (int x = 0; x < tileSize; x++)
            {
                if (nav.UpPass[(oy + y) * n + ox + x] != 0) return true;
            }
        }

        return false;
    }

    // ── 簇烘焙 ─────────────────────────────────────────────
    private static Dictionary<int, List<int>> LinksByCluster(NavLinkSet? links, int n, int s, int c)
    {
        var m = new Dictionary<int, List<int>>();
        if (links != null)
        {
            for (int e = 0; e < links.Count; e++)
            {
                foreach (var cell in new[] { links.From[e], links.To[e] })
                {
                    int cl = NavGridSteps.ClusterOf(cell, n, s, c);
                    if (!m.TryGetValue(cl, out var l)) m[cl] = l = new List<int>();
                    l.Add(cell);
                }
            }
        }

        return m;
    }

    private static void BakeCluster(HpaGraph g, NavContext nav, int cl, Dictionary<int, List<int>> lk)
    {
        int s = g.ClusterSize, c = g.ClustersPerSide, n = nav.CellCount, n2 = n * n;
        int cx = cl % c, cy = cl / c;
        var cells = new List<int>();
        var seen = new HashSet<int>();
        void Add(int cell) { if (seen.Add(cell)) cells.Add(cell); }
        void Each(int[] b, int side) { for (int k = side; k < b.Length; k += 2) Add(b[k]); }

        Each(g.BorderE[cl], 0);
        if (cx > 0) Each(g.BorderE[cl - 1], 1);
        Each(g.BorderS[cl], 0);
        if (cy > 0) Each(g.BorderS[cl - c], 1);
        if (lk.TryGetValue(cl, out var linkCells)) foreach (var cell in linkCells) Add(cell);

        // 桥面节点:跨界入口 + portal 跨层对
        var up = new List<int>();
        var uSeen = new HashSet<int>();
        var xl = new List<int>();
        void UAdd(int key) { if (uSeen.Add(key)) up.Add(key); }
        void UEach(int[] b, int side) { for (int k = side; k < b.Length; k += 2) UAdd(b[k]); }
        if (AnyUp(nav, cl, s))
        {
            UEach(g.UpE[cl], 0);
            if (cx > 0) UEach(g.UpE[cl - 1], 1);
            UEach(g.UpS[cl], 0);
            if (cy > 0) UEach(g.UpS[cl - c], 1);
            for (int i = 0; i < n2; i++)
            {
                if (nav.Portal[i] == 0) continue;
                int px = i % n, py = i / n;
                if (px / s != cx || py / s != cy) continue;
                if (nav.Passable[i] == 0) continue;
                Add(i);
                UAdd(n2 + i);
                xl.Add(i);
                xl.Add(n2 + i);
            }
        }

        // 簇内边:逐节点对(i<j)的簇内 Dijkstra(只读到 j 的距离即停)
        var intra = new List<int>();
        var intraDist = new List<Fix64>();
        int gCount = cells.Count;
        PrepareLocal(g, nav, cl);
        for (int i = 0; i < gCount; i++)
        {
            int need = 0;
            for (int j = i + 1; j < gCount; j++)
            {
                int l = NavGridSteps.LocalIndex(cells[j], n, s);
                if (g._localWant[l] == 0) { g._localWant[l] = 1; need++; }
            }

            if (need > 0) LocalDijkstra(g, nav, NavGridSteps.LocalIndex(cells[i], n, s), need);
            for (int j = i + 1; j < gCount; j++) g._localWant[NavGridSteps.LocalIndex(cells[j], n, s)] = 0;
            for (int j = i + 1; j < gCount; j++)
            {
                var d = g._dist[NavGridSteps.LocalIndex(cells[j], n, s)];
                if (d < Fix64.MaxValue / 4)
                {
                    intra.Add(i); intra.Add(j);
                    intraDist.Add(d);
                }
            }
        }

        // 桥面节点的簇内边(桥面 tile 内 8 连通 Dijkstra)
        if (up.Count > 0)
        {
            for (int i = 0; i < up.Count; i++)
            {
                UpperDijkstra(g, nav, cl, LocalOf(up[i] - n2, n, s));
                for (int j = i + 1; j < up.Count; j++)
                {
                    var d = g._dist[LocalOf(up[j] - n2, n, s)];
                    if (d < Fix64.MaxValue / 4)
                    {
                        intra.Add(gCount + i); intra.Add(gCount + j);
                        intraDist.Add(d);
                    }
                }
            }
        }

        g.Cells[cl] = cells.Concat(up).ToArray();
        g.Intra[cl] = intra.ToArray();
        g.IntraDist[cl] = intraDist.ToArray();
        g.CrossLayer[cl] = xl.ToArray();
    }

    private static int LocalOf(int cell, int n, int s) => NavGridSteps.LocalIndex(cell, n, s);

    // 簇内 8 连通图的一次性准备(目标 + 步代价)
    private static void PrepareLocal(HpaGraph g, NavContext nav, int cl)
    {
        int s = g.ClusterSize, c = g.ClustersPerSide, n = nav.CellCount;
        int x0 = cl % c * s, y0 = cl / c * s;
        for (int ly = 0; ly < s; ly++)
        {
            for (int lx = 0; lx < s; lx++)
            {
                int l = ly * s + lx, o = l * 8, ux = x0 + lx, uy = y0 + ly, u = uy * n + ux;
                for (int d = 0; d < 8; d++)
                {
                    int nx = lx + NavGridSteps.DX[d], ny = ly + NavGridSteps.DY[d];
                    if (nx < 0 || ny < 0 || nx >= s || ny >= s || !NavGridSteps.CanStep(nav.Passable, n, ux, uy, d))
                    {
                        g._localNb[o + d] = -1;
                        continue;
                    }

                    g._localNb[o + d] = ny * s + nx;
                    g._localW[o + d] = NavGridSteps.StepCost(nav.Cost, u, (y0 + ny) * n + x0 + nx, d);
                }
            }
        }
    }

    private static void LocalDijkstra(HpaGraph g, NavContext nav, int start, int need)
    {
        Array.Fill(g._dist, Fix64.MaxValue);
        g.Heap.Clear();
        g._dist[start] = Fix64.Zero;
        g.Heap.Push(Fix64.Zero, start);
        while (g.Heap.Size > 0 && need > 0)
        {
            var k = g.Heap.PeekKey();
            int u = g.Heap.Pop();
            if (k > g._dist[u]) continue;
            if (g._localWant[u] != 0) { g._localWant[u] = 0; need--; }
            for (int o = u * 8, e = o + 8; o < e; o++)
            {
                int v = g._localNb[o];
                if (v < 0) continue;
                var nd = k + g._localW[o];
                if (nd < g._dist[v]) { g._dist[v] = nd; g.Heap.Push(nd, v); }
            }
        }
    }

    // 桥面 tile 内的 8 连通 Dijkstra(禁切角,代价走桥面)
    private static void UpperDijkstra(HpaGraph g, NavContext nav, int cl, int start)
    {
        int s = g.ClusterSize, n = nav.CellCount;
        int x0 = cl % g.ClustersPerSide * s, y0 = cl / g.ClustersPerSide * s;
        Array.Fill(g._dist, Fix64.MaxValue);
        g.Heap.Clear();
        g._dist[start] = Fix64.Zero;
        g.Heap.Push(Fix64.Zero, start);
        while (g.Heap.Size > 0)
        {
            var k = g.Heap.PeekKey();
            int cell = g.Heap.Pop();
            if (k > g._dist[cell]) continue;
            int x = cell % s, y = cell / s;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + NavGridSteps.DX[d], ny = y + NavGridSteps.DY[d];
                if (nx < 0 || ny < 0 || nx >= s || ny >= s) continue;
                int v = ny * s + nx;
                int gCell = (y0 + y) * n + x0 + x;
                int gCellV = (y0 + ny) * n + x0 + nx;
                if (nav.UpPass[gCellV] == 0) continue;
                bool diag = NavGridSteps.DX[d] != 0 && NavGridSteps.DY[d] != 0;
                if (diag && (nav.UpPass[(y0 + y) * n + x0 + nx] == 0 || nav.UpPass[(y0 + ny) * n + x0 + x] == 0)) continue;
                var nd = k + (nav.UpCost[gCell] + nav.UpCost[gCellV]) * Fix64.HalfValue * (diag ? NavGridSteps.Sqrt2 : Fix64.OneValue);
                if (nd < g._dist[v]) { g._dist[v] = nd; g.Heap.Push(nd, v); }
            }
        }
    }

    // ── 簇块与图收尾 ─────────────────────────────────────────────
    private static HpaClusterBlock BuildBlock(HpaGraph g, NavContext nav, int cl)
    {
        int c = g.ClustersPerSide;
        int cx = cl % c, cy = cl / c;
        var list = g.Cells[cl];
        int count = list.Length;
        var index = new Dictionary<int, int>();
        var outE = new List<(int To, Fix64 Cost)>[count];
        for (int i = 0; i < count; i++) { index[list[i]] = i; outE[i] = new List<(int, Fix64)>(); }

        void Border(int[] b, int own)
        {
            for (int k = 0; k < b.Length; k += 2)
            {
                outE[index[b[k + own]]].Add((b[k + 1 - own], (nav.Cost[b[k]] + nav.Cost[b[k + 1]]) * Fix64.HalfValue));
            }
        }

        Border(g.BorderE[cl], 0);
        if (cx > 0) Border(g.BorderE[cl - 1], 1);
        Border(g.BorderS[cl], 0);
        if (cy > 0) Border(g.BorderS[cl - c], 1);

        void Up(int[] b, Fix64[] costs, int own)
        {
            for (int k = 0; k < b.Length; k += 2)
            {
                outE[index[b[k + own]]].Add((b[k + 1 - own], costs[k / 2]));
            }
        }

        Up(g.UpE[cl], g.UpECost[cl], 0);
        if (cx > 0) Up(g.UpE[cl - 1], g.UpECost[cl - 1], 1);
        Up(g.UpS[cl], g.UpSCost[cl], 0);
        if (cy > 0) Up(g.UpS[cl - c], g.UpSCost[cl - c], 1);

        var xl = g.CrossLayer[cl];
        for (int k = 0; k < xl.Length; k += 2)
        {
            outE[index[xl[k]]].Add((xl[k + 1], Fix64.Zero));
            outE[index[xl[k + 1]]].Add((xl[k], Fix64.Zero));
        }

        var intra = g.Intra[cl];
        var intraDist = g.IntraDist[cl];
        for (int k = 0; k < intra.Length; k += 2)
        {
            int i = intra[k], j = intra[k + 1];
            var d = intraDist[k / 2];
            outE[i].Add((list[j], d));
            outE[j].Add((list[i], d));
        }

        var adjStart = new int[count + 1];
        for (int i = 0; i < count; i++) adjStart[i + 1] = adjStart[i] + outE[i].Count;
        var adjTo = new int[adjStart[count]];
        var adjCost = new Fix64[adjStart[count]];
        for (int i = 0, p = 0; i < count; i++)
        {
            foreach (var (to, cost) in outE[i])
            {
                adjTo[p] = to;
                adjCost[p] = cost;
                p++;
            }
        }

        int edges = g.BorderE[cl].Length / 2 + g.BorderS[cl].Length / 2 + intra.Length / 2
            + g.UpE[cl].Length / 2 + g.UpS[cl].Length / 2 + xl.Length / 2;
        return new HpaClusterBlock
        {
            Cells = list,
            AdjStart = adjStart,
            AdjTo = adjTo,
            AdjCost = adjCost,
            EdgeCount = edges,
            Index = index,
        };
    }

    private static void FinishGraph(HpaGraph g, NavLinkSet? links)
    {
        var lo = new Dictionary<int, List<(int, Fix64)>>();
        if (links != null)
        {
            for (int e = 0; e < links.Count; e++)
            {
                if (!lo.TryGetValue(links.From[e], out var a)) lo[links.From[e]] = a = new List<(int, Fix64)>();
                a.Add((links.To[e], links.Cost[e]));
            }
        }

        int nodes = 0, edges = links?.Count ?? 0;
        foreach (var b in g.Blocks) { nodes += b.Cells.Length; edges += b.EdgeCount; }
        g.LinkOut = lo;
        g.NodeCount = nodes;
        g.EdgeCount = edges;
    }

    // ── 扁平视图(校验 / 叠加层 / 摘要用,不在查询路径上) ─────────────────────────────
    /// <summary>flattenHpa 移植:每节点先块内边后链接边;nodeCell 不带层位(adjTo 带)。</summary>
    public (int[] NodeCell, byte[] NodeLayer, int[] NodeCluster, int[] AdjStart, int[] AdjTo, Fix64[] AdjCost) Flatten()
    {
        var nodeCell = new List<int>();
        var nodeLayer = new List<byte>();
        var nodeCluster = new List<int>();
        var adjStart = new List<int> { 0 };
        var adjTo = new List<int>();
        var adjCost = new List<Fix64>();
        for (int cl = 0; cl < Blocks.Length; cl++)
        {
            var b = Blocks[cl];
            for (int i = 0; i < b.Cells.Length; i++)
            {
                int cell = b.Cells[i];
                nodeCell.Add(cell % CellCount2);
                nodeLayer.Add((byte)(cell >= CellCount2 ? 1 : 0));
                nodeCluster.Add(cl);
                for (int k = b.AdjStart[i]; k < b.AdjStart[i + 1]; k++)
                {
                    adjTo.Add(b.AdjTo[k] % CellCount2);
                    adjCost.Add(b.AdjCost[k]);
                }

                if (LinkOut.TryGetValue(cell, out var lo))
                {
                    foreach (var (to, cost) in lo)
                    {
                        adjTo.Add(to);
                        adjCost.Add(cost);
                    }
                }

                adjStart.Add(adjTo.Count);
            }
        }

        return (nodeCell.ToArray(), nodeLayer.ToArray(), nodeCluster.ToArray(), adjStart.ToArray(), adjTo.ToArray(), adjCost.ToArray());
    }
}

public sealed class HpaClusterBlock
{
    public required int[] Cells { get; init; }
    public required Dictionary<int, int> Index { get; init; }
    public required int[] AdjStart { get; init; }
    public required int[] AdjTo { get; init; }
    public required Fix64[] AdjCost { get; init; }
    public required int EdgeCount { get; init; }
}
