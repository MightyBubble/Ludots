using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// HPA* 抽象图(hpa.js + hpaLocal.js 移植,烘焙路径):
/// 入口 = 共享边界上的开放格对(宽 run 取两端、窄 run 取中点,不超过 maxEntranceWidth),
/// tile 内边 = tile 内 Dijkstra 距离;跳跃链接成为端点间的有向抽象边;
/// 桥面层节点键 = 导航格总数 + 格号,只在桥头 portal 与地面 0 代价相连。
/// 抽象图按 tile 分块(不可变),为增量重烘焙与认知变体共享做准备。
/// </summary>
public sealed class HpaGraph
{
    public required int NavCellsPerTile { get; init; }
    public required int NavTilesPerSide { get; init; }
    public required int NavCellTotal { get; init; }
    public required int MaxEntranceWidthNavCells { get; init; }

    /// <summary>每 tile 的东边界入口(cellA, cellB)对列表。</summary>
    public required int[][] BorderE { get; set; }
    /// <summary>每 tile 的南边界入口对列表。</summary>
    public required int[][] BorderS { get; set; }
    /// <summary>每 tile 的桥面东/南跨界边(keyA, keyB, cost)三元组。</summary>
    public required int[][] UpE { get; set; }
    public required Fix64[][] UpECost { get; set; }
    public required int[][] UpS { get; set; }
    public required Fix64[][] UpSCost { get; set; }
    /// <summary>每 tile 的节点格表(地面格在前,桥面节点 = 导航格总数 + 格号 在后)。</summary>
    public required int[][] Cells { get; set; }
    /// <summary>簇内边:(i, j, dist) 三元组,下标为 Cells 内序号。</summary>
    public required int[][] Intra { get; set; }
    public required Fix64[][] IntraDist { get; set; }
    /// <summary>跨层 0 代价边(ground cell, deck key)对。</summary>
    public required int[][] CrossLayer { get; set; }
    /// <summary>不可变簇块:节点格 + 格→局部号 + CSR 出边(目标为格号,桥面目标带层位)。</summary>
    public required HpaTileBlock[] Blocks { get; set; }
    /// <summary>跳跃链接的有向出边:起点格 → (目标格, 代价) 平铺。</summary>
    public required Dictionary<int, List<(int To, Fix64 Cost)>> LinkOut { get; set; }
    /// <summary>跳跃链接端点按簇的归属表(增量:与重烘后的表比对,落在干净簇的新端点要重烘该簇)。</summary>
    public Dictionary<int, List<int>>? LinkTiles { get; set; }
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
    // Update 的升序缓冲。本图独占:重烘在导航锁内,查询不碰它;变体克隆不共享。
    private StableInt[] _ascend = Array.Empty<StableInt>();

    /// <summary>认知变体克隆(F02):外层容器全拷;内层数组/tile 块/LinkOut 的 List 与真相(及
    /// 其他同源变体)共用,禁止原地写——写了就击穿全体共享者。一切改写只能整体替换变体自有的
    /// 外层槽位(增量重烘的全部写入点均如此);查询暂存全新。</summary>
    public HpaGraph CloneForVariant()
    {
        return new HpaGraph
        {
            NavCellsPerTile = NavCellsPerTile,
            NavTilesPerSide = NavTilesPerSide,
            NavCellTotal = NavCellTotal,
            MaxEntranceWidthNavCells = MaxEntranceWidthNavCells,
            BorderE = (int[][])BorderE.Clone(),
            BorderS = (int[][])BorderS.Clone(),
            UpE = (int[][])UpE.Clone(),
            UpECost = (Fix64[][])UpECost.Clone(),
            UpS = (int[][])UpS.Clone(),
            UpSCost = (Fix64[][])UpSCost.Clone(),
            Cells = (int[][])Cells.Clone(),
            Intra = (int[][])Intra.Clone(),
            IntraDist = (Fix64[][])IntraDist.Clone(),
            CrossLayer = (int[][])CrossLayer.Clone(),
            Blocks = (HpaTileBlock[])Blocks.Clone(),
            LinkOut = new Dictionary<int, List<(int, Fix64)>>(LinkOut),
            LinkTiles = LinkTiles == null ? null : new Dictionary<int, List<int>>(LinkTiles),
            NodeCount = NodeCount,
            EdgeCount = EdgeCount,
            Heap = new NavMinHeap(512),
            _dist = new Fix64[_dist.Length],
            _startDist = new Fix64[_startDist.Length],
            _goalDist = new Fix64[_goalDist.Length],
            _localNb = new int[_localNb.Length == 0 ? NavCellsPerTile * NavCellsPerTile * 8 : _localNb.Length],
            _localW = new Fix64[_localW.Length == 0 ? NavCellsPerTile * NavCellsPerTile * 8 : _localW.Length],
            _localWant = new byte[_localWant.Length == 0 ? NavCellsPerTile * NavCellsPerTile : _localWant.Length],
        };
    }

