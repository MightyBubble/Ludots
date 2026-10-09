using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 一个导航上下文（移动类型 × 净空）的烘焙产物（RT-03 的 S2 子集）：
/// 代价栅格、可走栅格（净空腐蚀后）、升序可走格表、连通域、跳跃链接。
/// NavMesh 多边形 / HPA* / tile 缓存随 S3 接入。
/// </summary>
public sealed class NavContext
{
    public required int Id { get; init; }
    public required int LayerIndex { get; init; }
    public required int ClearanceCells { get; init; }
    public required int CellCount { get; init; }
    /// <summary>逐格代价（0 = 不可通行）。</summary>
    public required Fix64[] Cost { get; init; }
    /// <summary>逐格可走（代价 &amp; 坡度 &amp; 非阻挡,腐蚀前）。</summary>
    public required byte[] Walk { get; init; }
    /// <summary>逐格可通行（walk 经净空腐蚀,地图边缘视为阻挡）。</summary>
    public required byte[] Passable { get; init; }
    /// <summary>升序可走格表。</summary>
    public required int[] Cells { get; set; }
    /// <summary>逐格连通域编号（-1 = 不可通行;4 连通洪泛,跳跃链接不参与标号）。</summary>
    public required int[] Comp { get; set; }
    public required int CompCount { get; set; }
    /// <summary>跳跃链接（无跳跃能力的移动类型为 null）。</summary>
    public required NavLinkSet? Links { get; set; }
    /// <summary>桥面层可走格（RT-16;无桥时全 0）。</summary>
    public required byte[] UpPass { get; init; }
    /// <summary>桥头 portal 格（桥面与地面唯一互通处）。</summary>
    public required byte[] Portal { get; init; }
    /// <summary>桥面可走格的区域编号（UpPass 处有效;tiles 缓存输入）。</summary>
    public required byte[] UpArea { get; init; }
    /// <summary>桥面逐格代价（UpPass 处 = 区域代价;上行 HPA* / 流场用）。</summary>
    public required Fix64[] UpCost { get; init; }
    /// <summary>逐 tile 的缓存条目(拼装 / 查询的输入;仅在烘焙拿到缓存时填充)。</summary>
    public NavTileEntry?[]? Tiles { get; set; }
    /// <summary>桥面 tile 条目(tile → 条目 + 桥头格 + 逐多边形代价;无桥为 null)。</summary>
    public Dictionary<int, UpperTileInfo>? UpperTiles { get; set; }
    /// <summary>该移动类型最便宜的已定价区域代价(A* 启发缩放,minCostOf 移植)。</summary>
    public required Fix64 MinCost { get; init; }
    /// <summary>HPA* 抽象图(烘焙末尾随上下文一起构建,与 buildNavContext 同点)。</summary>
    public HpaGraph? Hpa { get; set; }
    /// <summary>桥面格 → 全局连通域编号(地面域之后顺排;-1 = 无桥面)。</summary>
    public int[]? UpComp { get; set; }
    /// <summary>连通域总数(地面 + 桥面),可达备忘的键步长。</summary>
    public int CompCountTotal { get; set; }
    /// <summary>合并可达图:跳跃链接出边 + 桥头双层互通边(桥面域 = 两岸地面的接驳节)。</summary>
    public Dictionary<int, HashSet<int>>? ReachOut { get; set; }

    private readonly Dictionary<long, bool> _reachMemo = new();

    /// <summary>导航版本:cost / passable 增量重烘各 +1;在途规划与流场答复按它判陈旧。</summary>
    public int Version { get; set; }
    /// <summary>最近一次增量重烘里内容真变的 tile(缓存命中同条目的除外;F02 认知原位揭示
    /// 的脏集口径,与参考端 nav.changedTiles 同义)。null = 未重烘过。</summary>
    public List<int>? ChangedTiles { get; set; }
    /// <summary>最近一次增量重烘的类别(0 = 无变化,1 = 仅代价,2 = 可走位翻转)。</summary>
    public byte LastRebake { get; set; }

    /// <summary>连通域重标后失效可达备忘(comp 编号随重烘改变,旧键不再对应)。</summary>
    public void InvalidateReach() => _reachMemo.Clear();

    /// <summary>格在某层的连通域(0 地面 / 1 桥面;桥面格没有桥面编号时 -1)。compAt 移植。</summary>
    public int CompAt(int cell, int level = 0)
    {
        if (level == 0) return Comp[cell];
        return UpComp != null ? UpComp[cell] : -1;
    }

    /// <summary>连通域 a 能否到达 b(域内可走,域间靠跳跃链接与桥头互通边;canReach 移植)。</summary>
    public bool CanReach(int a, int b)
    {
        if (a == b) return a >= 0;
        if (a < 0 || b < 0) return false;
        var reachOut = ReachOut;
        if (reachOut == null) return false;
        long key = (long)a * CompCountTotal + b;
        if (_reachMemo.TryGetValue(key, out bool cached)) return cached;

        var seen = new HashSet<int> { a };
        var queue = new Queue<int>(new[] { a });
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

/// <summary>桥面 tile 的查询视图(bakeUpper 的 { entry, portals, pc } 移植)。</summary>
public sealed class UpperTileInfo
{
    public required NavTileEntry Entry { get; init; }
    /// <summary>桥头 portal 格(tile 内 T² 下标)——与地面层唯一的互通处。</summary>
    public required int[] Portals { get; init; }
    /// <summary>逐多边形代价(桥面区域代价按多边形格均值)。</summary>
    public required Fix64[] PolyCost { get; init; }
}

/// <summary>
/// 有向离网链接集（跳跃）：from → to,cost = 格距 × 跳跃代价;two = 双向。
/// 携带连通域间的有向可达图（compOut）与 canReach 备忘。
/// </summary>
public sealed class NavLinkSet
{
    public required int Count { get; init; }
    public required int[] From { get; init; }
    public required int[] To { get; init; }
    public required Fix64[] Cost { get; init; }
    public required byte[] TwoWay { get; init; }
    public required float[] LengthCells { get; init; }
    /// <summary>CSR：格 → 出边。</summary>
    public required int[] OutStart { get; init; }
    public required int[] OutList { get; init; }
    /// <summary>CSR：格 → 入边。</summary>
    public required int[] InStart { get; init; }
    public required int[] InList { get; init; }
    /// <summary>连通域 → 可达连通域（有向;只含跳跃链接边,桥头互通边在 NavContext.ReachOut 合并）。
    /// 增量重烘的 relink 会按新连通域标号整表重导出(链表本体不动)。</summary>
    public required Dictionary<int, HashSet<int>> CompOut { get; set; }
}
