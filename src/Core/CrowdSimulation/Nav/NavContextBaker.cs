using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 导航上下文烘焙（buildNavContext 的 S2 子集移植）：
/// 分类（代价 × 坡度 × 阻挡）→ 净空腐蚀 → 可走格表 → 连通域 → 跳跃链接。
/// 每格分类的唯一语义输入是导航区域栅格：代价 = 移动类型行[区域]；
/// 未列出的区域代价为 1、可通行；passable 为 false 的区域代价为 0、不可走；非 slopeFree 区域坡度超过 profile 上限不可走；阻挡格不可走。
/// </summary>
public static class NavContextBaker
{
    public static NavContext Bake(
        CrowdSimulationRuntimeConfig config,
        SurfaceGrid surface,
        NavHeightField heights,
        DeckSurface deck,
        int agentTypeIndex,
        int clearanceCells,
        NavTileCache? tileCache = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(heights);
        if (heights.NavCellsPerSide != config.NavCellsPerSide || surface.NavCellsPerSide != config.NavCellsPerSide)
        {
            throw new InvalidOperationException("NavContextBaker: 高度场 / 地表 / 配置的导航格数不一致。");
        }

        var agent = config.AgentTypes[agentTypeIndex];
        var profile = config.NavProfiles[agent.NavProfileIndex];
        Fix64 maxSlope = profile.MaxSlopeDeg is { } deg
            ? Fix64Math.Tan(deg * Fix64.Deg2Rad)
            : Fix64.MaxValue;

        int navCellsPerSide = config.NavCellsPerSide;
        int n2 = navCellsPerSide * navCellsPerSide;

        // classify
        var cost = new Fix64[n2];
        var speedDivisor = new Fix64[n2];
        var walk = new byte[n2];
        var row = agent.CostByArea;
        var speedRow = agent.SpeedDivisorByArea;
        var area = surface.Area;
        var blocked = surface.Blocked;
        var slope = heights.Slope;
        for (int i = 0; i < n2; i++)
        {
            int areaIndex = area[i];
            Fix64 c = row[areaIndex];
            Fix64 d = speedRow[areaIndex];
            if (c > Fix64.Zero && !config.NavAreas[areaIndex].SlopeFree && slope[i] > maxSlope)
            {
                c = Fix64.Zero;
                d = Fix64.Zero;
            }

            cost[i] = c;
            speedDivisor[i] = d;
            walk[i] = c > Fix64.Zero && blocked[i] == 0 ? (byte)1 : (byte)0;
        }

        // 净空腐蚀（切比雪夫,可分离两次扫描;地图边缘视为阻挡）
        var passable = new byte[n2];
        Erode(walk, navCellsPerSide, clearanceCells, passable);

        // 升序可走格表
        var cellList = new List<int>(n2 / 2);
        for (int i = 0; i < n2; i++)
        {
            if (passable[i] != 0) cellList.Add(i);
        }

        // 连通域:4 连通洪泛(与参考实现的 tile 局部标号 + 边界合并同划分)
        var comp = new int[n2];
        Array.Fill(comp, -1);
        int compCount = Components(passable, navCellsPerSide, comp);

        // 桥面层(RT-16):桥面格烘焙 + portal 合并进连通域
        var upPass = new byte[n2];
        var portal = new byte[n2];
        var upArea = new byte[n2];
        var upCost = new Fix64[n2];
        var upSpeedDivisor = new Fix64[n2];
        if (deck.TileOrder.Length > 0)
        {
            UpperLayerBake.BakeDeck(config, agentTypeIndex, clearanceCells, walk, deck.AreaIndex, deck.PortalFlag, upPass, portal);
            for (int i = 0; i < n2; i++)
            {
                // DeckSurface.AreaIndex 是 +1 编码(0 = 无桥面);UpArea 存 0 基区域下标供 tile 缓存输入
                if (upPass[i] != 0)
                {
                    upArea[i] = (byte)(deck.AreaIndex[i] - 1);
                    upCost[i] = agent.CostByArea[upArea[i]];
                    upSpeedDivisor[i] = agent.SpeedDivisorByArea[upArea[i]];
                }
            }

            var merged = new int[n2];
            compCount = UpperLayerBake.MergeDeckComponents(comp, compCount, passable, upPass, portal, navCellsPerSide, merged);
            comp = merged;
        }

        // tile 缓存填充(与 buildNavContext 同点同序):地面全部 tile + 有可走格的桥面 tile
        NavTileEntry?[]? tiles = null;
        Dictionary<int, UpperTileInfo>? upperTiles = null;
        if (tileCache != null)
        {
            int navCellsPerTile = config.Hpa.NavCellsPerTile, navTilesPerSide = navCellsPerSide / navCellsPerTile;
            tiles = new NavTileEntry?[navTilesPerSide * navTilesPerSide];
            for (int ty = 0; ty < navTilesPerSide; ty++)
            {
                for (int tx = 0; tx < navTilesPerSide; tx++)
                {
                    tiles[ty * navTilesPerSide + tx] = tileCache.Acquire(passable, surface.Area, navCellsPerSide, tx, ty);
                }
            }

            foreach (int tileId in deck.TileOrder)
            {
                int tx = tileId % navTilesPerSide, ty = tileId / navTilesPerSide;
                bool any = false;
                for (int y = ty * navCellsPerTile; y < ty * navCellsPerTile + navCellsPerTile && !any; y++)
                {
                    for (int x = tx * navCellsPerTile; x < tx * navCellsPerTile + navCellsPerTile; x++)
                    {
                        if (upPass[y * navCellsPerSide + x] != 0) { any = true; break; }
                    }
                }

                if (!any) continue;
                var entry = tileCache.Acquire(upPass, upArea, navCellsPerSide, tx, ty);
                var portals = new List<int>();
                var pc = new Fix64[entry.Count];
                var cnt = new int[entry.Count];
                for (int k = 0; k < navCellsPerTile * navCellsPerTile; k++)
                {
                    int cell = (ty * navCellsPerTile + k / navCellsPerTile) * navCellsPerSide + tx * navCellsPerTile + k % navCellsPerTile;
                    if (portal[cell] != 0) portals.Add(k);
                    int p = entry.PolyOf[k];
                    if (p >= 0) { pc[p] += upCost[cell]; cnt[p]++; }
                }

                for (int p = 0; p < entry.Count; p++) pc[p] = cnt[p] > 0 ? pc[p] / cnt[p] : Fix64.OneValue;
                (upperTiles ??= new Dictionary<int, UpperTileInfo>())[tileId] = new UpperTileInfo
                {
                    Entry = entry,
                    Portals = portals.ToArray(),
                    PolyCost = pc,
                };
            }
        }

        var links = BuildLinks(config, surface, profile, passable, comp);
        var (upComp, compCountTotal, reachOut) = BuildDeckReachability(navCellsPerSide, comp, compCount, links, upPass, portal, upArea);

        // 最便宜定价区域代价(minCostOf):A* 启发缩放,只取已定价值的行项
        Fix64 minCost = Fix64.MaxValue;
        foreach (var areaCost in row)
        {
            if (areaCost > Fix64.Zero && areaCost < minCost) minCost = areaCost;
        }

        var nav = new NavContext
        {
            Id = CrowdSimulationSpace.NavContextId(agent.AgentTypeIndex, clearanceCells),
            LayerIndex = agentTypeIndex,
            ClearanceNavCells = clearanceCells,
            NavCellsPerSide = navCellsPerSide,
            Cost = cost,
            SpeedDivisor = speedDivisor,
            Walk = walk,
            Passable = passable,
            Cells = PackCells(cellList, n2),
            CellCount = cellList.Count,
            Comp = comp,
            CompCount = compCount,
            Links = links,
            UpPass = upPass,
            Portal = portal,
            UpArea = upArea,
            UpCost = upCost,
            UpSpeedDivisor = upSpeedDivisor,
            Tiles = tiles,
            UpperTiles = upperTiles,
            MinCost = minCost,
            UpComp = upComp,
            CompCountTotal = compCountTotal,
            ReachOut = reachOut,
        };
        nav.Hpa = HpaGraph.Build(nav, config);
        // 增量重烘的全图缓冲在装载期备好。第一次放置不再为它们分配。
        nav.FloodQueue = new int[n2];
        nav.MergeBuffer = new int[n2];
        nav.DeckCompBuffer = new int[n2];
        nav.UnionParent = new int[n2 * 2];
        nav.UnionDense = new int[n2 * 2];
        nav.ErodeRow = new byte[n2];
        nav.PrevTiles = new NavTileEntry?[64];
        nav.PrefillRebakePools();
        return nav;
    }

