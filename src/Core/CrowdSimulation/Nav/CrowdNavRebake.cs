using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Diagnostics;
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
    UpperLayerSurface UpperLayer,
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
    /// <summary>0 = 无变化,2 = 可走位翻转。1 = 代价或速度除数变了,或可走位没变但 tile 条目换了。道路/泛洪/火场只对把这些区域标成可走的移动类型是 1；该行写 false 的区域铺上去是 2。</summary>
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
/// 结构变更后的导航增量重烘:
/// 变更格矩形 → 逐上下文重分类(见 Kind)→
/// 可走位翻转时净空腐蚀矩形重算、触到的 tile 重烘(内容键缓存命中即复用)、
/// 连通域与跳跃链接重标、HPA 增量维护;仅代价时 tile 多边形与 HPA 块代价现读重算。
/// 与同栅格的全量烘焙按覆盖字段逐格一致(一致性检查见 VerifyAgainstFullBake)。
/// </summary>
public static class CrowdNavRebake
{
    public const byte KindNone = 0;
    public const byte KindCost = 1;
    public const byte KindPassable = 2;

    /// <summary>全部上下文对同一变更矩形跑一遍增量重烘。</summary>
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
        int erosion = Math.Max(0, nav.ClearanceNavCells - 1);
        int baseKind = Classify(nav, sources, store, rect);
        int upperKind = RefreshUpper(nav, sources, rect, erosion);
        int kind = baseKind > upperKind ? baseKind : upperKind;
        var grown = Grow(rect, erosion, navCellsPerSide);
        if (upperKind == KindPassable)
        {
            var upperOut = Grow(rect, 2 * erosion, navCellsPerSide);
            grown = (
                Math.Min(grown.X0, upperOut.X0),
                Math.Min(grown.Y0, upperOut.Y0),
                Math.Max(grown.X1, upperOut.X1),
                Math.Max(grown.Y1, upperOut.Y1));
        }

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

        if (upperKind != KindNone)
        {
            RebuildUpperTiles(nav, sources, cache, Grow(rect, 2 * erosion, navCellsPerSide));
        }

