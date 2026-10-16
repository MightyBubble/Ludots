using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 一个导航上下文（移动类型 × 净空）的烘焙产物：
/// 代价栅格、可走栅格（净空腐蚀后）、升序可走格表、连通域、链接。
/// </summary>
public sealed class NavContext
{
    public required int Id { get; init; }
    public required int LayerIndex { get; init; }
    public required int ClearanceNavCells { get; init; }
    public required int NavCellsPerSide { get; init; }
    /// <summary>逐格路径代价（0 = 不可通行）。A* 与流场读这张表。</summary>
    public required Fix64[] Cost { get; init; }
    /// <summary>
    /// 逐格速度除数。马达、意图和领队用速度除以这张表。
    /// schema 未写 speedDivisor 时除数等于路径代价，两张栅格逐格相同。
    /// </summary>
    public required Fix64[] SpeedDivisor { get; init; }
    /// <summary>逐格可走（代价 &amp; 坡度 &amp; 非阻挡,腐蚀前）。</summary>
    public required byte[] Walk { get; init; }
    /// <summary>逐格可通行（walk 经净空腐蚀,地图边缘视为阻挡）。</summary>
    public required byte[] Passable { get; init; }
    /// <summary>升序可走格表。Cells 的前 CellCount 项有效;缓冲可以比计数长,
    /// 增量重烘把新表写进已有缓冲,计数变化不再分配。</summary>
    public required int[] Cells { get; set; }
    public int CellCount { get; set; }
    /// <summary>底层格连通域编号（-1 = 不可通行）。先 4 连通洪泛；入口 portal 把同一条上层条带连上的底层域并成同一编号。链接不参与标号。</summary>
    public required int[] Comp { get; set; }
    public required int CompCount { get; set; }
    /// <summary>链接。无链接能力、没有候选、或过滤后一条链接都没有，三者都是 null。</summary>
    public required NavLinkSet? Links { get; set; }
    /// <summary>上层可走格（无上层时全 0）。</summary>
    public required byte[] UpPass { get; init; }
    /// <summary>入口 portal 格（上层与底层唯一互通处）。</summary>
    public required byte[] Portal { get; init; }
    /// <summary>上层可走格的区域编号（UpPass 处有效;tiles 缓存输入）。</summary>
    public required byte[] UpArea { get; init; }
    /// <summary>上层逐格代价（UpPass 处 = 区域代价;上行 HPA* / 流场用）。</summary>
    public required Fix64[] UpCost { get; init; }
    /// <summary>上层逐格速度除数。未写 speedDivisor 时与 UpCost 逐格相同。</summary>
    public required Fix64[] UpSpeedDivisor { get; init; }
    /// <summary>逐 tile 的缓存条目(拼装 / 查询的输入;仅在烘焙拿到缓存时填充)。</summary>
    public NavTileEntry?[]? Tiles { get; set; }
    /// <summary>上层 tile 条目(tile → 条目 + 入口格 + 逐多边形代价;无上层为 null)。</summary>
    public Dictionary<int, UpperTileInfo>? UpperTiles { get; set; }
    /// <summary>该移动类型最便宜的已定价区域代价(A* 启发缩放)。</summary>
    public required Fix64 MinCost { get; init; }
    /// <summary>HPA* 抽象图(烘焙末尾随上下文一起构建)。</summary>
    public HpaGraph? Hpa { get; set; }
    /// <summary>上层格的连通域编号。按 upPass 的 4 连通分块，编号接在底层连通域之后，格号升序首见。-1 = 该格没有上层。不相连的上层条带各有各的号，即使区域相同。</summary>
    public int[]? UpComp { get; set; }
    /// <summary>连通域总数（底层编号用尽之后的下一个号），可达备忘的键步长。</summary>
    public int CompCountTotal { get; set; }
    /// <summary>合并可达图：链接出边，加上入口格上底层域与上层域的双向边。上层节点按上层连通块编号。</summary>
    public Dictionary<int, HashSet<int>>? ReachOut { get; set; }

    private readonly Dictionary<long, bool> _reachMemo = new(4096);
    private readonly HashSet<int> _reachSeen = new(1024);
    private readonly Queue<int> _reachQueue = new(1024);
    private readonly object _reachGate = new();
    internal readonly Stack<HashSet<int>> ReachSetPool = new(1024);
    // 增量重烘的全图缓冲。首次重烘分配，之后原地复用。CloneNav 不拷这些缓冲，变体上是 null，该变体第一次重烘才分配。
    internal int[]? FloodQueue;
    internal int[]? MergeBuffer;
    internal int[]? UpperLayerCompBuffer;
    internal int[]? UnionParent;
    internal int[]? UnionDense;
    internal byte[]? ErodeRow;
    internal byte[]? UpperLayerWin;
    internal byte[]? UpperLayerEroded;
    internal readonly List<int> TileScratch = new(16);
    internal readonly HashSet<int> TileSeenScratch = new(16);
    internal readonly List<int> ChangedTileList = new(16);
    internal readonly HashSet<int> SameTiles = new(16);
    internal NavTileEntry?[]? PrevTiles;

    private sealed class StitchKeyComparer : IEqualityComparer<(NavTileEntry A, int SideA, NavTileEntry B)>
    {
        public bool Equals((NavTileEntry A, int SideA, NavTileEntry B) x, (NavTileEntry A, int SideA, NavTileEntry B) y)
            => ReferenceEquals(x.A, y.A) && ReferenceEquals(x.B, y.B) && x.SideA == y.SideA;

