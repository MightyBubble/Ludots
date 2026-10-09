using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>增量重烘的烘焙输入(会话装载期的同源数据:配置 / 高度场 / 桥面 / 跳跃候选;
/// 区域与阻挡栅格的活数据在 CrowdStructuresStore 上,快照面经 SnapshotSurface 取)。</summary>
public sealed record CrowdRebakeSources(
    CrowdSimulationRuntimeConfig Config,
    NavHeightField HeightS,
    DeckSurface Deck,
    NavSurfaceJumpCandidate[] JumpCandidates);

/// <summary>单上下文增量重烘的脏 tile 集与摘要(真值逐字段对拍的数据面)。</summary>
public sealed class CrowdNavRebakeContextResult
{
    public required int NavId { get; init; }
    /// <summary>脏 tile(tile id 升序);空表 = 该上下文什么都看不到,什么都不失效。</summary>
    public required List<int> DirtyTiles { get; init; }
    /// <summary>0 = 无变化,1 = 仅代价,2 = 可走位翻转。</summary>
    public required int Kind { get; init; }
}

/// <summary>
/// 结构变更后的导航增量重烘(nav.js rebakeNavRects + finish 移植):
/// 变更格矩形 → 逐上下文重分类(0 = 无变化 / 1 = 仅代价 / 2 = 可走位翻转)→
/// 可走位翻转时净空腐蚀矩形重算、触到的 tile 重烘(内容键缓存命中即复用)、
/// 连通域与跳跃链接重标、HPA 增量维护;仅代价时 tile 多边形与 HPA 块代价现读重算。
/// 与同栅格的全量烘焙按覆盖字段逐格一致(一致性检查见 VerifyAgainstFullBake)。
/// </summary>
public static class CrowdNavRebake
{
    public const byte KindNone = 0;
    public const byte KindCost = 1;
    public const byte KindPassable = 2;

    /// <summary>全部上下文对同一变更矩形跑一遍增量重烘(rebakeContexts 移植)。</summary>
    public static List<CrowdNavRebakeContextResult> RebakeContexts(
        IEnumerable<NavContext> navs,
        CrowdRebakeSources sources,
        CrowdStructuresStore store,
        (int X0, int Y0, int X1, int Y1) rect,
        NavTileCache cache)
    {
        var results = new List<CrowdNavRebakeContextResult>();
        foreach (var nav in navs)
        {
            var tiles = RebakeNav(nav, sources, store, rect, cache);
            results.Add(new CrowdNavRebakeContextResult { NavId = nav.Id, DirtyTiles = tiles, Kind = nav.LastRebake });
        }

        return results;
    }

    /// <summary>单上下文增量重烘;返回脏 tile(tile id 升序,空 = 无变化)。</summary>
    public static List<int> RebakeNav(
        NavContext nav,
        CrowdRebakeSources sources,
        CrowdStructuresStore store,
        (int X0, int Y0, int X1, int Y1) rect,
        NavTileCache cache)
    {
        var config = sources.Config;
        int n = nav.CellCount, t = config.Hpa.ClusterSize, c = n / t;
        int erosion = nav.ClearanceCells - 1;
        int kind = Classify(nav, sources, store, rect);
        var grown = Grow(rect, erosion, n);
        List<int> tiles;
        if (kind == KindPassable)
        {
            ErodeRect(nav.Walk, n, nav.ClearanceCells, grown, nav.Passable);
            var dirty = TilesOf(grown, t, c);
            // 重烘后拿到同一内容条目(缓存命中)的 tile:HPA 簇内距离逐位复用
            var prev = new NavTileEntry?[dirty.Count];
            for (int i = 0; i < dirty.Count; i++) prev[i] = nav.Tiles![dirty[i]];
            BakeTiles(nav, dirty, cache, store.Area);
            var same = new HashSet<int>();
            for (int i = 0; i < dirty.Count; i++)
            {
                if (ReferenceEquals(nav.Tiles![dirty[i]], prev[i])) same.Add(dirty[i]);
            }

            FinishPassable(nav, sources, store, dirty, grown, same);
            tiles = dirty;
            nav.ChangedTiles = dirty.Where(x => !same.Contains(x)).ToList();
            nav.LastRebake = KindPassable;
        }
        else
        {
            var dirty = TilesOf(rect, t, c);
            bool meshChanged = BakeTiles(nav, dirty, cache, store.Area);
            if (kind == KindNone && !meshChanged)
            {
                nav.LastRebake = KindNone;
                nav.ChangedTiles = new List<int>();
                return new List<int>();
            }

            nav.Hpa!.Update(nav, nav.Links, dirty, same: null);
            tiles = dirty;
            nav.ChangedTiles = dirty;
            nav.LastRebake = KindCost;
        }

        nav.Version++;
        nav.InvalidateReach();
        return tiles;
    }

    /// <summary>变更矩形内重分类(参考 classify):0 = 本上下文看不到任何变化,
    /// 1 = 只有代价变(道路 / 泛洪 / 火场,仍可跨),2 = 有可走位翻转。</summary>
    private static int Classify(
        NavContext nav,
        CrowdRebakeSources sources,
        CrowdStructuresStore store,
        (int X0, int Y0, int X1, int Y1) rect)
    {
        var config = sources.Config;
        int n = nav.CellCount;
        var agent = config.AgentTypes[nav.LayerIndex];
        var profile = config.NavProfiles[agent.NavProfileIndex];
        Fix64 maxSlope = profile.MaxSlopeDeg is { } deg ? Fix64Math.Tan(deg * Fix64.Deg2Rad) : Fix64.MaxValue;
        var row = agent.CostByArea;
        var slopeFree = config.NavAreas;
        var area = store.Area;
        var blocked = store.Blocked;
        var slope = sources.HeightS.Slope;
        var cost = nav.Cost;
        var walk = nav.Walk;
        int kind = KindNone;
        for (int y = rect.Y0; y < rect.Y1; y++)
        {
            for (int x = rect.X0; x < rect.X1; x++)
            {
                int i = y * n + x;
                int a = area[i];
                Fix64 cell = row[a];
                if (cell > Fix64.Zero && !slopeFree[a].SlopeFree && slope[i] > maxSlope)
                {
                    cell = Fix64.Zero;
                }

                if (cost[i] != cell)
                {
                    cost[i] = cell;
                    if (kind == KindNone) kind = KindCost;
                }

                byte w = cell > Fix64.Zero && blocked[i] == 0 ? (byte)1 : (byte)0;
                if (walk[i] != w)
                {
                    walk[i] = w;
                    kind = KindPassable;
                }
            }
        }

        return kind;
    }

    /// <summary>格矩形外扩 k 格(夹到网格)。</summary>
    private static (int X0, int Y0, int X1, int Y1) Grow((int X0, int Y0, int X1, int Y1) rect, int k, int n) => (
        Math.Max(0, rect.X0 - k),
        Math.Max(0, rect.Y0 - k),
        Math.Min(n, rect.X1 + k),
        Math.Min(n, rect.Y1 + k));

    /// <summary>格矩形触到的 tile id(升序去重)。</summary>
    internal static List<int> TilesOf((int X0, int Y0, int X1, int Y1) rect, int t, int c)
    {
        var tiles = new List<int>();
        var seen = new HashSet<int>();
        for (int ty = rect.Y0 / t; ty <= (rect.Y1 - 1) / t; ty++)
        {
            for (int tx = rect.X0 / t; tx <= (rect.X1 - 1) / t; tx++)
            {
                if (seen.Add(ty * c + tx)) tiles.Add(ty * c + tx);
            }
        }

        return tiles;
    }

    /// <summary>可走位翻转后的净空腐蚀矩形重算(erodeRect 移植):只写矩形内;
    /// 切比雪夫可分离两遍,行/列连续段计数,读入需外扩腐蚀半径。</summary>
    internal static void ErodeRect(byte[] walk, int n, int clearance, (int X0, int Y0, int X1, int Y1) rect, byte[] output)
    {
        int r = clearance - 1;
        if (r <= 0)
        {
            for (int y = rect.Y0; y < rect.Y1; y++)
            {
                for (int x = rect.X0; x < rect.X1; x++)
                {
                    output[y * n + x] = walk[y * n + x];
                }
            }

            return;
        }

        int need = 2 * r + 1, w = rect.X1 - rect.X0;
        int ya = Math.Max(0, rect.Y0 - r), yb = Math.Min(n, rect.Y1 + r);
        var row = new byte[w * (yb - ya)];
        int xs = Math.Max(0, rect.X0 - r), xe = Math.Min(n, rect.X1 + r);
        for (int y = ya; y < yb; y++)
        {
            int @base = y * n, ro = (y - ya) * w;
            int run = 0;
            for (int cc = xs; cc < xe; cc++)
            {
                run = walk[@base + cc] != 0 ? run + 1 : 0;
                int x = cc - r;
                if (x >= rect.X0 && x < rect.X1) row[ro + x - rect.X0] = run >= need ? (byte)1 : (byte)0;
            }
        }

        for (int x = rect.X0; x < rect.X1; x++)
        {
            int run = 0;
            for (int yy = ya; yy < yb; yy++)
            {
                run = row[(yy - ya) * w + x - rect.X0] != 0 ? run + 1 : 0;
                int y = yy - r;
                if (y >= rect.Y0 && y < rect.Y1) output[y * n + x] = run >= need ? (byte)1 : (byte)0;
            }

            // 距下边界不足腐蚀半径的格永不可通行(行缓冲没覆盖到)
            for (int y = Math.Max(rect.Y0, yb - r); y < rect.Y1; y++) output[y * n + x] = 0;
        }
    }

    /// <summary>tile 重烘(缓存优先);返回是否有条目更换。</summary>
    private static bool BakeTiles(NavContext nav, List<int> tiles, NavTileCache cache, byte[] area)
    {
        int n = nav.CellCount, c = nav.Hpa!.ClustersPerSide;
        bool changed = false;
        foreach (int tile in tiles)
        {
            var entry = cache.Acquire(nav.Passable, area, n, tile % c, tile / c);
            if (!ReferenceEquals(nav.Tiles![tile], entry))
            {
                nav.Tiles[tile] = entry;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>可走位翻转的收尾(参考 finish 的非稀疏支路):连通域重标(全格 4 连通洪泛,
    /// 与全量烘焙同一份划分)+ 桥面合并 + 升序格表重扫 + 链接(端点触矩形才整表重建,否则只
    /// 重导出连通域图)+ 桥面可达 + HPA 增量。</summary>
    private static void FinishPassable(
        NavContext nav,
        CrowdRebakeSources sources,
        CrowdStructuresStore store,
        List<int> dirtyTiles,
        (int X0, int Y0, int X1, int Y1) rect,
        HashSet<int> sameTiles)
    {
        var config = sources.Config;
        int n = nav.CellCount, n2 = n * n;
        var comp = new int[n2];
        Array.Fill(comp, -1);
        int compCount = NavContextBaker.Components(nav.Passable, n, comp);

        if (sources.Deck.TileOrder.Length > 0)
        {
            var merged = new int[n2];
            compCount = UpperLayerBake.MergeDeckComponents(comp, compCount, nav.Passable, nav.UpPass, nav.Portal, n, merged);
            comp = merged;
        }

        nav.Comp = comp;
        nav.CompCount = compCount;
        nav.Cells = ScanCells(nav.Passable, n2);

        // 链接:只有跳跃候选端点落在重算矩形里,可走位变化才可能改链表;否则链表保留、仅重导出域图
        var profile = config.NavProfiles[config.AgentTypes[nav.LayerIndex].NavProfileIndex];
        if (profile.Jump != null && LinksTouch(sources.JumpCandidates, rect, n))
        {
            nav.Links = NavContextBaker.BuildLinks(config, store.SnapshotSurface(), profile, nav.Passable, comp);
        }
        else
        {
            RelinkComponents(nav.Links, comp);
        }

        var (upComp, compCountTotal, reachOut) = NavContextBaker.BuildDeckReachability(n, comp, compCount, nav.Links, nav.UpPass, nav.Portal, nav.UpArea);
        nav.UpComp = upComp;
        nav.CompCountTotal = compCountTotal;
        nav.ReachOut = reachOut;
        nav.Hpa!.Update(nav, nav.Links, dirtyTiles, sameTiles);
    }

    private static int[] ScanCells(byte[] passable, int n2)
    {
        var cells = new List<int>(n2 / 2);
        for (int i = 0; i < n2; i++)
        {
            if (passable[i] != 0) cells.Add(i);
        }

        return cells.ToArray();
    }

    /// <summary>跳跃候选端点是否触到格矩形(linksTouch 移植)。</summary>
    private static bool LinksTouch(NavSurfaceJumpCandidate[] candidates, (int X0, int Y0, int X1, int Y1) rect, int n)
    {
        foreach (var cand in candidates)
        {
            if (Inside(cand.FromX, cand.FromY) || Inside(cand.ToX, cand.ToY)) return true;
        }

        return false;

        bool Inside(int x, int y) => x >= rect.X0 && x < rect.X1 && y >= rect.Y0 && y < rect.Y1;
    }

    /// <summary>连通域重标后重导出链接的域图(relinkComponents 移植):链表与 CSR 不动,只按
    /// 新标号重建 compOut。</summary>
    private static void RelinkComponents(NavLinkSet? links, int[] comp)
    {
        if (links == null) return;
        var compOut = new Dictionary<int, HashSet<int>>();
        for (int e = 0; e < links.Count; e++)
        {
            int ca = comp[links.From[e]], cb = comp[links.To[e]];
            if (ca == cb) continue;
            if (!compOut.TryGetValue(ca, out var set)) compOut[ca] = set = new HashSet<int>();
            set.Add(cb);
        }

        links.CompOut = compOut;
    }

    // ── DB-03:增量 vs 全量一致性检查(调试开关;对拍免谈,验收必跑) ─────────────────────────────

    /// <summary>
    /// 把增量重烘后的上下文与"同栅格全量烘焙"逐字段比对:代价 / 可走 / 可通行 / 连通域划分 /
    /// 格表 / 链接表 / HPA 扁平视图。任何不一致都抛错——增量维护的等价性基准就是全量烘焙。
    /// 连通域编号按首见格序规范重标后比对(划分一致即可,编号本身不是合同)。
    /// </summary>
    public static void VerifyAgainstFullBake(
        IReadOnlyDictionary<int, NavContext> navs,
        CrowdRebakeSources sources,
        CrowdStructuresStore store)
    {
        var config = sources.Config;
        var freshCache = new NavTileCache(config.NavtileCacheCapacity, config.Hpa.ClusterSize,
            config.Navmesh.MinRegionArea.ToDouble(), config.Navmesh.MaxSimplificationError.ToDouble(),
            config.Navmesh.MaxEdgeLen.ToDouble(), config.Navmesh.MaxVertsPerPoly);
        foreach (var nav in navs.Values)
        {
            var full = NavContextBaker.Bake(config, store.SnapshotSurface(), sources.HeightS, sources.Deck, nav.LayerIndex, nav.ClearanceCells, freshCache);
            VerifyContext(nav, full, $"nav {nav.Id}");
        }
    }

    private static void VerifyContext(NavContext inc, NavContext full, string what)
    {
        int n2 = inc.CellCount * inc.CellCount;
        for (int i = 0; i < n2; i++)
        {
            if (inc.Cost[i] != full.Cost[i]) throw new InvalidOperationException($"{what}: 增量代价与全量不一致 @格 {i}");
            if (inc.Walk[i] != full.Walk[i]) throw new InvalidOperationException($"{what}: 增量可走与全量不一致 @格 {i}");
            if (inc.Passable[i] != full.Passable[i]) throw new InvalidOperationException($"{what}: 增量可通行与全量不一致 @格 {i}");
        }

        VerifyPartition(inc.Comp, full.Comp, what, "地面连通域");
        if (inc.Cells.Length != full.Cells.Length) throw new InvalidOperationException($"{what}: 增量格表长度 {inc.Cells.Length} ≠ 全量 {full.Cells.Length}");
        for (int i = 0; i < inc.Cells.Length; i++)
        {
            if (inc.Cells[i] != full.Cells[i]) throw new InvalidOperationException($"{what}: 增量格表与全量不一致 @序 {i}");
        }

        VerifyLinks(inc.Links, full.Links, what);
        VerifyHpa(inc.Hpa!, full.Hpa!, what);
    }

    private static void VerifyPartition(int[] inc, int[] full, string what, string name)
    {
        // 划分等价:两套编号按首见格序规范重标后逐格相等
        var mapInc = new Dictionary<int, int>();
        var mapFull = new Dictionary<int, int>();
        for (int i = 0; i < inc.Length; i++)
        {
            int a = inc[i], b = full[i];
            if ((a < 0) != (b < 0)) throw new InvalidOperationException($"{what}: {name}归属不一致 @格 {i}");
            if (a < 0) continue;
            if (!mapInc.TryGetValue(a, out int ra)) mapInc[a] = ra = mapInc.Count;
            if (!mapFull.TryGetValue(b, out int rb)) mapFull[b] = rb = mapFull.Count;
            if (ra != rb) throw new InvalidOperationException($"{what}: {name}划分与全量不一致 @格 {i}");
        }
    }

    private static void VerifyLinks(NavLinkSet? inc, NavLinkSet? full, string what)
    {
        if (inc == null || full == null)
        {
            if (inc != null || full != null) throw new InvalidOperationException($"{what}: 链接表存在性不一致");
            return;
        }

        if (inc.Count != full.Count) throw new InvalidOperationException($"{what}: 链接数 {inc.Count} ≠ 全量 {full.Count}");
        for (int e = 0; e < inc.Count; e++)
        {
            if (inc.From[e] != full.From[e] || inc.To[e] != full.To[e]
                || inc.Cost[e] != full.Cost[e] || inc.TwoWay[e] != full.TwoWay[e])
            {
                throw new InvalidOperationException($"{what}: 链接 {e} 与全量不一致");
            }
        }
    }

    private static void VerifyHpa(HpaGraph inc, HpaGraph full, string what)
    {
        if (inc.NodeCount != full.NodeCount || inc.EdgeCount != full.EdgeCount)
        {
            throw new InvalidOperationException($"{what}: HPA 节点/边数 ({inc.NodeCount},{inc.EdgeCount}) ≠ 全量 ({full.NodeCount},{full.EdgeCount})");
        }

        var a = inc.Flatten();
        var b = full.Flatten();
        if (a.NodeCell.Length != b.NodeCell.Length) throw new InvalidOperationException($"{what}: HPA 扁平视图节点数不一致");
        for (int i = 0; i < a.NodeCell.Length; i++)
        {
            if (a.NodeCell[i] != b.NodeCell[i] || a.NodeLayer[i] != b.NodeLayer[i] || a.NodeCluster[i] != b.NodeCluster[i])
            {
                throw new InvalidOperationException($"{what}: HPA 节点 {i} 与全量不一致");
            }

            if (a.AdjStart[i + 1] - a.AdjStart[i] != b.AdjStart[i + 1] - b.AdjStart[i])
            {
                throw new InvalidOperationException($"{what}: HPA 节点 {i} 出边数与全量不一致");
            }

            for (int k = a.AdjStart[i]; k < a.AdjStart[i + 1]; k++)
            {
                int kb = k - a.AdjStart[i] + b.AdjStart[i];
                if (a.AdjTo[k] != b.AdjTo[kb] || a.AdjCost[k] != b.AdjCost[kb])
                {
                    throw new InvalidOperationException($"{what}: HPA 节点 {i} 的边 {k - a.AdjStart[i]} 与全量不一致");
                }
            }
        }
    }
}