    private static int[] PackCells(List<int> cells, int n2)
    {
        var packed = new int[n2];
        for (int i = 0; i < cells.Count; i++) packed[i] = cells[i];
        return packed;
    }

    /// <summary>跳跃链接过滤（buildProfileLinks 移植）：候选端点都可走、格距 ≤ rangeCells、
    /// 落差 ≤ downCm 才成链;落差 ≤ upCm 的双向。判定只读 .navsurface 里的量化候选值。
    /// 增量重烘在候选端点触到变更矩形时重建整表(与全量同码)。</summary>
    internal static NavLinkSet? BuildLinks(
        CrowdSimulationRuntimeConfig config,
        SurfaceGrid surface,
        RuntimeNavProfile profile,
        byte[] passable,
        int[] comp)
    {
        var jump = profile.Jump;
        var candidates = surface.JumpCandidates;
        if (jump == null || candidates.Length == 0)
        {
            return null;
        }

        int navCellsPerSide = config.NavCellsPerSide, n2 = navCellsPerSide * navCellsPerSide;
        var from = new List<int>();
        var to = new List<int>();
        var cost = new List<Fix64>();
        var two = new List<byte>();
        var len = new List<float>();
        foreach (var cand in candidates)
        {
            int a = cand.FromY * navCellsPerSide + cand.FromX;
            int b = cand.ToY * navCellsPerSide + cand.ToX;
            if (passable[a] == 0 || passable[b] == 0 || cand.LengthCells > jump.RangeNavCells || cand.DropCm > jump.DownCm)
            {
                continue;
            }

            Fix64 c = Fix64.FromDouble(cand.LengthCells) * jump.Cost;
            bool up = cand.DropCm <= jump.UpCm;
            from.Add(a); to.Add(b); cost.Add(c); two.Add(up ? (byte)1 : (byte)0); len.Add(cand.LengthCells);
            if (up)
            {
                from.Add(b); to.Add(a); cost.Add(c); two.Add(1); len.Add(cand.LengthCells);
            }
        }

        if (from.Count == 0)
        {
            return null;
        }

        int count = from.Count;
        var inStart = Csr(to, n2, out var inList);
        var outStart = Csr(from, n2, out var outList);
        var compOut = new Dictionary<int, HashSet<int>>();
        for (int e = 0; e < count; e++)
        {
            int ca = comp[from[e]], cb = comp[to[e]];
            if (ca == cb) continue;
            if (!compOut.TryGetValue(ca, out var set))
            {
                compOut[ca] = set = new HashSet<int>();
            }

            set.Add(cb);
        }

        return new NavLinkSet
        {
            Count = count,
            From = from.ToArray(),
            To = to.ToArray(),
            Cost = cost.ToArray(),
            TwoWay = two.ToArray(),
            LengthCells = len.ToArray(),
            OutStart = outStart,
            OutList = outList,
            InStart = inStart,
            InList = inList,
            CompOut = compOut,
        };
    }