    public static HpaGraph Build(NavContext nav, CrowdSimulationRuntimeConfig config)
    {
        int navCellsPerSide = nav.NavCellsPerSide, navCellsPerTile = config.Hpa.NavCellsPerTile, navTilesPerSide = navCellsPerSide / navCellsPerTile, navTileCount = navTilesPerSide * navTilesPerSide;
        var g = new HpaGraph
        {
            NavCellsPerTile = navCellsPerTile,
            NavTilesPerSide = navTilesPerSide,
            NavCellTotal = navCellsPerSide * navCellsPerSide,
            MaxEntranceWidthNavCells = config.Hpa.MaxEntranceWidthNavCells,
            BorderE = new int[navTileCount][],
            BorderS = new int[navTileCount][],
            UpE = new int[navTileCount][],
            UpECost = new Fix64[navTileCount][],
            UpS = new int[navTileCount][],
            UpSCost = new Fix64[navTileCount][],
            Cells = new int[navTileCount][],
            Intra = new int[navTileCount][],
            IntraDist = new Fix64[navTileCount][],
            CrossLayer = new int[navTileCount][],
            Blocks = new HpaTileBlock[navTileCount],
            LinkOut = new Dictionary<int, List<(int, Fix64)>>(),
            NodeCount = 0,
            EdgeCount = 0,
        };
        g._dist = new Fix64[navCellsPerTile * navCellsPerTile];
        g._startDist = new Fix64[navCellsPerTile * navCellsPerTile];
        g._goalDist = new Fix64[navCellsPerTile * navCellsPerTile];
        g._localNb = new int[navCellsPerTile * navCellsPerTile * 8];
        g._localW = new Fix64[navCellsPerTile * navCellsPerTile * 8];
        g._localWant = new byte[navCellsPerTile * navCellsPerTile];

        for (int cl = 0; cl < navTileCount; cl++) { ScanE(g, nav, cl); ScanS(g, nav, cl); UpScan(g, nav, cl, true); UpScan(g, nav, cl, false); }
        var lk = LinksByTile(nav.Links, navCellsPerSide, navCellsPerTile, navTilesPerSide);
        g.LinkTiles = lk;
        for (int cl = 0; cl < navTileCount; cl++) BakeTile(g, nav, cl, lk, keep: false);
        for (int cl = 0; cl < navTileCount; cl++) g.Blocks[cl] = BuildBlock(g, nav, cl);
        FinishGraph(g, nav.Links);
        return g;
    }

