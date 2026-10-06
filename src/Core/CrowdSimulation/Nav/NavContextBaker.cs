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
/// 未定价区域不可走;非 slopeFree 区域坡度超过 profile 上限不可走;阻挡格不可走。
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
        if (heights.CellCount != config.NavCellCount || surface.CellCount != config.NavCellCount)
        {
            throw new InvalidOperationException("NavContextBaker: 高度场 / 地表 / 配置的导航格数不一致。");
        }

        var agent = config.AgentTypes[agentTypeIndex];
        var profile = config.NavProfiles[agent.NavProfileIndex];
        Fix64 maxSlope = profile.MaxSlopeDeg is { } deg
            ? Fix64Math.Tan(deg * Fix64.Deg2Rad)
            : Fix64.MaxValue;

        int n = config.NavCellCount;
        int n2 = n * n;

        // classify
        var cost = new Fix64[n2];
        var walk = new byte[n2];
        var row = agent.CostByArea;
        var area = surface.Area;
        var blocked = surface.Blocked;
        var slope = heights.Slope;
        for (int i = 0; i < n2; i++)
        {
            Fix64 c = row[area[i]];
            if (c > Fix64.Zero && !config.NavAreas[area[i]].SlopeFree && slope[i] > maxSlope)
            {
                c = Fix64.Zero;
            }

            cost[i] = c;
            walk[i] = c > Fix64.Zero && blocked[i] == 0 ? (byte)1 : (byte)0;
        }

        // 净空腐蚀（切比雪夫,可分离两次扫描;地图边缘视为阻挡）
        var passable = new byte[n2];
        Erode(walk, n, clearanceCells, passable);

        // 升序可走格表
        var cellList = new List<int>(n2 / 2);
        for (int i = 0; i < n2; i++)
        {
            if (passable[i] != 0) cellList.Add(i);
        }

        // 连通域:4 连通洪泛(与参考实现的 tile 局部标号 + 边界合并同划分)
        var comp = new int[n2];
        Array.Fill(comp, -1);
        int compCount = 0;
        var queue = new int[n2];
        for (int s = 0; s < n2; s++)
        {
            if (passable[s] == 0 || comp[s] >= 0) continue;
            int head = 0, tail = 0;
            queue[tail++] = s;
            comp[s] = compCount;
            while (head < tail)
            {
                int c = queue[head++];
                int x = c % n, y = c / n;
                if (x > 0 && passable[c - 1] != 0 && comp[c - 1] < 0) { comp[c - 1] = compCount; queue[tail++] = c - 1; }
                if (x < n - 1 && passable[c + 1] != 0 && comp[c + 1] < 0) { comp[c + 1] = compCount; queue[tail++] = c + 1; }
                if (y > 0 && passable[c - n] != 0 && comp[c - n] < 0) { comp[c - n] = compCount; queue[tail++] = c - n; }
                if (y < n - 1 && passable[c + n] != 0 && comp[c + n] < 0) { comp[c + n] = compCount; queue[tail++] = c + n; }
            }

            compCount++;
        }

        // 桥面层(RT-16):桥面格烘焙 + portal 合并进连通域
        var upPass = new byte[n2];
        var portal = new byte[n2];
        var upArea = new byte[n2];
        var upCost = new Fix64[n2];
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
                }
            }

            var merged = new int[n2];
            compCount = UpperLayerBake.MergeDeckComponents(comp, compCount, passable, upPass, portal, n, merged);
            comp = merged;
        }

        // tile 缓存填充(与 buildNavContext 同点同序):地面全部 tile + 有可走格的桥面 tile
        NavTileEntry?[]? tiles = null;
        if (tileCache != null)
        {
            int t = config.Hpa.ClusterSize, c = n / t;
            tiles = new NavTileEntry?[c * c];
            for (int ty = 0; ty < c; ty++)
            {
                for (int tx = 0; tx < c; tx++)
                {
                    tiles[ty * c + tx] = tileCache.Acquire(passable, surface.Area, n, tx, ty);
                }
            }

            foreach (int tileId in deck.TileOrder)
            {
                int tx = tileId % c, ty = tileId / c;
                bool any = false;
                for (int y = ty * t; y < ty * t + t && !any; y++)
                {
                    for (int x = tx * t; x < tx * t + t; x++)
                    {
                        if (upPass[y * n + x] != 0) { any = true; break; }
                    }
                }

                if (any) tileCache.Acquire(upPass, upArea, n, tx, ty);
            }
        }

        var links = BuildLinks(config, surface, profile, passable, comp);

        return new NavContext
        {
            Id = CrowdSimulationSpace.NavContextId(agent.Layer, clearanceCells),
            LayerIndex = agentTypeIndex,
            ClearanceCells = clearanceCells,
            CellCount = n,
            Cost = cost,
            Walk = walk,
            Passable = passable,
            Cells = cellList.ToArray(),
            Comp = comp,
            CompCount = compCount,
            Links = links,
            UpPass = upPass,
            Portal = portal,
            UpArea = upArea,
            UpCost = upCost,
            Tiles = tiles,
        };
    }

    /// <summary>
    /// 跳跃链接过滤（buildProfileLinks 移植）：候选端点都可走、格距 ≤ rangeCells、
    /// 落差 ≤ downCm 才成链;落差 ≤ upCm 的双向。判定只读 .navsurface 里的量化候选值。
    /// </summary>
    private static NavLinkSet? BuildLinks(
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

        int n = config.NavCellCount, n2 = n * n;
        var from = new List<int>();
        var to = new List<int>();
        var cost = new List<Fix64>();
        var two = new List<byte>();
        var len = new List<float>();
        foreach (var cand in candidates)
        {
            int a = cand.FromY * n + cand.FromX;
            int b = cand.ToY * n + cand.ToX;
            if (passable[a] == 0 || passable[b] == 0 || cand.LengthCells > jump.RangeCells || cand.DropCm > jump.DownCm)
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

    /// <summary>切比雪夫净空腐蚀（erodeRect 移植）:先行扫描再列扫描,两遍各计连续可走段。</summary>
    private static void Erode(byte[] walk, int n, int clearance, byte[] output)
    {
        int r = clearance - 1;
        if (r <= 0)
        {
            Array.Copy(walk, output, walk.Length);
            return;
        }

        int need = 2 * r + 1;
        // 行缓冲:w = 整行;边缘外永不写入,视为 0
        var row = new byte[n * n];
        for (int y = 0; y < n; y++)
        {
            int baseIdx = y * n;
            int run = 0;
            for (int c = 0; c < n; c++)
            {
                run = walk[baseIdx + c] != 0 ? run + 1 : 0;
                int x = c - r;
                if (x >= 0) row[baseIdx + x] = run >= need ? (byte)1 : (byte)0;
            }
        }

        for (int x = 0; x < n; x++)
        {
            int run = 0;
            for (int yy = 0; yy < n; yy++)
            {
                run = row[yy * n + x] != 0 ? run + 1 : 0;
                int y = yy - r;
                if (y >= 0) output[y * n + x] = run >= need ? (byte)1 : (byte)0;
            }

            for (int y = Math.Max(0, n - r); y < n; y++) output[y * n + x] = 0;
        }
    }
}