    private static int[] Csr(List<int> keys, int cellCount, out int[] list)
    {
        var start = new int[cellCount + 1];
        foreach (int k in keys) start[k + 1]++;
        for (int c = 0; c < cellCount; c++) start[c + 1] += start[c];
        var fill = new int[cellCount];
        Array.Copy(start, fill, cellCount);
        list = new int[keys.Count];
        for (int e = 0; e < keys.Count; e++) list[fill[keys[e]]++] = e;
        return start;
    }

    /// <summary>
    /// 桥面可达性(移植:上层标号接在地面连通域之后顺排):桥面区域(UpArea)按格升序首见
    /// 分配编号(确定性);桥头格双层可走 ⇒ 该格地面域 ↔ 桥面域双向互通边。可达图 = 跳跃链接边 + 互通边。
    /// </summary>
    internal static (int[] UpComp, int CompCountTotal, Dictionary<int, HashSet<int>> ReachOut) BuildDeckReachability(
        int navCellsPerSide, int[] comp, int compCount, NavLinkSet? links, byte[] upPass, byte[] portal, byte[] upArea)
    {
        int n2 = navCellsPerSide * navCellsPerSide;
        var reachOut = new Dictionary<int, HashSet<int>>();
        if (links != null)
        {
            foreach (var kv in links.CompOut) reachOut[kv.Key] = new HashSet<int>(kv.Value);
        }

        var upComp = new int[n2];
        Array.Fill(upComp, -1);
        var areaComp = new Dictionary<int, int>();
        int next = compCount;
        for (int c = 0; c < n2; c++)
        {
            if (upPass[c] == 0) continue;
            int area = upArea[c];
            if (!areaComp.TryGetValue(area, out int id))
            {
                id = next++;
                areaComp[area] = id;
            }

            upComp[c] = id;
        }

        for (int c = 0; c < n2; c++)
        {
            if (portal[c] == 0 || comp[c] < 0 || upPass[c] == 0) continue;
            int ground = comp[c], deck = upComp[c];
            AddEdge(reachOut, ground, deck);
            AddEdge(reachOut, deck, ground);
        }

        return (upComp, next, reachOut);

        static void AddEdge(Dictionary<int, HashSet<int>> graph, int from, int to)
        {
            if (!graph.TryGetValue(from, out var set)) graph[from] = set = new HashSet<int>();
            set.Add(to);
        }
    }