    // ── 增量维护(updateHpa 移植) ─────────────────────────────────────
    /// <summary>脏 tile 的 HPA 增量维护:触到的共享边界各重扫一次,入口表变化的簇(及边界对面
    /// 的邻居)重烘簇内边——入口未变的邻居块仍重建(边代价从本侧格现读)。结果与同一栅格的
    /// 全量构建逐位一致。</summary>
    public void Update(NavContext nav, NavLinkSet? links, IReadOnlyCollection<int> tiles, HashSet<int>? same)
    {
        int navTilesPerSide = NavTilesPerSide;
        var own = new HashSet<int>(tiles);
        var aff = new HashSet<int>(tiles);
        var rebake = new HashSet<int>(tiles);
        var east = new HashSet<int>();
        var south = new HashSet<int>();
        // 遍历序合同:本方法对无序集合(HashSet/字典键)的枚举一律按簇号升序显式化——
        // 各枚举只做集合并集或逐簇独立读写(Rescan 只加集,BakeTile/BuildBlock 逐簇
        // 写自有槽位),结果与序无关,升序仅为把序写成合同不依赖运行时枚举序。
        foreach (int t in tiles) // tiles 是调用方 List(CrowdNavRebake.TilesOf),有序容器
        {
            int cx = t % navTilesPerSide, cy = t / navTilesPerSide;
            east.Add(t);
            south.Add(t);
            if (cx > 0) east.Add(t - 1);
            if (cy > 0) south.Add(t - navTilesPerSide);
        }

        // 每条触到脏 tile 的边界只重扫一次(相邻脏 tile 共享边界);入口表未变则对面簇不重烘簇内边
        void Rescan(int cl, int nb, bool isEast)
        {
            var b0 = (isEast ? BorderE : BorderS)[cl];
            var u0 = (isEast ? UpE : UpS)[cl];
            var u0c = (isEast ? UpECost : UpSCost)[cl];
            if (isEast) ScanE(this, nav, cl);
            else ScanS(this, nav, cl);
            UpScan(this, nav, cl, isEast);
            if (nb < 0) return;
            aff.Add(cl);
            aff.Add(nb);
            bool changed = !SameCells(b0, (isEast ? BorderE : BorderS)[cl])
                || !SameCells(u0, (isEast ? UpE : UpS)[cl])
                || !SameCosts(u0c, (isEast ? UpECost : UpSCost)[cl]);
            if (changed)
            {
                rebake.Add(cl);
                rebake.Add(nb);
            }
        }

        int eastCount = LoadAscending(east);
        for (int i = 0; i < eastCount; i++)
        {
            int cl = _ascend[i].Key;
            Rescan(cl, cl % navTilesPerSide < navTilesPerSide - 1 ? cl + 1 : -1, true);
        }

        int southCount = LoadAscending(south);
        for (int i = 0; i < southCount; i++)
        {
            int cl = _ascend[i].Key;
            Rescan(cl, cl / navTilesPerSide < navTilesPerSide - 1 ? cl + navTilesPerSide : -1, false);
        }

        int ownCount = LoadAscending(own);
        for (int i = 0; i < ownCount; i++) aff.Add(_ascend[i].Key);
        var lk = LinksByTile(links, nav.NavCellsPerSide, NavCellsPerTile, navTilesPerSide);
        // 重生成的跳跃链接可能落进干净簇(链接跨 tile)——端点表变化的簇一并重烘
        var oldLk = LinkTiles ?? new Dictionary<int, List<int>>();
        int oldCount = LoadAscendingKeys(oldLk);
        for (int i = 0; i < oldCount; i++) aff.Add(_ascend[i].Key);
        int linkCount = LoadAscendingKeys(lk);
        for (int i = 0; i < linkCount; i++) aff.Add(_ascend[i].Key);
        oldCount = LoadAscendingKeys(oldLk);
        for (int i = 0; i < oldCount; i++)
        {
            int cl = _ascend[i].Key;
            oldLk.TryGetValue(cl, out var a);
            lk.TryGetValue(cl, out var b);
            if (!SameIntLists(a, b)) rebake.Add(cl);
        }

        LinkTiles = lk;
        int rebakeCount = LoadAscending(rebake);
        for (int i = 0; i < rebakeCount; i++)
        {
            int cl = _ascend[i].Key;
            BakeTile(this, nav, cl, lk, keep: !own.Contains(cl) || (same != null && same.Contains(cl)));
        }

        int affCount = LoadAscending(aff);
        for (int i = 0; i < affCount; i++) Blocks[_ascend[i].Key] = BuildBlock(this, nav, _ascend[i].Key);
        FinishGraph(this, links);
    }

    private int LoadAscending(HashSet<int> source)
    {
        int n = source.Count;
        StableOrder.Ensure(ref _ascend, n);
        int i = 0;
        foreach (int v in source)
        {
            _ascend[i] = new StableInt(v, i);
            i++;
        }

        StableOrder.Sort(_ascend, n);
        return n;
    }

