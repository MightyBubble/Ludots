using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>增量重烘的烘焙输入(会话装载期的同源数据:配置 / 高度场 / 桥面 / 跳跃候选;
/// 区域与阻挡栅格的活数据在 CrowdStructuresStore 上,快照面经 SnapshotSurface 取)。</summary>
public sealed record CrowdRebakeSources(
    CrowdSimulationRuntimeConfig Config,
    NavHeightField HeightS,
    DeckSurface Deck,
    NavSurfaceJumpCandidate[] JumpCandidates)
{
    internal readonly CrowdRebakeResultScratch Results = new();
}

/// <summary>单上下文增量重烘的脏 tile 集与摘要(真值逐字段对拍的数据面)。</summary>
public sealed class CrowdNavRebakeContextResult
{
    public int NavId { get; set; }
    /// <summary>脏 tile(tile id 升序);空表 = 该上下文什么都看不到,什么都不失效。</summary>
    public List<int> DirtyTiles { get; set; } = new();
    /// <summary>0 = 无变化,1 = 仅代价,2 = 可走位翻转。</summary>
    public int Kind { get; set; }
}

/// <summary>重烘结果的双缓冲。构造时两面都留好行,测量窗里换面复用,不逐次 new。</summary>
internal sealed class CrowdRebakeResultScratch
{
    internal readonly List<CrowdNavRebakeContextResult>[] Buffers = new List<CrowdNavRebakeContextResult>[2];
    internal readonly Stack<CrowdNavRebakeContextResult> Spare = new(32);
    internal int Cursor;

    internal CrowdRebakeResultScratch()
    {
        for (int b = 0; b < 2; b++)
        {
            Buffers[b] = new List<CrowdNavRebakeContextResult>(16);
            for (int i = 0; i < 16; i++) Buffers[b].Add(NewRow());
        }
    }

    internal static CrowdNavRebakeContextResult NewRow() => new() { DirtyTiles = new List<int>(8) };
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
        Dictionary<int, NavContext> navs,
        CrowdRebakeSources sources,
        CrowdStructuresStore store,
        (int X0, int Y0, int X1, int Y1) rect,
        NavTileCache cache)
    {
        var scratch = sources.Results;
        var results = scratch.Buffers[scratch.Cursor & 1];
        scratch.Cursor++;
        int n = 0;
        foreach (var kv in navs)
        {
            var nav = kv.Value;
            var tiles = RebakeNav(nav, sources, store, rect, cache);
            CrowdNavRebakeContextResult row;
            if (n < results.Count) row = results[n];
            else
            {
                row = scratch.Spare.TryPop(out var spare) ? spare : CrowdRebakeResultScratch.NewRow();
                results.Add(row);
            }

            row.NavId = nav.Id;
            row.Kind = nav.LastRebake;
            row.DirtyTiles.Clear();
            for (int i = 0; i < tiles.Count; i++) row.DirtyTiles.Add(tiles[i]);
            n++;
        }

        while (results.Count > n)
        {
            scratch.Spare.Push(results[^1]);
            results.RemoveAt(results.Count - 1);
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
        int navCellsPerSide = nav.NavCellsPerSide, navCellsPerTile = config.Hpa.NavCellsPerTile, navTilesPerSide = navCellsPerSide / navCellsPerTile;
        int erosion = nav.ClearanceNavCells - 1;
        int kind = Classify(nav, sources, store, rect);
        var grown = Grow(rect, erosion, navCellsPerSide);
        List<int> tiles;
        if (kind == KindPassable)
        {
            ErodeRect(nav, nav.Walk, navCellsPerSide, nav.ClearanceNavCells, grown, nav.Passable);
            var dirty = TilesOf(nav, grown, navCellsPerTile, navTilesPerSide);
            // 重烘后拿到同一内容条目(缓存命中)的 tile:HPA 簇内距离逐位复用
            var prev = PrevOf(nav, dirty.Count);
            for (int i = 0; i < dirty.Count; i++) prev[i] = nav.Tiles![dirty[i]];
            BakeTiles(nav, dirty, cache, store.Area);
            var same = nav.SameTiles;
            same.Clear();
            for (int i = 0; i < dirty.Count; i++)
            {
                if (ReferenceEquals(nav.Tiles![dirty[i]], prev[i])) same.Add(dirty[i]);
            }

            FinishPassable(nav, sources, store, dirty, grown, same);
            tiles = dirty;
            var changed = nav.ChangedTileList;
            changed.Clear();
            for (int i = 0; i < dirty.Count; i++)
            {
                if (!same.Contains(dirty[i])) changed.Add(dirty[i]);
            }

            nav.ChangedTiles = changed;
            nav.LastRebake = KindPassable;
        }
        else
        {
            var dirty = TilesOf(nav, rect, navCellsPerTile, navTilesPerSide);
            bool meshChanged = BakeTiles(nav, dirty, cache, store.Area);
            if (kind == KindNone && !meshChanged)
            {
                nav.LastRebake = KindNone;
                nav.ChangedTiles = NoTiles;
                return NoTiles;
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

    private static readonly List<int> NoTiles = new();

    private static NavTileEntry?[] PrevOf(NavContext nav, int count)
    {
        var prev = nav.PrevTiles;
        if (prev == null || prev.Length < count) nav.PrevTiles = prev = new NavTileEntry?[Math.Max(count, 8)];
        return prev;
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
        int navCellsPerSide = nav.NavCellsPerSide;
        var agent = config.AgentTypes[nav.LayerIndex];
        var profile = config.NavProfiles[agent.NavProfileIndex];
        Fix64 maxSlope = profile.MaxSlopeDeg is { } deg ? Fix64Math.Tan(deg * Fix64.Deg2Rad) : Fix64.MaxValue;
        var row = agent.CostByArea;
        var speedRow = agent.SpeedDivisorByArea;
        var slopeFree = config.NavAreas;
        var area = store.Area;
        var blocked = store.Blocked;
        var slope = sources.HeightS.Slope;
        var cost = nav.Cost;
        var speedDivisor = nav.SpeedDivisor;
        var walk = nav.Walk;
        int kind = KindNone;
        for (int y = rect.Y0; y < rect.Y1; y++)
        {
            for (int x = rect.X0; x < rect.X1; x++)
            {
                int i = y * navCellsPerSide + x;
                int a = area[i];
                Fix64 cell = row[a];
                Fix64 div = speedRow[a];
                if (cell > Fix64.Zero && !slopeFree[a].SlopeFree && slope[i] > maxSlope)
                {
                    cell = Fix64.Zero;
                    div = Fix64.Zero;
                }

                if (cost[i] != cell)
                {
                    cost[i] = cell;
                    if (kind == KindNone) kind = KindCost;
                }

                if (speedDivisor[i] != div)
                {
                    speedDivisor[i] = div;
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
    private static (int X0, int Y0, int X1, int Y1) Grow((int X0, int Y0, int X1, int Y1) rect, int k, int navCellsPerSide) => (
        Math.Max(0, rect.X0 - k),
        Math.Max(0, rect.Y0 - k),
        Math.Min(navCellsPerSide, rect.X1 + k),
        Math.Min(navCellsPerSide, rect.Y1 + k));

    /// <summary>格矩形触到的 tile id(升序去重)。结果写进 nav 的复用表,下次重烘会清空。</summary>
    internal static List<int> TilesOf(NavContext nav, (int X0, int Y0, int X1, int Y1) rect, int navCellsPerTile, int navTilesPerSide)
    {
        var tiles = nav.TileScratch;
        var seen = nav.TileSeenScratch;
        tiles.Clear();
        seen.Clear();
        for (int ty = rect.Y0 / navCellsPerTile; ty <= (rect.Y1 - 1) / navCellsPerTile; ty++)
        {
            for (int tx = rect.X0 / navCellsPerTile; tx <= (rect.X1 - 1) / navCellsPerTile; tx++)
            {
                if (seen.Add(ty * navTilesPerSide + tx)) tiles.Add(ty * navTilesPerSide + tx);
            }
        }

        return tiles;
    }

    /// <summary>可走位翻转后的净空腐蚀矩形重算(erodeRect 移植):只写矩形内;
    /// 切比雪夫可分离两遍,行/列连续段计数,读入需外扩腐蚀半径。</summary>
    internal static void ErodeRect(NavContext nav, byte[] walk, int navCellsPerSide, int clearance, (int X0, int Y0, int X1, int Y1) rect, byte[] output)
    {
        int r = clearance - 1;
        if (r <= 0)
        {
            for (int y = rect.Y0; y < rect.Y1; y++)
            {
                for (int x = rect.X0; x < rect.X1; x++)
                {
                    output[y * navCellsPerSide + x] = walk[y * navCellsPerSide + x];
                }
            }

            return;
        }

        int need = 2 * r + 1, w = rect.X1 - rect.X0;
        int ya = Math.Max(0, rect.Y0 - r), yb = Math.Min(navCellsPerSide, rect.Y1 + r);
        int rowBytes = w * (yb - ya);
        var row = nav.ErodeRow;
        if (row == null || row.Length < rowBytes) nav.ErodeRow = row = new byte[Math.Max(rowBytes, navCellsPerSide * navCellsPerSide)];
        Array.Clear(row, 0, rowBytes);
        int xs = Math.Max(0, rect.X0 - r), xe = Math.Min(navCellsPerSide, rect.X1 + r);
        for (int y = ya; y < yb; y++)
        {
            int @base = y * navCellsPerSide, ro = (y - ya) * w;
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
                if (y >= rect.Y0 && y < rect.Y1) output[y * navCellsPerSide + x] = run >= need ? (byte)1 : (byte)0;
            }

            // 距下边界不足腐蚀半径的格永不可通行(行缓冲没覆盖到)
            for (int y = Math.Max(rect.Y0, yb - r); y < rect.Y1; y++) output[y * navCellsPerSide + x] = 0;
        }
    }

    /// <summary>tile 重烘(缓存优先);返回是否有条目更换。</summary>
    private static bool BakeTiles(NavContext nav, List<int> tiles, NavTileCache cache, byte[] area)
    {
        int navCellsPerSide = nav.NavCellsPerSide, navTilesPerSide = nav.Hpa!.NavTilesPerSide;
        bool changed = false;
        foreach (int tile in tiles)
        {
            var entry = cache.Acquire(nav.Passable, area, navCellsPerSide, tile % navTilesPerSide, tile / navTilesPerSide);
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
        int navCellsPerSide = nav.NavCellsPerSide, n2 = navCellsPerSide * navCellsPerSide;
        var comp = nav.Comp;
        Array.Fill(comp, -1);
        var queue = Ensure(ref nav.FloodQueue, n2);
        int compCount = NavContextBaker.Components(nav.Passable, navCellsPerSide, comp, queue);

        if (sources.Deck.TileOrder.Length > 0)
        {
            var merged = Ensure(ref nav.MergeBuffer, n2);
            compCount = UpperLayerBake.MergeDeckComponents(
                comp, compCount, nav.Passable, nav.UpPass, nav.Portal, navCellsPerSide, merged,
                Ensure(ref nav.DeckCompBuffer, n2), queue, Ensure(ref nav.UnionParent, n2 * 2), Ensure(ref nav.UnionDense, n2 * 2));
            (nav.Comp, nav.MergeBuffer) = (merged, comp);
            comp = nav.Comp;
        }

        nav.CompCount = compCount;
        ScanCells(nav);

        // 链接:只有跳跃候选端点落在重算矩形里,可走位变化才可能改链表;否则链表保留、仅重导出域图
        var profile = config.NavProfiles[config.AgentTypes[nav.LayerIndex].NavProfileIndex];
        bool relink = profile.Jump != null && LinksTouch(sources.JumpCandidates, rect, navCellsPerSide);
        if (relink)
        {
            nav.Links = NavContextBaker.BuildLinks(config, store.SnapshotSurface(), profile, nav.Passable, comp);
        }
        else
        {
            RelinkComponents(nav.Links, comp);
        }

        RefillDeckReach(nav, compCount, nav.Links);
        nav.Hpa!.Update(nav, nav.Links, dirtyTiles, sameTiles);
    }

    private static int[] Ensure(ref int[]? buffer, int length)
    {
        if (buffer == null || buffer.Length < length) buffer = new int[length];
        return buffer;
    }

    private static void ScanCells(NavContext nav)
    {
        int n2 = nav.NavCellsPerSide * nav.NavCellsPerSide;
        if (nav.Cells.Length < n2) nav.Cells = new int[n2];
        var passable = nav.Passable;
        var cells = nav.Cells;
        int w = 0;
        for (int i = 0; i < n2; i++)
        {
            if (passable[i] != 0) cells[w++] = i;
        }

        nav.CellCount = w;
    }

    private static void RefillDeckReach(NavContext nav, int compCount, NavLinkSet? links)
    {
        int n2 = nav.NavCellsPerSide * nav.NavCellsPerSide;
        var upComp = nav.UpComp ??= new int[n2];
        Array.Fill(upComp, -1);
        var reach = nav.ReachOut ??= new Dictionary<int, HashSet<int>>(CrowdSimulationLimits.HotPathSlotReserve);
        nav.ReleaseReachGraph(reach);
        if (links != null)
        {
            foreach (var kv in links.CompOut)
            {
                if (!reach.TryGetValue(kv.Key, out var set))
                {
                    reach[kv.Key] = set = nav.RentReachSet();
                }

                foreach (int v in kv.Value) set.Add(v);
            }
        }

        var areaComp = nav.AreaCompScratch;
        areaComp.Clear();
        int next = compCount;
        for (int c = 0; c < n2; c++)
        {
            if (nav.UpPass[c] == 0) continue;
            int area = nav.UpArea[c];
            if (!areaComp.TryGetValue(area, out int id))
            {
                id = next++;
                areaComp[area] = id;
            }

            upComp[c] = id;
        }

        for (int c = 0; c < n2; c++)
        {
            if (nav.Portal[c] == 0 || nav.Comp[c] < 0 || nav.UpPass[c] == 0) continue;
            AddReachEdge(nav, nav.Comp[c], upComp[c]);
            AddReachEdge(nav, upComp[c], nav.Comp[c]);
        }

        nav.CompCountTotal = next;
    }

    private static void AddReachEdge(NavContext nav, int from, int to)
    {
        var graph = nav.ReachOut!;
        if (!graph.TryGetValue(from, out var set)) graph[from] = set = nav.RentReachSet();
        set.Add(to);
    }

    /// <summary>跳跃候选端点是否触到格矩形(linksTouch 移植)。</summary>
    private static bool LinksTouch(NavSurfaceJumpCandidate[] candidates, (int X0, int Y0, int X1, int Y1) rect, int navCellsPerSide)
    {
        foreach (var cand in candidates)
        {
            if (Inside(cand.FromX, cand.FromY, rect) || Inside(cand.ToX, cand.ToY, rect)) return true;
        }

        return false;
    }

    private static bool Inside(int x, int y, (int X0, int Y0, int X1, int Y1) rect)
        => x >= rect.X0 && x < rect.X1 && y >= rect.Y0 && y < rect.Y1;

    /// <summary>连通域重标后重导出链接的域图(relinkComponents 移植):链表与 CSR 不动,只按
    /// 新标号重建 compOut。</summary>
    private static void RelinkComponents(NavLinkSet? links, int[] comp)
    {
        if (links == null) return;
        var compOut = links.CompOut;
        foreach (var set in compOut.Values)
        {
            int slots = set.EnsureCapacity(0);
            set.Clear();
            if (slots >= 128) links.CompSetPool.Push(set);
        }

        compOut.Clear();
        for (int e = 0; e < links.Count; e++)
        {
            int ca = comp[links.From[e]], cb = comp[links.To[e]];
            if (ca == cb) continue;
            if (!compOut.TryGetValue(ca, out var set))
            {
                set = links.CompSetPool.TryPop(out var pooled) ? pooled : new HashSet<int>(128);
                compOut[ca] = set;
            }

            set.Add(cb);
        }
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
        var freshCache = new NavTileCache(config.NavtileCacheCapacity, config.Hpa.NavCellsPerTile,
            config.Navmesh.MinRegionNavCellArea.ToDouble(), config.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
            config.Navmesh.MaxEdgeLenNavCells.ToDouble(), config.Navmesh.MaxVertsPerPoly);
        foreach (var nav in navs.Values)
        {
            var full = NavContextBaker.Bake(config, store.SnapshotSurface(), sources.HeightS, sources.Deck, nav.LayerIndex, nav.ClearanceNavCells, freshCache);
            VerifyContext(nav, full, $"nav {nav.Id}");
        }
    }

    private static void VerifyContext(NavContext inc, NavContext full, string what)
    {
        int n2 = inc.NavCellsPerSide * inc.NavCellsPerSide;
        for (int i = 0; i < n2; i++)
        {
            if (inc.Cost[i] != full.Cost[i]) throw new InvalidOperationException($"{what}: 增量代价与全量不一致 @格 {i}");
            if (inc.Walk[i] != full.Walk[i]) throw new InvalidOperationException($"{what}: 增量可走与全量不一致 @格 {i}");
            if (inc.Passable[i] != full.Passable[i]) throw new InvalidOperationException($"{what}: 增量可通行与全量不一致 @格 {i}");
        }

        VerifyPartition(inc.Comp, full.Comp, what, "地面连通域");
        if (inc.CellCount != full.CellCount) throw new InvalidOperationException($"{what}: 增量格表长度 {inc.CellCount} ≠ 全量 {full.CellCount}");
        for (int i = 0; i < inc.CellCount; i++)
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
            if (a.NodeCell[i] != b.NodeCell[i] || a.NodeLayer[i] != b.NodeLayer[i] || a.NodeTile[i] != b.NodeTile[i])
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