    /// <summary>连通域标号(4 连通洪泛,格号升序首见分配编号):全量烘焙与增量重烘共用一份划分。</summary>
    internal static int Components(byte[] passable, int navCellsPerSide, int[] comp, int[]? queue = null)
    {
        int n2 = navCellsPerSide * navCellsPerSide, compCount = 0;
        queue ??= new int[n2];
        for (int s = 0; s < n2; s++)
        {
            if (passable[s] == 0 || comp[s] >= 0) continue;
            int head = 0, tail = 0;
            queue[tail++] = s;
            comp[s] = compCount;
            while (head < tail)
            {
                int c = queue[head++];
                int x = c % navCellsPerSide, y = c / navCellsPerSide;
                if (x > 0 && passable[c - 1] != 0 && comp[c - 1] < 0) { comp[c - 1] = compCount; queue[tail++] = c - 1; }
                if (x < navCellsPerSide - 1 && passable[c + 1] != 0 && comp[c + 1] < 0) { comp[c + 1] = compCount; queue[tail++] = c + 1; }
                if (y > 0 && passable[c - navCellsPerSide] != 0 && comp[c - navCellsPerSide] < 0) { comp[c - navCellsPerSide] = compCount; queue[tail++] = c - navCellsPerSide; }
                if (y < navCellsPerSide - 1 && passable[c + navCellsPerSide] != 0 && comp[c + navCellsPerSide] < 0) { comp[c + navCellsPerSide] = compCount; queue[tail++] = c + navCellsPerSide; }
            }

            compCount++;
        }

        return compCount;
    }

    /// <summary>切比雪夫净空腐蚀（erodeRect 移植）:先行扫描再列扫描,两遍各计连续可走段。</summary>
    private static void Erode(byte[] walk, int navCellsPerSide, int clearance, byte[] output)
    {
        int r = clearance - 1;
        if (r <= 0)
        {
            Array.Copy(walk, output, walk.Length);
            return;
        }

        int need = 2 * r + 1;
        // 行缓冲:w = 整行;边缘外永不写入,视为 0
        var row = new byte[navCellsPerSide * navCellsPerSide];
        for (int y = 0; y < navCellsPerSide; y++)
        {
            int baseIdx = y * navCellsPerSide;
            int run = 0;
            for (int c = 0; c < navCellsPerSide; c++)
            {
                run = walk[baseIdx + c] != 0 ? run + 1 : 0;
                int x = c - r;
                if (x >= 0) row[baseIdx + x] = run >= need ? (byte)1 : (byte)0;
            }
        }

        for (int x = 0; x < navCellsPerSide; x++)
        {
            int run = 0;
            for (int yy = 0; yy < navCellsPerSide; yy++)
            {
                run = row[yy * navCellsPerSide + x] != 0 ? run + 1 : 0;
                int y = yy - r;
                if (y >= 0) output[y * navCellsPerSide + x] = run >= need ? (byte)1 : (byte)0;
            }

            for (int y = Math.Max(0, navCellsPerSide - r); y < navCellsPerSide; y++) output[y * navCellsPerSide + x] = 0;
        }
    }
}