    private int LoadAscendingKeys(Dictionary<int, List<int>> source)
    {
        int n = source.Count;
        StableOrder.Ensure(ref _ascend, n);
        int i = 0;
        foreach (int v in source.Keys)
        {
            _ascend[i] = new StableInt(v, i);
            i++;
        }

        StableOrder.Sort(_ascend, n);
        return n;
    }

    private static bool SameCells(int[]? a, int[]? b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }

        return true;
    }

    private static bool SameCosts(Fix64[]? a, Fix64[]? b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }

        return true;
    }

    private static bool SameIntLists(List<int>? a, List<int>? b)
    {
        if (a == null || b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }

        return true;
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
        int navCellsPerTile = g.NavCellsPerTile, navTilesPerSide = g.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide, cx = cl % navTilesPerSide;
        if (cx >= navTilesPerSide - 1) { g.BorderE[cl] = Array.Empty<int>(); return; }
        int x0 = (cx + 1) * navCellsPerTile - 1, ys = cl / navTilesPerSide * navCellsPerTile;
        g.BorderE[cl] = ScanBorder(nav.Passable, g.MaxEntranceWidthNavCells, k => (ys + k) * navCellsPerSide + x0, k => (ys + k) * navCellsPerSide + x0 + 1, navCellsPerTile);
    }

    private static void ScanS(HpaGraph g, NavContext nav, int cl)
    {
        int navCellsPerTile = g.NavCellsPerTile, navTilesPerSide = g.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide, cy = cl / navTilesPerSide;
        if (cy >= navTilesPerSide - 1) { g.BorderS[cl] = Array.Empty<int>(); return; }
        int y0 = (cy + 1) * navCellsPerTile - 1, xs = cl % navTilesPerSide * navCellsPerTile;
        g.BorderS[cl] = ScanBorder(nav.Passable, g.MaxEntranceWidthNavCells, k => y0 * navCellsPerSide + xs + k, k => (y0 + 1) * navCellsPerSide + xs + k, navCellsPerTile);
    }

    // 桥面跨界入口(仅桥面↔桥面;桥头 portal 的跨层边在 BakeTile 的 xl 里)
    private static void UpScan(HpaGraph g, NavContext nav, int cl, bool east)
    {
        int navCellsPerTile = g.NavCellsPerTile, navTilesPerSide = g.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide;
        int cx = cl % navTilesPerSide, cy = cl / navTilesPerSide;
        int nb = east ? (cx < navTilesPerSide - 1 ? cl + 1 : -1) : (cy < navTilesPerSide - 1 ? cl + navTilesPerSide : -1);
        var list = new List<int>();
        var costs = new List<Fix64>();
        if (nb >= 0 && AnyUp(nav, cl, navCellsPerTile) && AnyUp(nav, nb, navCellsPerTile))
        {
            // 与 scanBorder 同一 run 逻辑,目标格函数映射到桥面可走格
            int Ga(int k) => east ? (cy * navCellsPerTile + k) * navCellsPerSide + cx * navCellsPerTile + navCellsPerTile - 1 : (cy * navCellsPerTile + navCellsPerTile - 1) * navCellsPerSide + cx * navCellsPerTile + k;
            var pairs = ScanBorder(nav.UpPass, g.MaxEntranceWidthNavCells, Ga, k => Ga(k) + (east ? 1 : navCellsPerSide), navCellsPerTile);
            for (int p = 0; p < pairs.Length; p += 2)
            {
                list.Add(g.NavCellTotal + pairs[p]);
                list.Add(g.NavCellTotal + pairs[p + 1]);
                costs.Add((nav.UpCost[pairs[p]] + nav.UpCost[pairs[p + 1]]) * Fix64.HalfValue);
            }
        }

        if (east) { g.UpE[cl] = list.ToArray(); g.UpECost[cl] = costs.ToArray(); }
        else { g.UpS[cl] = list.ToArray(); g.UpSCost[cl] = costs.ToArray(); }
    }

    // 该 tile 是否有桥面可走格
    private static bool AnyUp(NavContext nav, int tileId, int tileSize)
    {
        int navCellsPerSide = nav.NavCellsPerSide;
        int c = navCellsPerSide / tileSize;
        int ox = tileId % c * tileSize, oy = tileId / c * tileSize;
        for (int y = 0; y < tileSize; y++)
        {
            for (int x = 0; x < tileSize; x++)
            {
                if (nav.UpPass[(oy + y) * navCellsPerSide + ox + x] != 0) return true;
            }
        }

        return false;
    }

    // ── tile 烘焙 ───────────────────────────────────────────
    private static Dictionary<int, List<int>> LinksByTile(NavLinkSet? links, int navCellsPerSide, int navCellsPerTile, int navTilesPerSide)
    {
        var m = new Dictionary<int, List<int>>();
        if (links != null)
        {
            for (int e = 0; e < links.Count; e++)
            {
                foreach (var cell in new[] { links.From[e], links.To[e] })
                {
                    int cl = NavGridSteps.TileOf(cell, navCellsPerSide, navCellsPerTile, navTilesPerSide);
                    if (!m.TryGetValue(cl, out var l)) m[cl] = l = new List<int>();
                    l.Add(cell);
                }
            }
        }

        return m;
    }

    private static void BakeTile(HpaGraph g, NavContext nav, int cl, Dictionary<int, List<int>> lk, bool keep)
    {
        int navCellsPerTile = g.NavCellsPerTile, navTilesPerSide = g.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide, n2 = navCellsPerSide * navCellsPerSide;
        int cx = cl % navTilesPerSide, cy = cl / navTilesPerSide;
        var cells = new List<int>();
        var seen = new HashSet<int>();
        void Add(int cell) { if (seen.Add(cell)) cells.Add(cell); }
        void Each(int[] b, int side) { for (int k = side; k < b.Length; k += 2) Add(b[k]); }

        Each(g.BorderE[cl], 0);
        if (cx > 0) Each(g.BorderE[cl - 1], 1);
        Each(g.BorderS[cl], 0);
        if (cy > 0) Each(g.BorderS[cl - navTilesPerSide], 1);
        if (lk.TryGetValue(cl, out var linkCells)) foreach (var cell in linkCells) Add(cell);

        // 桥面节点:跨界入口 + portal 跨层对
        var up = new List<int>();
        var uSeen = new HashSet<int>();
        var xl = new List<int>();
        void UAdd(int key) { if (uSeen.Add(key)) up.Add(key); }
        void UEach(int[] b, int side) { for (int k = side; k < b.Length; k += 2) UAdd(b[k]); }
        if (AnyUp(nav, cl, navCellsPerTile))
        {
            UEach(g.UpE[cl], 0);
            if (cx > 0) UEach(g.UpE[cl - 1], 1);
            UEach(g.UpS[cl], 0);
            if (cy > 0) UEach(g.UpS[cl - navTilesPerSide], 1);
            for (int i = 0; i < n2; i++)
            {
                if (nav.Portal[i] == 0) continue;
                int px = i % navCellsPerSide, py = i / navCellsPerSide;
                if (px / navCellsPerTile != cx || py / navCellsPerTile != cy) continue;
                if (nav.Passable[i] == 0) continue;
                Add(i);
                UAdd(n2 + i);
                xl.Add(i);
                xl.Add(n2 + i);
            }
        }

        // 簇内边:逐节点对(i<j)的簇内 Dijkstra(只读到 j 的距离即停)。
        // keep(节点集未变的干净簇/同内容 tile):旧 Dijkstra 距离逐位复用——旧表与新的节点集
        // 前缀一致时,同源同内积的距离不变(参考实现 bakeCluster 的 oldIdx/oldD 复用)。
        var intra = new List<int>();
        var intraDist = new List<Fix64>();
        int gCount = cells.Count;
        Dictionary<int, int>? oldIdx = null;
        Dictionary<long, Fix64>? oldDist = null;
        if (keep && g.Cells[cl] != null)
        {
            var oc = g.Cells[cl];
            var oi = g.Intra[cl];
            var od = g.IntraDist[cl];
            oldIdx = new Dictionary<int, int>();
            for (int k = 0; k < oc.Length && oc[k] < n2; k++) oldIdx[oc[k]] = k;
            oldDist = new Dictionary<long, Fix64>();
            for (int k = 0, d = 0; k < oi.Length; k += 2, d++) oldDist[(long)oi[k] * 65536 + oi[k + 1]] = od[d];
        }

        bool prepared = false;
        for (int i = 0; i < gCount; i++)
        {
            if (oldIdx != null && oldIdx.TryGetValue(cells[i], out int oiIdx))
            {
                bool ok = true;
                for (int j = i + 1; j < gCount && ok; j++)
                {
                    ok = oldIdx.TryGetValue(cells[j], out int oj) && oj > oiIdx;
                }

                if (ok)
                {
                    for (int j = i + 1; j < gCount; j++)
                    {
                        if (oldDist!.TryGetValue((long)oiIdx * 65536 + oldIdx[cells[j]], out Fix64 d))
                        {
                            intra.Add(i);
                            intra.Add(j);
                            intraDist.Add(d);
                        }
                    }

                    continue;
                }
            }

            if (!prepared)
            {
                PrepareLocal(g, nav, cl);
                prepared = true;
            }

            int need = 0;
            for (int j = i + 1; j < gCount; j++)
            {
                int l = NavGridSteps.LocalIndex(cells[j], navCellsPerSide, navCellsPerTile);
                if (g._localWant[l] == 0) { g._localWant[l] = 1; need++; }
            }

            if (need > 0) LocalDijkstra(g, nav, NavGridSteps.LocalIndex(cells[i], navCellsPerSide, navCellsPerTile), need);
            for (int j = i + 1; j < gCount; j++) g._localWant[NavGridSteps.LocalIndex(cells[j], navCellsPerSide, navCellsPerTile)] = 0;
            for (int j = i + 1; j < gCount; j++)
            {
                var d = g._dist[NavGridSteps.LocalIndex(cells[j], navCellsPerSide, navCellsPerTile)];
                if (d < Fix64.MaxValue / 4)
                {
                    intra.Add(i);
                    intra.Add(j);
                    intraDist.Add(d);
                }
            }
        }

        // 桥面节点的簇内边(桥面 tile 内 8 连通 Dijkstra)
        if (up.Count > 0)
        {
            for (int i = 0; i < up.Count; i++)
            {
                UpperDijkstra(g, nav, cl, LocalOf(up[i] - n2, navCellsPerSide, navCellsPerTile));
                for (int j = i + 1; j < up.Count; j++)
                {
                    var d = g._dist[LocalOf(up[j] - n2, navCellsPerSide, navCellsPerTile)];
                    if (d < Fix64.MaxValue / 4)
                    {
                        intra.Add(gCount + i); intra.Add(gCount + j);
                        intraDist.Add(d);
                    }
                }
            }
        }

        var joined = new int[cells.Count + up.Count];
        for (int i = 0; i < cells.Count; i++) joined[i] = cells[i];
        for (int i = 0; i < up.Count; i++) joined[cells.Count + i] = up[i];
        g.Cells[cl] = joined;
        g.Intra[cl] = intra.ToArray();
        g.IntraDist[cl] = intraDist.ToArray();
        g.CrossLayer[cl] = xl.ToArray();
    }

    private static int LocalOf(int cell, int navCellsPerSide, int navCellsPerTile) => NavGridSteps.LocalIndex(cell, navCellsPerSide, navCellsPerTile);

    // 簇内 8 连通图的一次性准备(目标 + 步代价)
    private static void PrepareLocal(HpaGraph g, NavContext nav, int cl)
    {
        int navCellsPerTile = g.NavCellsPerTile, navTilesPerSide = g.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide;
        int x0 = cl % navTilesPerSide * navCellsPerTile, y0 = cl / navTilesPerSide * navCellsPerTile;
        for (int ly = 0; ly < navCellsPerTile; ly++)
        {
            for (int lx = 0; lx < navCellsPerTile; lx++)
            {
                int l = ly * navCellsPerTile + lx, o = l * 8, ux = x0 + lx, uy = y0 + ly, u = uy * navCellsPerSide + ux;
                for (int d = 0; d < 8; d++)
                {
                    int nx = lx + NavGridSteps.DX[d], ny = ly + NavGridSteps.DY[d];
                    if (nx < 0 || ny < 0 || nx >= navCellsPerTile || ny >= navCellsPerTile || !NavGridSteps.CanStep(nav.Passable, navCellsPerSide, ux, uy, d))
                    {
                        g._localNb[o + d] = -1;
                        continue;
                    }

                    g._localNb[o + d] = ny * navCellsPerTile + nx;
                    g._localW[o + d] = NavGridSteps.StepCost(nav.Cost, u, (y0 + ny) * navCellsPerSide + x0 + nx, d);
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
        int navCellsPerTile = g.NavCellsPerTile, navCellsPerSide = nav.NavCellsPerSide;
        int x0 = cl % g.NavTilesPerSide * navCellsPerTile, y0 = cl / g.NavTilesPerSide * navCellsPerTile;
        Array.Fill(g._dist, Fix64.MaxValue);
        g.Heap.Clear();
        g._dist[start] = Fix64.Zero;
        g.Heap.Push(Fix64.Zero, start);
        while (g.Heap.Size > 0)
        {
            var k = g.Heap.PeekKey();
            int cell = g.Heap.Pop();
            if (k > g._dist[cell]) continue;
            int x = cell % navCellsPerTile, y = cell / navCellsPerTile;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + NavGridSteps.DX[d], ny = y + NavGridSteps.DY[d];
                if (nx < 0 || ny < 0 || nx >= navCellsPerTile || ny >= navCellsPerTile) continue;
                int v = ny * navCellsPerTile + nx;
                int gCell = (y0 + y) * navCellsPerSide + x0 + x;
                int gCellV = (y0 + ny) * navCellsPerSide + x0 + nx;
                if (nav.UpPass[gCellV] == 0) continue;
                bool diag = NavGridSteps.DX[d] != 0 && NavGridSteps.DY[d] != 0;
                if (diag && (nav.UpPass[(y0 + y) * navCellsPerSide + x0 + nx] == 0 || nav.UpPass[(y0 + ny) * navCellsPerSide + x0 + x] == 0)) continue;
                var nd = k + (nav.UpCost[gCell] + nav.UpCost[gCellV]) * Fix64.HalfValue * (diag ? NavGridSteps.Sqrt2 : Fix64.OneValue);
                if (nd < g._dist[v]) { g._dist[v] = nd; g.Heap.Push(nd, v); }
            }
        }
    }

    // ── 簇块与图收尾 ─────────────────────────────────────────────
    private static HpaTileBlock BuildBlock(HpaGraph g, NavContext nav, int cl)
    {
        int navTilesPerSide = g.NavTilesPerSide;
        int cx = cl % navTilesPerSide, cy = cl / navTilesPerSide;
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
        if (cy > 0) Border(g.BorderS[cl - navTilesPerSide], 1);

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
        if (cy > 0) Up(g.UpS[cl - navTilesPerSide], g.UpSCost[cl - navTilesPerSide], 1);

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
        return new HpaTileBlock
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
    public (int[] NodeCell, byte[] NodeLayer, int[] NodeTile, int[] AdjStart, int[] AdjTo, Fix64[] AdjCost) Flatten()
    {
        var nodeCell = new List<int>();
        var nodeLayer = new List<byte>();
        var nodeTile = new List<int>();
        var adjStart = new List<int> { 0 };
        var adjTo = new List<int>();
        var adjCost = new List<Fix64>();
        for (int cl = 0; cl < Blocks.Length; cl++)
        {
            var b = Blocks[cl];
            for (int i = 0; i < b.Cells.Length; i++)
            {
                int cell = b.Cells[i];
                nodeCell.Add(cell % NavCellTotal);
                nodeLayer.Add((byte)(cell >= NavCellTotal ? 1 : 0));
                nodeTile.Add(cl);
                for (int k = b.AdjStart[i]; k < b.AdjStart[i + 1]; k++)
                {
                    adjTo.Add(b.AdjTo[k] % NavCellTotal);
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

        return (nodeCell.ToArray(), nodeLayer.ToArray(), nodeTile.ToArray(), adjStart.ToArray(), adjTo.ToArray(), adjCost.ToArray());
    }
}

public sealed class HpaTileBlock
{
    public required int[] Cells { get; set; }
    public required Dictionary<int, int> Index { get; set; }
    public required int[] AdjStart { get; set; }
    public required int[] AdjTo { get; set; }
    public required Fix64[] AdjCost { get; set; }
    public required int EdgeCount { get; set; }
}