        nav.Version++;
        nav.InvalidateReach();
        return tiles;
    }

    /// <summary>
    /// 桥面可走比足迹多出两倍腐蚀半径：岸边并入一圈，净空腐蚀再一圈。
    /// 腐蚀输入比输出再大一圈，桥头检索再大一圈，窗口内的结果才和整图腐蚀一致。
    /// 窗口里没有桥面也没有残留桥面可走时不碰缓冲。
    /// </summary>
    private static int RefreshUpper(
        NavContext nav,
        CrowdRebakeSources sources,
        (int X0, int Y0, int X1, int Y1) rect,
        int erosion)
    {
        var upperLayer = sources.UpperLayer;
        int side = nav.NavCellsPerSide;
        var output = Grow(rect, 2 * erosion, side);
        var win = Grow(output, erosion, side);
        var portals = Grow(win, erosion, side);
        if (UpperWindowClean(nav, upperLayer, portals, output)) return KindNone;

        int n2 = side * side;
        if (nav.UpperLayerWin == null || nav.UpperLayerWin.Length < n2) nav.UpperLayerWin = new byte[n2];
        if (nav.UpperLayerEroded == null || nav.UpperLayerEroded.Length < n2) nav.UpperLayerEroded = new byte[n2];
        ClearRect(nav.UpperLayerWin, side, win);
        var agent = sources.Config.AgentTypes[nav.LayerIndex];
        var row = agent.CostByArea;
        var speedRow = agent.SpeedDivisorByArea;
        var area = upperLayer.AreaIndex;
        var portalFlag = upperLayer.PortalFlag;
        for (int y = win.Y0; y < win.Y1; y++)
        {
            int rowBase = y * side;
            for (int x = win.X0; x < win.X1; x++)
            {
                int a = area[rowBase + x] - 1;
                if (a >= 0 && row[a] > Fix64.Zero) nav.UpperLayerWin[rowBase + x] = 1;
            }
        }

        if (erosion > 0)
        {
            var walk = nav.Walk;
            for (int y = portals.Y0; y < portals.Y1; y++)
            {
                for (int x = portals.X0; x < portals.X1; x++)
                {
                    int cell = y * side + x;
                    int a = area[cell] - 1;
                    if (a < 0 || portalFlag[cell] == 0 || row[a] <= Fix64.Zero) continue;
                    int y0 = Math.Max(win.Y0, y - erosion);
                    int y1 = Math.Min(win.Y1, y + erosion + 1);
                    int x0 = Math.Max(win.X0, x - erosion);
                    int x1 = Math.Min(win.X1, x + erosion + 1);
                    for (int gy = y0; gy < y1; gy++)
                    {
                        int grow = gy * side;
                        for (int gx = x0; gx < x1; gx++)
                        {
                            if (walk[grow + gx] != 0) nav.UpperLayerWin[grow + gx] = 1;
                        }
                    }
                }
            }
        }

        ErodeRect(nav, nav.UpperLayerWin, side, nav.ClearanceNavCells, output, nav.UpperLayerEroded);
        int kind = KindNone;
        var eroded = nav.UpperLayerEroded;
        for (int y = output.Y0; y < output.Y1; y++)
        {
            int rowBase = y * side;
            for (int x = output.X0; x < output.X1; x++)
            {
                int i = rowBase + x;
                int a = area[i] - 1;
                bool ok = a >= 0 && row[a] > Fix64.Zero && eroded[i] != 0;
                byte up = ok ? (byte)1 : (byte)0;
                byte po = ok && portalFlag[i] != 0 ? (byte)1 : (byte)0;
                byte ua = ok ? (byte)a : (byte)0;
                Fix64 uc = ok ? row[a] : Fix64.Zero;
                Fix64 us = ok ? speedRow[a] : Fix64.Zero;
                if (nav.UpPass[i] != up || nav.Portal[i] != po) kind = KindPassable;
                else if (kind != KindPassable && (nav.UpArea[i] != ua || nav.UpCost[i] != uc || nav.UpSpeedDivisor[i] != us))
                {
                    kind = KindCost;
                }

                nav.UpPass[i] = up;
                nav.Portal[i] = po;
                nav.UpArea[i] = ua;
                nav.UpCost[i] = uc;
                nav.UpSpeedDivisor[i] = us;
            }
        }

        return kind;
    }

    private static bool UpperWindowClean(
        NavContext nav,
        UpperLayerSurface upperLayer,
        (int X0, int Y0, int X1, int Y1) portals,
        (int X0, int Y0, int X1, int Y1) output)
    {
        int side = nav.NavCellsPerSide;
        var area = upperLayer.AreaIndex;
        var flag = upperLayer.PortalFlag;
        for (int y = portals.Y0; y < portals.Y1; y++)
        {
            int row = y * side;
            for (int x = portals.X0; x < portals.X1; x++)
            {
                int i = row + x;
                if (area[i] != 0 || flag[i] != 0) return false;
            }
        }

        for (int y = output.Y0; y < output.Y1; y++)
        {
            int row = y * side;
            for (int x = output.X0; x < output.X1; x++)
            {
                int i = row + x;
                if (nav.UpPass[i] != 0 || nav.Portal[i] != 0 || nav.UpArea[i] != 0
                    || nav.UpCost[i] != Fix64.Zero || nav.UpSpeedDivisor[i] != Fix64.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void ClearRect(byte[] buffer, int side, (int X0, int Y0, int X1, int Y1) rect)
    {
        int width = rect.X1 - rect.X0;
        if (width <= 0) return;
        for (int y = rect.Y0; y < rect.Y1; y++) Array.Clear(buffer, y * side + rect.X0, width);
    }

    private static void RebuildUpperTiles(
        NavContext nav,
        CrowdRebakeSources sources,
        NavTileCache cache,
        (int X0, int Y0, int X1, int Y1) rect)
    {
        if (nav.Tiles == null || rect.X1 <= rect.X0 || rect.Y1 <= rect.Y0) return;
        int side = nav.NavCellsPerSide;
        int tileSize = sources.Config.Hpa.NavCellsPerTile;
        int tilesPerSide = side / tileSize;
        int tx0 = rect.X0 / tileSize;
        int ty0 = rect.Y0 / tileSize;
        int tx1 = (rect.X1 - 1) / tileSize;
        int ty1 = (rect.Y1 - 1) / tileSize;
        var upPass = nav.UpPass;
        var portal = nav.Portal;
        var upCost = nav.UpCost;
        var upArea = nav.UpArea;
        int span = tileSize * tileSize;
        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int tileId = ty * tilesPerSide + tx;
                int originX = tx * tileSize;
                int originY = ty * tileSize;
                bool any = false;
                for (int y = originY; y < originY + tileSize && !any; y++)
                {
                    int row = y * side;
                    for (int x = originX; x < originX + tileSize; x++)
                    {
                        if (upPass[row + x] == 0) continue;
                        any = true;
                        break;
                    }
                }

                var dict = nav.UpperTiles;
                if (!any)
                {
                    if (dict != null && dict.Remove(tileId) && dict.Count == 0) nav.UpperTiles = null;
                    continue;
                }

                var entry = cache.Acquire(upPass, upArea, side, tx, ty);
                var portals = new List<int>();
                var pc = new Fix64[entry.Count];
                var cnt = new int[entry.Count];
                for (int k = 0; k < span; k++)
                {
                    int cell = (originY + k / tileSize) * side + originX + k % tileSize;
                    if (portal[cell] != 0) portals.Add(k);
                    int p = entry.PolyOf[k];
                    if (p >= 0)
                    {
                        pc[p] += upCost[cell];
                        cnt[p]++;
                    }
                }

                for (int p = 0; p < entry.Count; p++) pc[p] = cnt[p] > 0 ? pc[p] / cnt[p] : Fix64.OneValue;
                (nav.UpperTiles ??= new Dictionary<int, UpperTileInfo>())[tileId] = new UpperTileInfo
                {
                    Entry = entry,
                    Portals = portals.ToArray(),
                    PolyCost = pc,
                };
            }
        }
    }

    private static readonly List<int> NoTiles = new();

    private static NavTileEntry?[] PrevOf(NavContext nav, int count)
    {
        var prev = nav.PrevTiles;
        if (prev == null || prev.Length < count) nav.PrevTiles = prev = new NavTileEntry?[Math.Max(count, 8)];
        return prev;
    }

    /// <summary>变更矩形内重分类。0 = 本上下文看不到任何变化。1 = 代价或速度除数变了。2 = 有可走位翻转。道路/泛洪/火场只对把这些区域标成可走的移动类型是 1。</summary>
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
        var navAreas = config.NavAreas;
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
                if (cell > Fix64.Zero && !navAreas[a].IgnoresSlope && slope[i] > maxSlope)
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

    /// <summary>可走位翻转后的净空腐蚀矩形重算:只写矩形内;
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

    /// <summary>可走位翻转的收尾:连通域重标(全格 4 连通洪泛,
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

        int[]? upperLocal = null;
        if (sources.UpperLayer.TileOrder.Length > 0)
        {
            var merged = Ensure(ref nav.MergeBuffer, n2);
            upperLocal = Ensure(ref nav.UpperLayerCompBuffer, n2);
            compCount = UpperLayerBake.MergeUpperLayerComponents(
                comp, compCount, nav.Passable, nav.UpPass, nav.Portal, navCellsPerSide, merged,
                upperLocal, queue, Ensure(ref nav.UnionParent, n2 * 2), Ensure(ref nav.UnionDense, n2 * 2));
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

        RefillUpperLayerReach(nav, compCount, nav.Links, upperLocal);
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

    private static void RefillUpperLayerReach(NavContext nav, int compCount, NavLinkSet? links, int[]? upperLocal)
    {
        int n2 = nav.NavCellsPerSide * nav.NavCellsPerSide;
        var upComp = nav.UpComp ??= new int[n2];
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

        nav.CompCountTotal = NavContextBaker.WriteUpperLayerLabels(upComp, nav.UpPass, compCount, upperLocal);
        for (int c = 0; c < n2; c++)
        {
            if (nav.Portal[c] == 0 || nav.Comp[c] < 0 || upComp[c] < 0) continue;
            AddReachEdge(nav, nav.Comp[c], upComp[c]);
            AddReachEdge(nav, upComp[c], nav.Comp[c]);
        }
    }

    private static void AddReachEdge(NavContext nav, int from, int to)
    {
        var graph = nav.ReachOut!;
        if (!graph.TryGetValue(from, out var set)) graph[from] = set = nav.RentReachSet();
        set.Add(to);
    }

    /// <summary>跳跃候选端点是否触到格矩形。</summary>
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

    /// <summary>连通域重标后重导出链接的域图:链表与 CSR 不动,只按
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

    /// <summary>
    /// 把增量重烘后的上下文与"同栅格全量烘焙"逐字段比对:代价 / 可走 / 可通行 / 连通域划分 /
    /// 格表 / 链接表 / HPA 扁平视图。任何不一致都抛错——增量维护的等价性基准就是全量烘焙。
    /// 连通域编号按首见格序规范重标后比对(划分一致即可,编号本身不是合同)。
    /// 不一致抛 <see cref="CrowdSimulationInvariantException"/>，消息带 tick、nav 与 tile。
    /// </summary>
    public static void VerifyAgainstFullBake(
        IReadOnlyDictionary<int, NavContext> navs,
        CrowdRebakeSources sources,
        CrowdStructuresStore store,
        int tick)
    {
        var config = sources.Config;
        var freshCache = new NavTileCache(config.NavtileCacheCapacity, config.Hpa.NavCellsPerTile,
            config.Navmesh.MinRegionNavCellArea.ToDouble(), config.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
            config.Navmesh.MaxEdgeLenNavCells.ToDouble(), config.Navmesh.MaxVertsPerPoly);
        foreach (var nav in navs.Values)
        {
            VerifyAgainstField(nav, store, sources, tick, linksByCountOnly: false, freshCache);
        }
    }

    /// <summary>一份导航与给定地表的全量烘焙逐字段比对。认知变体的跳跃链接只比数量。</summary>
    public static void VerifyAgainstField(
        NavContext nav,
        CrowdStructuresStore field,
        CrowdRebakeSources sources,
        int tick,
        bool linksByCountOnly)
    {
        var config = sources.Config;
        var freshCache = new NavTileCache(config.NavtileCacheCapacity, config.Hpa.NavCellsPerTile,
            config.Navmesh.MinRegionNavCellArea.ToDouble(), config.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
            config.Navmesh.MaxEdgeLenNavCells.ToDouble(), config.Navmesh.MaxVertsPerPoly);
        VerifyAgainstField(nav, field, sources, tick, linksByCountOnly, freshCache);
    }

    private static void VerifyAgainstField(
        NavContext nav,
        CrowdStructuresStore field,
        CrowdRebakeSources sources,
        int tick,
        bool linksByCountOnly,
        NavTileCache freshCache)
    {
        var full = NavContextBaker.Bake(
            sources.Config, field.SnapshotSurface(), sources.HeightS, sources.UpperLayer,
            nav.LayerIndex, nav.ClearanceNavCells, freshCache);
        VerifyContext(nav, full, tick, linksByCountOnly);
    }

    private static void VerifyContext(NavContext inc, NavContext full, int tick, bool linksByCountOnly)
    {
        int n2 = inc.NavCellsPerSide * inc.NavCellsPerSide;
        if (inc.MinCost != full.MinCost)
        {
            ThrowMismatch(tick, inc, -1, -1, $"最小代价 {inc.MinCost} ≠ 全量 {full.MinCost}");
        }

        for (int i = 0; i < n2; i++)
        {
            if (inc.Cost[i] != full.Cost[i]) ThrowMismatch(tick, inc, i, -1, $"增量代价与全量不一致 @格 {i}");
            if (inc.SpeedDivisor[i] != full.SpeedDivisor[i]) ThrowMismatch(tick, inc, i, -1, $"增量速度除数与全量不一致 @格 {i}");
            if (inc.Walk[i] != full.Walk[i]) ThrowMismatch(tick, inc, i, -1, $"增量可走与全量不一致 @格 {i}");
            if (inc.Passable[i] != full.Passable[i]) ThrowMismatch(tick, inc, i, -1, $"增量可通行与全量不一致 @格 {i}");
            if (inc.UpPass[i] != full.UpPass[i]) ThrowMismatch(tick, inc, i, -1, $"增量桥面可通行与全量不一致 @格 {i}");
            if (inc.Portal[i] != full.Portal[i]) ThrowMismatch(tick, inc, i, -1, $"增量桥头与全量不一致 @格 {i}");
            if (inc.UpArea[i] != full.UpArea[i]) ThrowMismatch(tick, inc, i, -1, $"增量桥面区域与全量不一致 @格 {i}");
            if (inc.UpCost[i] != full.UpCost[i]) ThrowMismatch(tick, inc, i, -1, $"增量桥面代价与全量不一致 @格 {i}");
            if (inc.UpSpeedDivisor[i] != full.UpSpeedDivisor[i]) ThrowMismatch(tick, inc, i, -1, $"增量桥面速度除数与全量不一致 @格 {i}");
        }

        VerifyPartition(inc, inc.Comp, full.Comp, tick, "地面连通域");
        if (inc.CellCount != full.CellCount)
        {
            ThrowMismatch(tick, inc, -1, -1, $"增量格表长度 {inc.CellCount} ≠ 全量 {full.CellCount}");
        }

        if (inc.UpComp == null || full.UpComp == null)
        {
            if (inc.UpComp != null || full.UpComp != null)
            {
                ThrowMismatch(tick, inc, -1, -1, "桥面连通域存在性不一致");
            }
        }
        else
        {
            VerifyPartition(inc, inc.UpComp, full.UpComp, tick, "桥面连通域");
        }

        if (inc.CompCountTotal != full.CompCountTotal)
        {
            ThrowMismatch(tick, inc, -1, -1, $"连通域总数 {inc.CompCountTotal} ≠ 全量 {full.CompCountTotal}");
        }

        VerifyReach(inc, full, tick);
        for (int i = 0; i < inc.CellCount; i++)
        {
            if (inc.Cells[i] != full.Cells[i]) ThrowMismatch(tick, inc, inc.Cells[i], -1, $"增量格表与全量不一致 @序 {i}");
        }

        VerifyLinks(inc, full.Links, tick, linksByCountOnly);
        VerifyTiles(inc, full, tick);
        VerifyUpperTiles(inc, full, tick);
        VerifyHpa(inc, full.Hpa!, tick);
    }

    /// <summary>桥面编号按同一套 4 连通划分比对；可达图按这个划分下的边集比对，边集相同则 CanReach 相同。</summary>
    private static void VerifyReach(NavContext inc, NavContext full, int tick)
    {
        var mapInc = CanonIds(inc);
        var mapFull = CanonIds(full);
        var edgesInc = CanonEdges(inc.ReachOut, mapInc, inc, tick, "增量");
        var edgesFull = CanonEdges(full.ReachOut, mapFull, inc, tick, "全量");
        if (edgesInc.Count != edgesFull.Count)
        {
            ThrowMismatch(tick, inc, -1, -1, $"可达边数 {edgesInc.Count} ≠ 全量 {edgesFull.Count}");
        }

        foreach (var kv in edgesInc)
        {
            if (!edgesFull.TryGetValue(kv.Key, out var other) || !kv.Value.SetEquals(other))
            {
                ThrowMismatch(tick, inc, -1, -1, $"可达边与全量不一致 @域 {kv.Key}");
            }
        }
    }

    private static Dictionary<int, int> CanonIds(NavContext nav)
    {
        var map = new Dictionary<int, int>();
        void Add(int id)
        {
            if (id >= 0) map.TryAdd(id, map.Count);
        }

        int n = nav.Comp.Length;
        var up = nav.UpComp;
        for (int i = 0; i < n; i++)
        {
            Add(nav.Comp[i]);
            if (up != null && i < up.Length) Add(up[i]);
        }

        return map;
    }

    private static Dictionary<int, HashSet<int>> CanonEdges(
        Dictionary<int, HashSet<int>>? graph, Dictionary<int, int> map, NavContext nav, int tick, string side)
    {
        var edges = new Dictionary<int, HashSet<int>>();
        if (graph == null) return edges;
        foreach (var kv in graph)
        {
            if (!map.TryGetValue(kv.Key, out int from))
            {
                ThrowMismatch(tick, nav, -1, -1, $"{side}可达边的起点 {kv.Key} 不在连通域里");
            }

            if (!edges.TryGetValue(from, out var set)) edges[from] = set = new HashSet<int>();
            foreach (int next in kv.Value)
            {
                if (!map.TryGetValue(next, out int to))
                {
                    ThrowMismatch(tick, nav, -1, -1, $"{side}可达边的终点 {next} 不在连通域里");
                }

                set.Add(to);
            }
        }

        return edges;
    }

    private static void VerifyPartition(NavContext incNav, int[] inc, int[] full, int tick, string name)
    {
        // 划分等价:两套编号按首见格序规范重标后逐格相等
        var mapInc = new Dictionary<int, int>();
        var mapFull = new Dictionary<int, int>();
        for (int i = 0; i < inc.Length; i++)
        {
            int a = inc[i], b = full[i];
            if ((a < 0) != (b < 0)) ThrowMismatch(tick, incNav, i, -1, $"{name}归属不一致 @格 {i}");
            if (a < 0) continue;
            if (!mapInc.TryGetValue(a, out int ra)) mapInc[a] = ra = mapInc.Count;
            if (!mapFull.TryGetValue(b, out int rb)) mapFull[b] = rb = mapFull.Count;
            if (ra != rb) ThrowMismatch(tick, incNav, i, -1, $"{name}划分与全量不一致 @格 {i}");
        }
    }

    private static void VerifyLinks(NavContext nav, NavLinkSet? full, int tick, bool countOnly)
    {
        var inc = nav.Links;
        if (inc == null || full == null)
        {
            if (inc != null || full != null) ThrowMismatch(tick, nav, -1, -1, "链接表存在性不一致");
            return;
        }

        if (inc.Count != full.Count) ThrowMismatch(tick, nav, -1, -1, $"链接数 {inc.Count} ≠ 全量 {full.Count}");
        if (countOnly) return;
        for (int e = 0; e < inc.Count; e++)
        {
            if (inc.From[e] != full.From[e] || inc.To[e] != full.To[e]
                || inc.Cost[e] != full.Cost[e] || inc.TwoWay[e] != full.TwoWay[e])
            {
                ThrowMismatch(tick, nav, inc.From[e], -1, $"链接 {e} 与全量不一致");
            }
        }
    }

    private static void VerifyHpa(NavContext nav, HpaGraph full, int tick)
    {
        var inc = nav.Hpa!;
        if (inc.NodeCount != full.NodeCount || inc.EdgeCount != full.EdgeCount)
        {
            ThrowMismatch(tick, nav, -1, -1,
                $"HPA 节点/边数 ({inc.NodeCount},{inc.EdgeCount}) ≠ 全量 ({full.NodeCount},{full.EdgeCount})");
        }

        var a = inc.Flatten();
        var b = full.Flatten();
        if (a.NodeCell.Length != b.NodeCell.Length) ThrowMismatch(tick, nav, -1, -1, "HPA 扁平视图节点数不一致");
        for (int i = 0; i < a.NodeCell.Length; i++)
        {
            int tile = a.NodeTile[i];
            if (a.NodeCell[i] != b.NodeCell[i] || a.NodeLayer[i] != b.NodeLayer[i] || tile != b.NodeTile[i])
            {
                ThrowMismatch(tick, nav, -1, tile, $"HPA 节点 {i} 与全量不一致");
            }

            if (a.AdjStart[i + 1] - a.AdjStart[i] != b.AdjStart[i + 1] - b.AdjStart[i])
            {
                ThrowMismatch(tick, nav, -1, tile, $"HPA 节点 {i} 出边数与全量不一致");
            }

            for (int k = a.AdjStart[i]; k < a.AdjStart[i + 1]; k++)
            {
                int kb = k - a.AdjStart[i] + b.AdjStart[i];
                if (a.AdjTo[k] != b.AdjTo[kb] || a.AdjCost[k] != b.AdjCost[kb])
                {
                    ThrowMismatch(tick, nav, -1, tile, $"HPA 节点 {i} 的边 {k - a.AdjStart[i]} 与全量不一致");
                }
            }
        }
    }

    private static void VerifyTiles(NavContext inc, NavContext full, int tick)
    {
        var left = inc.Tiles;
        var right = full.Tiles;
        if (left == null || right == null)
        {
            if (left != null || right != null) ThrowMismatch(tick, inc, -1, -1, "地面 tile 表存在性不一致");
            return;
        }

        if (left.Length != right.Length)
        {
            ThrowMismatch(tick, inc, -1, -1, $"地面 tile 数 {left.Length} ≠ 全量 {right.Length}");
        }

        for (int i = 0; i < left.Length; i++)
        {
            CompareTile(inc, left[i], right[i], tick, i, "地面");
        }
    }

    private static void VerifyUpperTiles(NavContext inc, NavContext full, int tick)
    {
        var left = inc.UpperTiles;
        var right = full.UpperTiles;
        if (left == null || right == null)
        {
            if (left != null || right != null) ThrowMismatch(tick, inc, -1, -1, "桥面 tile 表存在性不一致");
            return;
        }

        if (left.Count != right.Count)
        {
            ThrowMismatch(tick, inc, -1, -1, $"桥面 tile 数 {left.Count} ≠ 全量 {right.Count}");
        }

        foreach (var kv in left)
        {
            if (!right.TryGetValue(kv.Key, out var other))
            {
                ThrowMismatch(tick, inc, -1, kv.Key, $"桥面 tile {kv.Key} 全量没有");
            }

            CompareTile(inc, kv.Value.Entry, other.Entry, tick, kv.Key, "桥面");
            CompareInts(inc, kv.Value.Portals, kv.Value.Portals.Length, other.Portals, other.Portals.Length, tick, kv.Key, "桥面 portal");
            CompareFix(inc, kv.Value.PolyCost, kv.Value.PolyCost.Length, other.PolyCost, other.PolyCost.Length, tick, kv.Key, "桥面多边形代价");
        }
    }

    private static void CompareTile(NavContext nav, NavTileEntry? inc, NavTileEntry? full, int tick, int tile, string name)
    {
        if (inc == null || full == null)
        {
            if (inc != null || full != null) ThrowMismatch(tick, nav, -1, tile, $"{name} tile {tile} 存在性不一致");
            return;
        }

        if (inc.Count != full.Count || inc.RegionCount != full.RegionCount)
        {
            ThrowMismatch(tick, nav, -1, tile, $"{name} tile {tile} 多边形数 {inc.Count}/{inc.RegionCount} ≠ 全量 {full.Count}/{full.RegionCount}");
        }

        CompareInts(nav, inc.Vx, inc.LiveVertices, full.Vx, full.LiveVertices, tick, tile, $"{name} tile {tile} 顶点 x");
        CompareInts(nav, inc.Vy, inc.LiveVertices, full.Vy, full.LiveVertices, tick, tile, $"{name} tile {tile} 顶点 y");
        CompareInts(nav, inc.PolyStart, inc.LivePolyStart, full.PolyStart, full.LivePolyStart, tick, tile, $"{name} tile {tile} 多边形起点");
        CompareInts(nav, inc.PolyVerts, inc.LivePolyVerts, full.PolyVerts, full.LivePolyVerts, tick, tile, $"{name} tile {tile} 多边形顶点");
        CompareInts(nav, inc.NeiStart, inc.LiveNeiStart, full.NeiStart, full.LiveNeiStart, tick, tile, $"{name} tile {tile} 邻接起点");
        CompareInts(nav, inc.Nei, inc.LiveNeighbors, full.Nei, full.LiveNeighbors, tick, tile, $"{name} tile {tile} 邻接");
        CompareInts(nav, inc.NeiA, inc.LiveNeighbors, full.NeiA, full.LiveNeighbors, tick, tile, $"{name} tile {tile} 邻接边 a");
        CompareInts(nav, inc.NeiB, inc.LiveNeighbors, full.NeiB, full.LiveNeighbors, tick, tile, $"{name} tile {tile} 邻接边 b");
        CompareInts(nav, inc.BMinX, inc.LiveBoxes, full.BMinX, full.LiveBoxes, tick, tile, $"{name} tile {tile} 盒 minX");
        CompareInts(nav, inc.BMinY, inc.LiveBoxes, full.BMinY, full.LiveBoxes, tick, tile, $"{name} tile {tile} 盒 minY");
        CompareInts(nav, inc.BMaxX, inc.LiveBoxes, full.BMaxX, full.LiveBoxes, tick, tile, $"{name} tile {tile} 盒 maxX");
        CompareInts(nav, inc.BMaxY, inc.LiveBoxes, full.BMaxY, full.LiveBoxes, tick, tile, $"{name} tile {tile} 盒 maxY");
        CompareInts(nav, inc.PolyOf, inc.PolyOf.Length, full.PolyOf, full.PolyOf.Length, tick, tile, $"{name} tile {tile} 格多边形");
        CompareInts(nav, inc.BorderPoly, inc.LiveBorders, full.BorderPoly, full.LiveBorders, tick, tile, $"{name} tile {tile} 边界多边形");
        CompareBytes(nav, inc.BorderSide, inc.LiveBorders, full.BorderSide, full.LiveBorders, tick, tile, $"{name} tile {tile} 边界侧");
        CompareInts(nav, inc.BorderLo, inc.LiveBorders, full.BorderLo, full.LiveBorders, tick, tile, $"{name} tile {tile} 边界 lo");
        CompareInts(nav, inc.BorderHi, inc.LiveBorders, full.BorderHi, full.LiveBorders, tick, tile, $"{name} tile {tile} 边界 hi");
        CompareBytes(nav, inc.BorderRev, inc.LiveBorders, full.BorderRev, full.LiveBorders, tick, tile, $"{name} tile {tile} 边界反向");
    }

    private static void CompareInts(NavContext nav, int[] left, int leftCount, int[] right, int rightCount, int tick, int tile, string name)
    {
        if (leftCount != rightCount)
        {
            ThrowMismatch(tick, nav, -1, tile, $"{name} 长度 {leftCount} ≠ 全量 {rightCount}");
        }

        for (int i = 0; i < leftCount; i++)
        {
            if (left[i] != right[i]) ThrowMismatch(tick, nav, -1, tile, $"{name} [{i}] {left[i]} ≠ 全量 {right[i]}");
        }
    }

    private static void CompareBytes(NavContext nav, byte[] left, int leftCount, byte[] right, int rightCount, int tick, int tile, string name)
    {
        if (leftCount != rightCount)
        {
            ThrowMismatch(tick, nav, -1, tile, $"{name} 长度 {leftCount} ≠ 全量 {rightCount}");
        }

        for (int i = 0; i < leftCount; i++)
        {
            if (left[i] != right[i]) ThrowMismatch(tick, nav, -1, tile, $"{name} [{i}] {left[i]} ≠ 全量 {right[i]}");
        }
    }

    private static void CompareFix(NavContext nav, Fix64[] left, int leftCount, Fix64[] right, int rightCount, int tick, int tile, string name)
    {
        if (leftCount != rightCount)
        {
            ThrowMismatch(tick, nav, -1, tile, $"{name} 长度 {leftCount} ≠ 全量 {rightCount}");
        }

        for (int i = 0; i < leftCount; i++)
        {
            if (left[i] != right[i]) ThrowMismatch(tick, nav, -1, tile, $"{name} [{i}] 与全量不一致");
        }
    }

    private static void ThrowMismatch(int tick, NavContext nav, int cell, int tile, string detail)
    {
        int cx = -1, cy = -1;
        if (cell >= 0)
        {
            int n = nav.NavCellsPerSide;
            cx = cell % n;
            cy = cell / n;
            if (tile < 0) tile = TileIndex(nav, cell);
            detail = $"{detail} ({cx},{cy})";
        }

        throw new CrowdSimulationInvariantException(
            $"tick {tick} nav {nav.Id} tile {tile}: {detail}",
            tick, -1, -1, cx, cy, nav.Id, tile, -1);
    }

    private static int TileIndex(NavContext nav, int cell)
    {
        var hpa = nav.Hpa;
        if (hpa == null || hpa.NavCellsPerTile <= 0) return -1;
        int n = nav.NavCellsPerSide;
        int tile = hpa.NavCellsPerTile;
        int tilesPerSide = hpa.NavTilesPerSide;
        int tx = cell % n / tile;
        int ty = cell / n / tile;
        if (tx >= tilesPerSide) tx = tilesPerSide - 1;
        if (ty >= tilesPerSide) ty = tilesPerSide - 1;
        return ty * tilesPerSide + tx;
    }
}
