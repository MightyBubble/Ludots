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
    public NavMinHeap Heap = new(4096);
    public Fix64[] Dist => _dist;
    public Fix64[] StartDist => _startDist;
    public Fix64[] GoalDist => _goalDist;
    private Fix64[] _dist;
    private Fix64[] _startDist;
    private Fix64[] _goalDist;
    private int[] _localNb = Array.Empty<int>();
    private Fix64[] _localW = Array.Empty<Fix64>();
    private byte[] _localWant = Array.Empty<byte>();
    // Update 的升序缓冲与集合。本图独占:重烘在导航锁内,查询不碰它们。
    // 变体克隆深拷内层数组,所以这里原地复用不会写穿真相。
    private StableInt[] _ascend = Array.Empty<StableInt>();
    private readonly HashSet<int> _own = new();
    private readonly HashSet<int> _aff = new();
    private readonly HashSet<int> _rebake = new();
    private readonly HashSet<int> _east = new();
    private readonly HashSet<int> _south = new();
    private readonly List<int> _border = new();
    private readonly List<int> _held = new();
    private readonly List<Fix64> _upCosts = new();
    private readonly List<int> _dropKeys = new();
    private Dictionary<int, List<int>> _linkAlt = new();
    private Dictionary<int, List<int>>? _linkAltSpare;
    private static readonly Dictionary<int, List<int>> EmptyLinks = new();
    private List<(int To, Fix64 Cost)>[]? _outE;
    private readonly Dictionary<int, int> _oldIdx = new(512);
    private readonly Dictionary<long, Fix64> _oldDist = new(4096);
    private int[] _nE = Array.Empty<int>();
    private int[] _nS = Array.Empty<int>();
    private int[] _nUpE = Array.Empty<int>();
    private int[] _nUpS = Array.Empty<int>();
    private int[] _nUpEc = Array.Empty<int>();
    private int[] _nUpSc = Array.Empty<int>();
    private int[] _nCells = Array.Empty<int>();
    private int[] _nIntra = Array.Empty<int>();
    private int[] _nCross = Array.Empty<int>();
    private readonly List<int> _snap = new(64);
    private readonly List<int> _snapUp = new(64);
    private readonly List<Fix64> _snapCost = new(32);
    private readonly List<int> _bakeCells = new(512);
    private readonly List<int> _bakeUp = new(512);
    private readonly List<int> _bakeXl = new(512);
    private readonly List<int> _bakeIntra = new(4096);
    private readonly List<Fix64> _bakeIntraDist = new(2048);
    private readonly HashSet<int> _bakeSeen = new(512);
    private readonly HashSet<int> _bakeUSeen = new(512);

    /// <summary>认知变体克隆(F02):内层数组、簇块和链接表深拷。增量重烘原地复用缓冲,
    /// 共享内层会被写穿,所以变体必须独占自己的数组。</summary>
    public HpaGraph CloneForVariant()
    {
        var blocks = new HpaTileBlock[Blocks.Length];
        for (int i = 0; i < Blocks.Length; i++)
        {
            var b = Blocks[i];
            blocks[i] = new HpaTileBlock
            {
                Cells = (int[])b.Cells.Clone(),
                Index = CopyIndex(b.Index),
                AdjStart = (int[])b.AdjStart.Clone(),
                AdjTo = (int[])b.AdjTo.Clone(),
                AdjCost = (Fix64[])b.AdjCost.Clone(),
                EdgeCount = b.EdgeCount,
                CellCount = b.CellCount,
            };
        }

        var linkOut = new Dictionary<int, List<(int To, Fix64 Cost)>>(LinkOut.Count);
        foreach (var kv in LinkOut) linkOut[kv.Key] = new List<(int To, Fix64 Cost)>(kv.Value);
        Dictionary<int, List<int>>? linkTiles = null;
        if (LinkTiles != null)
        {
            linkTiles = new Dictionary<int, List<int>>(LinkTiles.Count);
            foreach (var kv in LinkTiles) linkTiles[kv.Key] = new List<int>(kv.Value);
        }

        var copy = new HpaGraph
        {
            NavCellsPerTile = NavCellsPerTile,
            NavTilesPerSide = NavTilesPerSide,
            NavCellTotal = NavCellTotal,
            MaxEntranceWidthNavCells = MaxEntranceWidthNavCells,
            BorderE = CopyJagged(BorderE),
            BorderS = CopyJagged(BorderS),
            UpE = CopyJagged(UpE),
            UpECost = CopyJagged(UpECost),
            UpS = CopyJagged(UpS),
            UpSCost = CopyJagged(UpSCost),
            Cells = CopyJagged(Cells),
            Intra = CopyJagged(Intra),
            IntraDist = CopyJagged(IntraDist),
            CrossLayer = CopyJagged(CrossLayer),
            Blocks = blocks,
            LinkOut = linkOut,
            LinkTiles = linkTiles,
            NodeCount = NodeCount,
            EdgeCount = EdgeCount,
            Heap = new NavMinHeap(4096),
            _dist = new Fix64[_dist.Length],
            _startDist = new Fix64[_startDist.Length],
            _goalDist = new Fix64[_goalDist.Length],
            _localNb = new int[_localNb.Length == 0 ? NavCellsPerTile * NavCellsPerTile * 8 : _localNb.Length],
            _localW = new Fix64[_localW.Length == 0 ? NavCellsPerTile * NavCellsPerTile * 8 : _localW.Length],
            _localWant = new byte[_localWant.Length == 0 ? NavCellsPerTile * NavCellsPerTile : _localWant.Length],
        };
        copy._nE = (int[])_nE.Clone();
        copy._nS = (int[])_nS.Clone();
        copy._nUpE = (int[])_nUpE.Clone();
        copy._nUpS = (int[])_nUpS.Clone();
        copy._nUpEc = (int[])_nUpEc.Clone();
        copy._nUpSc = (int[])_nUpSc.Clone();
        copy._nCells = (int[])_nCells.Clone();
        copy._nIntra = (int[])_nIntra.Clone();
        copy._nCross = (int[])_nCross.Clone();
        copy.PrepareIncrementalScratch(NavTilesPerSide * NavTilesPerSide);
        return copy;
    }

    private static Dictionary<int, int> CopyIndex(Dictionary<int, int> source)
    {
        var copy = new Dictionary<int, int>(512);
        foreach (var kv in source) copy.Add(kv.Key, kv.Value);
        return copy;
    }

    private static int[][] CopyJagged(int[][] src)
    {
        var copy = new int[src.Length][];
        for (int i = 0; i < src.Length; i++) copy[i] = (int[])src[i].Clone();
        return copy;
    }

    private static Fix64[][] CopyJagged(Fix64[][] src)
    {
        var copy = new Fix64[src.Length][];
        for (int i = 0; i < src.Length; i++) copy[i] = (Fix64[])src[i].Clone();
        return copy;
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
        g._nE = new int[navTileCount];
        g._nS = new int[navTileCount];
        g._nUpE = new int[navTileCount];
        g._nUpS = new int[navTileCount];
        g._nUpEc = new int[navTileCount];
        g._nUpSc = new int[navTileCount];
        g._nCells = new int[navTileCount];
        g._nIntra = new int[navTileCount];
        g._nCross = new int[navTileCount];

        for (int cl = 0; cl < navTileCount; cl++) { ScanE(g, nav, cl); ScanS(g, nav, cl); UpScan(g, nav, cl, true); UpScan(g, nav, cl, false); }
        var lk = LinksByTile(nav.Links, navCellsPerSide, navCellsPerTile, navTilesPerSide);
        g.LinkTiles = lk;
        for (int cl = 0; cl < navTileCount; cl++) BakeTile(g, nav, cl, lk, keep: false);
        for (int cl = 0; cl < navTileCount; cl++) g.Blocks[cl] = BuildBlock(g, nav, cl);
        FinishGraph(g, nav.Links);
        FillLinkTiles(g._linkAlt, nav.Links, navCellsPerSide, navCellsPerTile, navTilesPerSide);
        g.PrepareIncrementalScratch(navTileCount);
        return g;
    }

    /// <summary>增量重烘的暂存都在装载期留够:集合槽位、块索引、链接桶,以及字典的键视图对象。
    /// 这些东西第一次用才会分配,不能留到放置 tick。</summary>
    private void PrepareIncrementalScratch(int navTileCount)
    {
        _own.EnsureCapacity(navTileCount);
        _aff.EnsureCapacity(navTileCount);
        _rebake.EnsureCapacity(navTileCount);
        _east.EnsureCapacity(navTileCount);
        _south.EnsureCapacity(navTileCount);
        StableOrder.Ensure(ref _ascend, navTileCount);
        _linkAlt.EnsureCapacity(navTileCount);
        _linkAltSpare ??= new Dictionary<int, List<int>>(navTileCount);
        _linkAltSpare.EnsureCapacity(navTileCount);
        _ = EmptyLinks.Count;
        _ = _linkAlt.Keys.Count;
        _ = _linkAltSpare.Keys.Count;
        if (LinkTiles != null) _ = LinkTiles.Keys.Count;
        for (int cl = 0; cl < Blocks.Length; cl++)
            Blocks[cl].Index.EnsureCapacity(512);
    }

    // ── 增量维护(updateHpa 移植) ─────────────────────────────────────
    /// <summary>脏 tile 的 HPA 增量维护:触到的共享边界各重扫一次,入口表变化的簇(及边界对面
    /// 的邻居)重烘簇内边——入口未变的邻居块仍重建(边代价从本侧格现读)。结果与同一栅格的
    /// 全量构建逐位一致。</summary>
    public void Update(NavContext nav, NavLinkSet? links, IReadOnlyList<int> tiles, HashSet<int>? same)
    {
        int navTilesPerSide = NavTilesPerSide;
        var own = _own;
        var aff = _aff;
        var rebake = _rebake;
        var east = _east;
        var south = _south;
        own.Clear();
        aff.Clear();
        rebake.Clear();
        east.Clear();
        south.Clear();
        // tiles 按具体下标走。foreach 接口会每调用装箱一个枚举器。
        for (int ti = 0; ti < tiles.Count; ti++)
        {
            int t = tiles[ti];
            own.Add(t);
            aff.Add(t);
            rebake.Add(t);
        }
        // 遍历序合同:本方法对无序集合(HashSet/字典键)的枚举一律按簇号升序显式化——
        // 各枚举只做集合并集或逐簇独立读写(Rescan 只加集,BakeTile/BuildBlock 逐簇
        // 写自有槽位),结果与序无关,升序仅为把序写成合同不依赖运行时枚举序。
        for (int ti = 0; ti < tiles.Count; ti++)
        {
            int t = tiles[ti];
            int cx = t % navTilesPerSide, cy = t / navTilesPerSide;
            east.Add(t);
            south.Add(t);
            if (cx > 0) east.Add(t - 1);
            if (cy > 0) south.Add(t - navTilesPerSide);
        }

        int eastCount = LoadAscending(east);
        for (int i = 0; i < eastCount; i++)
        {
            int cl = _ascend[i].Key;
            Rescan(nav, cl, cl % navTilesPerSide < navTilesPerSide - 1 ? cl + 1 : -1, true);
        }

        int southCount = LoadAscending(south);
        for (int i = 0; i < southCount; i++)
        {
            int cl = _ascend[i].Key;
            Rescan(nav, cl, cl / navTilesPerSide < navTilesPerSide - 1 ? cl + navTilesPerSide : -1, false);
        }

        int ownCount = LoadAscending(own);
        for (int i = 0; i < ownCount; i++) aff.Add(_ascend[i].Key);
        var lk = _linkAlt;
        FillLinkTiles(lk, links, nav.NavCellsPerSide, NavCellsPerTile, navTilesPerSide);
        _dropKeys.Clear();
        foreach (var kv in lk)
        {
            if (kv.Value.Count == 0) _dropKeys.Add(kv.Key);
        }

        for (int i = 0; i < _dropKeys.Count; i++) lk.Remove(_dropKeys[i]);
        // 重生成的跳跃链接可能落进干净簇(链接跨 tile)——端点表变化的簇一并重烘
        var oldLk = LinkTiles ?? EmptyLinks;
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
        if (oldLk == EmptyLinks)
        {
            _linkAlt = _linkAltSpare ??= new Dictionary<int, List<int>>(Math.Max(1, NavTilesPerSide * NavTilesPerSide));
        }
        else
        {
            _linkAlt = oldLk;
        }

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

    // 每条触到脏 tile 的边界只重扫一次;入口表未变则对面簇不重烘簇内边。
    private void Rescan(NavContext nav, int cl, int nb, bool isEast)
    {
        CopyPrefix(_snap, isEast ? BorderE[cl] : BorderS[cl], isEast ? _nE[cl] : _nS[cl]);
        CopyPrefix(_snapUp, isEast ? UpE[cl] : UpS[cl], isEast ? _nUpE[cl] : _nUpS[cl]);
        CopyCosts(_snapCost, isEast ? UpECost[cl] : UpSCost[cl], isEast ? _nUpEc[cl] : _nUpSc[cl]);
        if (isEast) ScanE(this, nav, cl);
        else ScanS(this, nav, cl);
        UpScan(this, nav, cl, isEast);
        if (nb < 0) return;
        _aff.Add(cl);
        _aff.Add(nb);
        bool changed = !SamePrefix(_snap, isEast ? BorderE[cl] : BorderS[cl], isEast ? _nE[cl] : _nS[cl])
            || !SamePrefix(_snapUp, isEast ? UpE[cl] : UpS[cl], isEast ? _nUpE[cl] : _nUpS[cl])
            || !SameCostPrefix(_snapCost, isEast ? UpECost[cl] : UpSCost[cl], isEast ? _nUpEc[cl] : _nUpSc[cl]);
        if (changed)
        {
            _rebake.Add(cl);
            _rebake.Add(nb);
        }
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
    /// <summary>cellA(k) = origin + k * stride, cellB(k) = cellA(k) + side。不用委托,扫描不分配。</summary>
    private static void ScanBorder(HpaGraph g, byte[] passable, int maxW, int origin, int stride, int side, int len)
    {
        var output = g._border;
        output.Clear();
        int run = -1;
        for (int k = 0; k <= len; k++)
        {
            int a = origin + k * stride;
            bool open = k < len && passable[a] != 0 && passable[a + side] != 0;
            if (open && run < 0) run = k;
            else if (!open && run >= 0)
            {
                int e = k - 1;
                int ar = origin + run * stride, ae = origin + e * stride;
                if (e - run + 1 > maxW) { output.Add(ar); output.Add(ar + side); output.Add(ae); output.Add(ae + side); }
                else { int p = origin + ((run + e) >> 1) * stride; output.Add(p); output.Add(p + side); }
                run = -1;
            }
        }
    }

    /// <summary>写入已有缓冲的前缀。容量按 slack 在首次分配时留出,入口个数变化不再换数组。</summary>
    private int[] PlaceInts(int[]? slot, int count, int slack)
    {
        if (slot == null || slot.Length < count)
        {
            int cap = Math.Max(count, slack);
            if (slot != null && slot.Length * 2 > cap) cap = slot.Length * 2;
            slot = new int[cap];
        }

        for (int i = 0; i < count; i++) slot[i] = _border[i];
        return slot;
    }

    private int[] PlaceList(int[]? slot, List<int> src, int slack)
    {
        int count = src.Count;
        if (slot == null || slot.Length < count)
        {
            int cap = Math.Max(count, slack);
            if (slot != null && slot.Length * 2 > cap) cap = slot.Length * 2;
            slot = new int[cap];
        }

        for (int i = 0; i < count; i++) slot[i] = src[i];
        return slot;
    }

    private static void ScanE(HpaGraph g, NavContext nav, int cl)
    {
        int navCellsPerTile = g.NavCellsPerTile, navTilesPerSide = g.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide, cx = cl % navTilesPerSide;
        if (cx >= navTilesPerSide - 1) { g.BorderE[cl] = Array.Empty<int>(); g._nE[cl] = 0; return; }
        int x0 = (cx + 1) * navCellsPerTile - 1, ys = cl / navTilesPerSide * navCellsPerTile;
        ScanBorder(g, nav.Passable, g.MaxEntranceWidthNavCells, ys * navCellsPerSide + x0, navCellsPerSide, 1, navCellsPerTile);
        g.BorderE[cl] = g.PlaceInts(g.BorderE[cl], g._border.Count, 64);
        g._nE[cl] = g._border.Count;
    }

    private static void ScanS(HpaGraph g, NavContext nav, int cl)
    {
        int navCellsPerTile = g.NavCellsPerTile, navTilesPerSide = g.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide, cy = cl / navTilesPerSide;
        if (cy >= navTilesPerSide - 1) { g.BorderS[cl] = Array.Empty<int>(); g._nS[cl] = 0; return; }
        int y0 = (cy + 1) * navCellsPerTile - 1, xs = cl % navTilesPerSide * navCellsPerTile;
        ScanBorder(g, nav.Passable, g.MaxEntranceWidthNavCells, y0 * navCellsPerSide + xs, 1, navCellsPerSide, navCellsPerTile);
        g.BorderS[cl] = g.PlaceInts(g.BorderS[cl], g._border.Count, 64);
        g._nS[cl] = g._border.Count;
    }

    // 桥面跨界入口(仅桥面↔桥面;桥头 portal 的跨层边在 BakeTile 的 xl 里)
    private static void UpScan(HpaGraph g, NavContext nav, int cl, bool east)
    {
        int navCellsPerTile = g.NavCellsPerTile, navTilesPerSide = g.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide;
        int cx = cl % navTilesPerSide, cy = cl / navTilesPerSide;
        int nb = east ? (cx < navTilesPerSide - 1 ? cl + 1 : -1) : (cy < navTilesPerSide - 1 ? cl + navTilesPerSide : -1);
        var list = g._border;
        var costs = g._upCosts;
        costs.Clear();
        if (nb >= 0 && AnyUp(nav, cl, navCellsPerTile) && AnyUp(nav, nb, navCellsPerTile))
        {
            // 与 scanBorder 同一 run 逻辑,目标格函数映射到桥面可走格。先把入口对抄走,再复用 _border。
            int origin = east
                ? (cy * navCellsPerTile) * navCellsPerSide + cx * navCellsPerTile + navCellsPerTile - 1
                : (cy * navCellsPerTile + navCellsPerTile - 1) * navCellsPerSide + cx * navCellsPerTile;
            int stride = east ? navCellsPerSide : 1;
            int side = east ? 1 : navCellsPerSide;
            ScanBorder(g, nav.UpPass, g.MaxEntranceWidthNavCells, origin, stride, side, navCellsPerTile);
            var held = g._held;
            held.Clear();
            for (int i = 0; i < g._border.Count; i++) held.Add(g._border[i]);
            list = g._border;
            list.Clear();
            for (int p = 0; p < held.Count; p += 2)
            {
                list.Add(g.NavCellTotal + held[p]);
                list.Add(g.NavCellTotal + held[p + 1]);
                costs.Add((nav.UpCost[held[p]] + nav.UpCost[held[p + 1]]) * Fix64.HalfValue);
            }
        }
        else
        {
            list.Clear();
        }

        if (east)
        {
            g.UpE[cl] = g.PlaceInts(g.UpE[cl], list.Count, 64);
            g._nUpE[cl] = list.Count;
            g.UpECost[cl] = g.PlaceCosts(g.UpECost[cl], costs.Count, 32);
            g._nUpEc[cl] = costs.Count;
        }
        else
        {
            g.UpS[cl] = g.PlaceInts(g.UpS[cl], list.Count, 64);
            g._nUpS[cl] = list.Count;
            g.UpSCost[cl] = g.PlaceCosts(g.UpSCost[cl], costs.Count, 32);
            g._nUpSc[cl] = costs.Count;
        }
    }

    private Fix64[] PlaceCosts(Fix64[]? slot, int count, int slack)
    {
        if (slot == null || slot.Length < count)
        {
            int cap = Math.Max(count, slack);
            if (slot != null && slot.Length * 2 > cap) cap = slot.Length * 2;
            slot = new Fix64[cap];
        }

        for (int i = 0; i < count; i++) slot[i] = _upCosts[i];
        return slot;
    }

    private Fix64[] PlaceDist(Fix64[]? slot, List<Fix64> src, int slack)
    {
        int count = src.Count;
        if (slot == null || slot.Length < count)
        {
            int cap = Math.Max(count, slack);
            if (slot != null && slot.Length * 2 > cap) cap = slot.Length * 2;
            slot = new Fix64[cap];
        }

        for (int i = 0; i < count; i++) slot[i] = src[i];
        return slot;
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
        FillLinkTiles(m, links, navCellsPerSide, navCellsPerTile, navTilesPerSide);
        return m;
    }

    private static void FillLinkTiles(Dictionary<int, List<int>> m, NavLinkSet? links, int navCellsPerSide, int navCellsPerTile, int navTilesPerSide)
    {
        foreach (var list in m.Values) list.Clear();
        if (links != null)
        {
            for (int e = 0; e < links.Count; e++)
            {
                AddLinkCell(m, links.From[e], navCellsPerSide, navCellsPerTile, navTilesPerSide);
                AddLinkCell(m, links.To[e], navCellsPerSide, navCellsPerTile, navTilesPerSide);
            }
        }
    }

    private static void AddLinkCell(Dictionary<int, List<int>> m, int cell, int navCellsPerSide, int navCellsPerTile, int navTilesPerSide)
    {
        int cl = NavGridSteps.TileOf(cell, navCellsPerSide, navCellsPerTile, navTilesPerSide);
        if (!m.TryGetValue(cl, out var l)) m[cl] = l = new List<int>(8);
        l.Add(cell);
    }

    private static void BakeTile(HpaGraph g, NavContext nav, int cl, Dictionary<int, List<int>> lk, bool keep)
    {
        int navCellsPerTile = g.NavCellsPerTile, navTilesPerSide = g.NavTilesPerSide, navCellsPerSide = nav.NavCellsPerSide, n2 = navCellsPerSide * navCellsPerSide;
        int cx = cl % navTilesPerSide, cy = cl / navTilesPerSide;
        var cells = g._bakeCells;
        var seen = g._bakeSeen;
        cells.Clear();
        seen.Clear();
        AddBorder(g, g.BorderE[cl], g._nE[cl], 0);
        if (cx > 0) AddBorder(g, g.BorderE[cl - 1], g._nE[cl - 1], 1);
        AddBorder(g, g.BorderS[cl], g._nS[cl], 0);
        if (cy > 0) AddBorder(g, g.BorderS[cl - navTilesPerSide], g._nS[cl - navTilesPerSide], 1);
        if (lk.TryGetValue(cl, out var linkCells))
        {
            for (int i = 0; i < linkCells.Count; i++) AddNode(g, linkCells[i]);
        }

        // 桥面节点:跨界入口 + portal 跨层对
        var up = g._bakeUp;
        var uSeen = g._bakeUSeen;
        var xl = g._bakeXl;
        up.Clear();
        uSeen.Clear();
        xl.Clear();
        if (AnyUp(nav, cl, navCellsPerTile))
        {
            AddUp(g, g.UpE[cl], g._nUpE[cl], 0);
            if (cx > 0) AddUp(g, g.UpE[cl - 1], g._nUpE[cl - 1], 1);
            AddUp(g, g.UpS[cl], g._nUpS[cl], 0);
            if (cy > 0) AddUp(g, g.UpS[cl - navTilesPerSide], g._nUpS[cl - navTilesPerSide], 1);
            for (int i = 0; i < n2; i++)
            {
                if (nav.Portal[i] == 0) continue;
                int px = i % navCellsPerSide, py = i / navCellsPerSide;
                if (px / navCellsPerTile != cx || py / navCellsPerTile != cy) continue;
                if (nav.Passable[i] == 0) continue;
                AddNode(g, i);
                AddUpNode(g, n2 + i);
                xl.Add(i);
                xl.Add(n2 + i);
            }
        }

        // 簇内边:逐节点对(i<j)的簇内 Dijkstra(只读到 j 的距离即停)。
        // keep(节点集未变的干净簇/同内容 tile):旧 Dijkstra 距离逐位复用——旧表与新的节点集
        // 前缀一致时,同源同内积的距离不变(参考实现 bakeCluster 的 oldIdx/oldD 复用)。
        var intra = g._bakeIntra;
        var intraDist = g._bakeIntraDist;
        intra.Clear();
        intraDist.Clear();
        int gCount = cells.Count;
        var oldIdx = g._oldIdx;
        var oldDist = g._oldDist;
        oldIdx.Clear();
        oldDist.Clear();
        bool reuseIntra = keep && g._nCells[cl] > 0;
        if (reuseIntra)
        {
            var oc = g.Cells[cl];
            var oi = g.Intra[cl];
            var od = g.IntraDist[cl];
            int cellCount = g._nCells[cl];
            int intraCount = g._nIntra[cl];
            for (int k = 0; k < cellCount && oc[k] < n2; k++) oldIdx[oc[k]] = k;
            for (int k = 0, d = 0; k < intraCount; k += 2, d++) oldDist[(long)oi[k] * 65536 + oi[k + 1]] = od[d];
        }

        bool prepared = false;
        for (int i = 0; i < gCount; i++)
        {
            if (reuseIntra && oldIdx.TryGetValue(cells[i], out int oiIdx))
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

        int joinedCount = cells.Count + up.Count;
        var joined = g.PlaceList(g.Cells[cl], cells, Math.Max(512, joinedCount));
        if (joined.Length < joinedCount)
        {
            joined = g.PlaceList(null, cells, joinedCount);
        }

        for (int i = 0; i < up.Count; i++) joined[cells.Count + i] = up[i];
        g.Cells[cl] = joined;
        g._nCells[cl] = joinedCount;
        g.Intra[cl] = g.PlaceList(g.Intra[cl], intra, 4096);
        g._nIntra[cl] = intra.Count;
        g.IntraDist[cl] = g.PlaceDist(g.IntraDist[cl], intraDist, 2048);
        g.CrossLayer[cl] = g.PlaceList(g.CrossLayer[cl], xl, 512);
        g._nCross[cl] = xl.Count;
    }

    private static void AddNode(HpaGraph g, int cell)
    {
        if (g._bakeSeen.Add(cell)) g._bakeCells.Add(cell);
    }

    private static void AddUpNode(HpaGraph g, int key)
    {
        if (g._bakeUSeen.Add(key)) g._bakeUp.Add(key);
    }

    private static void AddBorder(HpaGraph g, int[] border, int count, int side)
    {
        for (int k = side; k < count; k += 2) AddNode(g, border[k]);
    }

    private static void AddUp(HpaGraph g, int[] border, int count, int side)
    {
        for (int k = side; k < count; k += 2) AddUpNode(g, border[k]);
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
        int count = g._nCells[cl];
        var block = g.Blocks[cl];
        var index = block != null ? block.Index : new Dictionary<int, int>();
        if (block != null) index.Clear();
        var outE = g.OutScratch(count);
        for (int i = 0; i < count; i++) index[list[i]] = i;

        AddBlockBorder(g, nav, outE, index, g.BorderE[cl], g._nE[cl], 0);
        if (cx > 0) AddBlockBorder(g, nav, outE, index, g.BorderE[cl - 1], g._nE[cl - 1], 1);
        AddBlockBorder(g, nav, outE, index, g.BorderS[cl], g._nS[cl], 0);
        if (cy > 0) AddBlockBorder(g, nav, outE, index, g.BorderS[cl - navTilesPerSide], g._nS[cl - navTilesPerSide], 1);

        AddBlockUp(outE, index, g.UpE[cl], g.UpECost[cl], g._nUpE[cl], 0);
        if (cx > 0) AddBlockUp(outE, index, g.UpE[cl - 1], g.UpECost[cl - 1], g._nUpE[cl - 1], 1);
        AddBlockUp(outE, index, g.UpS[cl], g.UpSCost[cl], g._nUpS[cl], 0);
        if (cy > 0) AddBlockUp(outE, index, g.UpS[cl - navTilesPerSide], g.UpSCost[cl - navTilesPerSide], g._nUpS[cl - navTilesPerSide], 1);

        var xl = g.CrossLayer[cl];
        for (int k = 0; k < g._nCross[cl]; k += 2)
        {
            outE[index[xl[k]]].Add((xl[k + 1], Fix64.Zero));
            outE[index[xl[k + 1]]].Add((xl[k], Fix64.Zero));
        }

        var intra = g.Intra[cl];
        var intraDist = g.IntraDist[cl];
        for (int k = 0; k < g._nIntra[cl]; k += 2)
        {
            int i = intra[k], j = intra[k + 1];
            var d = intraDist[k / 2];
            outE[i].Add((list[j], d));
            outE[j].Add((list[i], d));
        }

        int edges = g._nE[cl] / 2 + g._nS[cl] / 2 + g._nIntra[cl] / 2
            + g._nUpE[cl] / 2 + g._nUpS[cl] / 2 + g._nCross[cl] / 2;
        int edgeSlots = 0;
        for (int i = 0; i < count; i++) edgeSlots += outE[i].Count;
        if (block != null && block.AdjStart.Length >= count + 1 && block.AdjTo.Length >= edgeSlots && block.AdjCost.Length >= edgeSlots)
        {
            var adjStart = block.AdjStart;
            var adjTo = block.AdjTo;
            var adjCost = block.AdjCost;
            adjStart[0] = 0;
            for (int i = 0; i < count; i++) adjStart[i + 1] = adjStart[i] + outE[i].Count;
            for (int i = 0, p = 0; i < count; i++)
            {
                var row = outE[i];
                for (int k = 0; k < row.Count; k++)
                {
                    adjTo[p] = row[k].To;
                    adjCost[p] = row[k].Cost;
                    p++;
                }
            }

            block.Cells = list;
            block.CellCount = count;
            block.EdgeCount = edges;
            return block;
        }

        int startCap = Math.Max(count + 1, 64);
        int edgeCap = Math.Max(edgeSlots, 4096);
        var freshStart = new int[startCap];
        for (int i = 0; i < count; i++) freshStart[i + 1] = freshStart[i] + outE[i].Count;
        var freshTo = new int[edgeCap];
        var freshCost = new Fix64[edgeCap];
        for (int i = 0, p = 0; i < count; i++)
        {
            var row = outE[i];
            for (int k = 0; k < row.Count; k++)
            {
                freshTo[p] = row[k].To;
                freshCost[p] = row[k].Cost;
                p++;
            }
        }

        return new HpaTileBlock
        {
            Cells = list,
            AdjStart = freshStart,
            AdjTo = freshTo,
            AdjCost = freshCost,
            EdgeCount = edges,
            CellCount = count,
            Index = index,
        };
    }

    private static void AddBlockBorder(
        HpaGraph g, NavContext nav, List<(int To, Fix64 Cost)>[] outE, Dictionary<int, int> index, int[] border, int count, int own)
    {
        for (int k = 0; k < count; k += 2)
        {
            outE[index[border[k + own]]].Add((border[k + 1 - own], (nav.Cost[border[k]] + nav.Cost[border[k + 1]]) * Fix64.HalfValue));
        }
    }

    private static void AddBlockUp(List<(int To, Fix64 Cost)>[] outE, Dictionary<int, int> index, int[] border, Fix64[] costs, int count, int own)
    {
        for (int k = 0; k < count; k += 2)
        {
            outE[index[border[k + own]]].Add((border[k + 1 - own], costs[k / 2]));
        }
    }

    private static void CopyPrefix(List<int> dst, int[]? src, int count)
    {
        dst.Clear();
        if (src == null) return;
        for (int i = 0; i < count; i++) dst.Add(src[i]);
    }

    private static void CopyCosts(List<Fix64> dst, Fix64[]? src, int count)
    {
        dst.Clear();
        if (src == null) return;
        for (int i = 0; i < count; i++) dst.Add(src[i]);
    }

    private static bool SamePrefix(List<int> prev, int[]? now, int count)
    {
        if (prev.Count != count) return false;
        if (now == null) return count == 0;
        for (int i = 0; i < count; i++)
        {
            if (prev[i] != now[i]) return false;
        }

        return true;
    }

    private static bool SameCostPrefix(List<Fix64> prev, Fix64[]? now, int count)
    {
        if (prev.Count != count) return false;
        if (now == null) return count == 0;
        for (int i = 0; i < count; i++)
        {
            if (prev[i] != now[i]) return false;
        }

        return true;
    }

    private List<(int To, Fix64 Cost)>[] OutScratch(int count)
    {
        var cur = _outE;
        if (cur == null || cur.Length < count)
        {
            var grown = new List<(int To, Fix64 Cost)>[Math.Max(count, 8)];
            int old = cur?.Length ?? 0;
            for (int i = 0; i < old; i++) grown[i] = cur![i];
            for (int i = old; i < grown.Length; i++) grown[i] = new List<(int To, Fix64 Cost)>(32);
            _outE = cur = grown;
        }

        for (int i = 0; i < count; i++) cur[i].Clear();
        return cur;
    }

    private static void FinishGraph(HpaGraph g, NavLinkSet? links)
    {
        var lo = g.LinkOut;
        foreach (var list in lo.Values) list.Clear();
        if (links != null)
        {
            for (int e = 0; e < links.Count; e++)
            {
                if (!lo.TryGetValue(links.From[e], out var a)) lo[links.From[e]] = a = new List<(int, Fix64)>(4);
                a.Add((links.To[e], links.Cost[e]));
            }
        }

        int nodes = 0, edges = links?.Count ?? 0;
        foreach (var b in g.Blocks) { nodes += b.CellCount; edges += b.EdgeCount; }
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
            for (int i = 0; i < b.CellCount; i++)
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
    public int CellCount { get; set; }
}