        public int GetHashCode((NavTileEntry A, int SideA, NavTileEntry B) obj)
            => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.A), obj.SideA, RuntimeHelpers.GetHashCode(obj.B));
    }

    /// <summary>tile 边界缝合对的记忆化(随本上下文释放;每次全局拼装前清空)。</summary>
    internal readonly Dictionary<(NavTileEntry A, int SideA, NavTileEntry B), int[]> StitchMemo = new(new StitchKeyComparer());
    internal readonly object StitchMemoLock = new();

    public void ClearStitchMemo()
    {
        lock (StitchMemoLock)
        {
            StitchMemo.Clear();
        }
    }

    /// <summary>导航版本:cost / passable 增量重烘各 +1;在途规划与流场答复按它判陈旧。</summary>
    public int Version { get; set; }
    /// <summary>最近一次增量重烘写出的脏 tile。可走位翻转时排除缓存命中的同一条目；代价路径把矩形盖住的 tile 全部写入。null = 未重烘过。</summary>
    public List<int>? ChangedTiles { get; set; }
    /// <summary>最近一次增量重烘的类别(0 = 无变化,1 = 仅代价,2 = 可走位翻转)。</summary>
    public byte LastRebake { get; set; }

    /// <summary>连通域重标后失效可达备忘(comp 编号随重烘改变,旧键不再对应)。</summary>
    public void InvalidateReach() => _reachMemo.Clear();

    /// <summary>格在某层的连通域(0 底层 / 1 上层;上层格没有上层编号时 -1)。</summary>
    public int CompAt(int cell, int level = 0)
    {
        if (level == 0) return Comp[cell];
        return UpComp != null ? UpComp[cell] : -1;
    }

    /// <summary>连通域 a 能否到达 b(域内可走,域间靠链接与入口互通边)。</summary>
    public bool CanReach(int a, int b)
    {
        if (a == b) return a >= 0;
        if (a < 0 || b < 0) return false;
        var reachOut = ReachOut;
        if (reachOut == null) return false;
        // 整次 CanReach 都在 _reachGate 里，命中和未命中都串行。搜索暂存挂在上下文上。
        lock (_reachGate)
        {
            long key = (long)a * CompCountTotal + b;
            if (_reachMemo.TryGetValue(key, out bool cached)) return cached;

            var seen = _reachSeen;
            var queue = _reachQueue;
            seen.Clear();
            queue.Clear();
            seen.Add(a);
            queue.Enqueue(a);
            bool result = false;
            while (queue.Count > 0 && !result)
            {
                int cur = queue.Dequeue();
                if (!reachOut.TryGetValue(cur, out var next)) continue;
                foreach (int c in next)
                {
                    if (c == b) { result = true; break; }
                    if (seen.Add(c)) queue.Enqueue(c);
                }
            }

            _reachMemo[key] = result;
            return result;
        }
    }

    internal HashSet<int> RentReachSet()
    {
        if (!ReachSetPool.TryPop(out var set)) set = new HashSet<int>(128);
        return set;
    }

    /// <summary>重烘会按新连通域号租出集合。装载期把池留够，放置 tick 不再 new。
    /// 初始烘焙留下的小集合不回收：它们一加边就会再分配。</summary>
    internal void PrefillRebakePools()
    {
        TileScratch.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        TileSeenScratch.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        ChangedTileList.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        SameTiles.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        ReachOut?.EnsureCapacity(512);
        if (ReachOut != null) _ = ReachOut.Values.Count;
        while (ReachSetPool.Count < CrowdSimulationLimits.HotPathSpanReserve) ReachSetPool.Push(new HashSet<int>(128));
        if (Links != null)
        {
            Links.CompOut.EnsureCapacity(512);
            _ = Links.CompOut.Values.Count;
            while (Links.CompSetPool.Count < CrowdSimulationLimits.HotPathSpanReserve) Links.CompSetPool.Push(new HashSet<int>(128));
        }
    }

    internal void ReleaseReachGraph(Dictionary<int, HashSet<int>> graph)
    {
        foreach (var set in graph.Values)
        {
            // 初始烘焙的小集合一回池就会压在预填集合上面,下一次 Add 再扩容。
            // 容量够的才回收。EnsureCapacity(0) 只读当前槽位数,不增长。
            int slots = set.EnsureCapacity(0);
            set.Clear();
            if (slots >= 128) ReachSetPool.Push(set);
        }

        graph.Clear();
    }
}

/// <summary>上层 tile 的查询视图。</summary>
public sealed class UpperTileInfo
{
    public required NavTileEntry Entry { get; init; }
    /// <summary>入口 portal 格(tile 内 T² 下标)——与底层唯一的互通处。</summary>
    public required int[] Portals { get; init; }
    /// <summary>逐多边形代价(上层区域代价按多边形格均值)。</summary>
    public required Fix64[] PolyCost { get; init; }
}

/// <summary>
/// 有向离网链接集（穿越）：from → to,cost = 格距 × 链接代价;two = 双向。
/// 备忘在 NavContext 上。链接集只有 CompOut（穿越边）；入口边在 ReachOut。
/// </summary>
public sealed class NavLinkSet
{
    public required int Count { get; init; }
    public required int[] From { get; init; }
    public required int[] To { get; init; }
    public required Fix64[] Cost { get; init; }
    public required byte[] TwoWay { get; init; }
    public required float[] LengthCells { get; init; }
    public required int[] OutStart { get; init; }
    public required int[] OutList { get; init; }
    public required int[] InStart { get; init; }
    public required int[] InList { get; init; }
    /// <summary>连通域 → 可达连通域（有向;只含链接边,入口互通边在 NavContext.ReachOut 合并）。
    /// 增量重烘的 relink 会按新连通域标号整表重导出(链表本体不动)。</summary>
    public required Dictionary<int, HashSet<int>> CompOut { get; set; }
    internal readonly Stack<HashSet<int>> CompSetPool = new(1024);
}
