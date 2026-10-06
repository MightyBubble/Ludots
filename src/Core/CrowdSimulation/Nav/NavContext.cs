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
    public required int[] Cells { get; init; }
    /// <summary>逐格连通域编号（-1 = 不可通行;4 连通洪泛,跳跃链接不参与标号）。</summary>
    public required int[] Comp { get; init; }
    public required int CompCount { get; init; }
    /// <summary>跳跃链接（无跳跃能力的移动类型为 null）。</summary>
    public required NavLinkSet? Links { get; init; }
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
    /// <summary>连通域 → 可达连通域（有向）。</summary>
    public required Dictionary<int, HashSet<int>> CompOut { get; init; }

    private readonly Dictionary<long, bool> _reachMemo = new();

    /// <summary>连通域 a 能否到达 b（域内可走,域间靠有向链接）。</summary>
    public bool CanReach(int a, int b, int compCount)
    {
        if (a == b) return a >= 0;
        if (a < 0 || b < 0) return false;
        long key = (long)a * compCount + b;
        if (_reachMemo.TryGetValue(key, out bool cached)) return cached;

        var seen = new HashSet<int> { a };
        var queue = new Queue<int>(new[] { a });
        bool result = false;
        while (queue.Count > 0 && !result)
        {
            int cur = queue.Dequeue();
            if (!CompOut.TryGetValue(cur, out var next)) continue;
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
