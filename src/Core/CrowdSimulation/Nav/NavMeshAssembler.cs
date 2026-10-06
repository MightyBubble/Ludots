using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>全局 NavMesh 视图(assembleNavmesh 移植):跨 tile 拼接后的多边形与邻接/portal 边。</summary>
public sealed class NavMeshView
{
    public required int Count { get; init; }
    public required int RegionCount { get; init; }
    public required int[] Vx { get; init; }
    public required int[] Vy { get; init; }
    public required int[] PolyStart { get; init; }
    public required int[] PolyVerts { get; init; }
    public required int[] PolyTile { get; init; }
    public required int[] NeiStart { get; init; }
    public required int[] Nei { get; init; }
    /// <summary>邻接 / portal 边的两端点(格坐标,与 nei 平行)。</summary>
    public required int[] PaX { get; init; }
    public required int[] PaY { get; init; }
    public required int[] PbX { get; init; }
    public required int[] PbY { get; init; }
    public required int[] PolyOf { get; init; }
    /// <summary>逐多边形平均代价(格代价均值)。</summary>
    public required Fix64[] PolyCost { get; init; }
}

/// <summary>
/// 全局 NavMesh 拼装(assemble.js):拼接各 tile 的多边形(局部→全局格坐标)、
/// 保留 tile 内部邻接、跨 tile 边界的重叠边缝合成 portal(端点 = 精确重叠段)。
/// 纯拼装,无烘焙。条目不可变且被缓存共享,边界重叠对的搜索结果按条目对记忆化。
/// </summary>
public static class NavMeshAssembler
{
    // 边界重叠对(stitchPairs):(entryA uid, sideA, entryB uid) → [i, j, lo, hi]×k
    private static readonly Dictionary<(int, int, int), int[]> _stitchMemo = new();

    public static int[] StitchPairs(NavTileEntry ea, NavTileEntry eb, int sideA, int sideB)
    {
        var key = (ea.Uid, sideA, eb.Uid);
        if (_stitchMemo.TryGetValue(key, out var cached)) return cached;

        var res = new List<int>();
        var (polyA, sideArrA, loA, hiA) = (ea.BorderPoly, ea.BorderSide, ea.BorderLo, ea.BorderHi);
        var (polyB, sideArrB, loB, hiB) = (eb.BorderPoly, eb.BorderSide, eb.BorderLo, eb.BorderHi);
        for (int i = 0; i < polyA.Length; i++)
        {
            if (sideArrA[i] != sideA) continue;
            for (int j = 0; j < polyB.Length; j++)
            {
                if (sideArrB[j] != sideB) continue;
                int lo = Math.Max(loA[i], loB[j]), hi = Math.Min(hiA[i], hiB[j]);
                if (hi > lo) { res.Add(i); res.Add(j); res.Add(lo); res.Add(hi); }
            }
        }

        var output = res.ToArray();
        _stitchMemo[key] = output;
        return output;
    }

    /// <summary>拼装某上下文的全部 tile(传 null 的槽位 = 该 tile 无内容,按空处理)。</summary>
    public static NavMeshView Assemble(NavTileEntry?[] tiles, Fix64[] cost, int n, int tileCells)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        _stitchMemo.Clear(); // memo 键是缓存 uid,跨烘焙复用;新会话清空避免陈旧引用
        int c = n / tileCells, cc = c * c;

        var polyBase = new int[cc + 1];
        var vertBase = new int[cc + 1];
        var idxBase = new int[cc + 1];
        int regionCount = 0;
        for (int t = 0; t < cc; t++)
        {
            var e = tiles[t] ?? EmptyEntry;
            polyBase[t + 1] = polyBase[t] + e.Count;
            vertBase[t + 1] = vertBase[t] + e.Vx.Length;
            idxBase[t + 1] = idxBase[t] + e.PolyVerts.Length;
            regionCount += e.RegionCount;
        }

        int pCount = polyBase[cc], vCount = vertBase[cc];
        var vx = new int[vCount]; var vy = new int[vCount];
        var polyStart = new int[pCount + 1];
        var polyVerts = new int[idxBase[cc]];
        var polyTile = new int[pCount];
        var edgeP = new List<int>();
        var edgeQ = new List<int>();
        var edgeAx = new List<int>(); var edgeAy = new List<int>();
        var edgeBx = new List<int>(); var edgeBy = new List<int>();

        void Edge(int p, int q, int ax, int ay, int bx, int by)
        {
            edgeP.Add(p); edgeQ.Add(q);
            edgeAx.Add(ax); edgeAy.Add(ay); edgeBx.Add(bx); edgeBy.Add(by);
        }

        for (int t = 0; t < cc; t++)
        {
            var e = tiles[t] ?? EmptyEntry;
            int ox = (t % c) * tileCells, oy = (t / c) * tileCells;
            int pb = polyBase[t], vb = vertBase[t], ib = idxBase[t];
            for (int v = 0; v < e.Vx.Length; v++) { vx[vb + v] = e.Vx[v] + ox; vy[vb + v] = e.Vy[v] + oy; }
            for (int k = 0; k < e.PolyVerts.Length; k++) polyVerts[ib + k] = vb + e.PolyVerts[k];
            for (int p = 0; p < e.Count; p++)
            {
                int g = pb + p;
                polyStart[g + 1] = ib + e.PolyStart[p + 1];
                polyTile[g] = t;
                for (int k = e.NeiStart[p]; k < e.NeiStart[p + 1]; k++)
                {
                    int a = vb + e.NeiA[k], b = vb + e.NeiB[k];
                    Edge(g, pb + e.Nei[k], vx[a], vy[a], vx[b], vy[b]);
                }
            }
        }

        // 边界缝合:东邻(右侧边 vs 左侧边)与南邻(下边 vs 上边)
        void Portal(int p, int q, int rev, bool vertical, int line, int lo, int hi)
        {
            int a = rev != 0 ? hi : lo, b = rev != 0 ? lo : hi;
            if (vertical) Edge(p, q, line, a, line, b); else Edge(p, q, a, line, b, line);
        }

        void Stitch(int ta, int tb, int sa, int sb, bool vertical, int line, int off)
        {
            var ea = tiles[ta] ?? EmptyEntry;
            var eb = tiles[tb] ?? EmptyEntry;
            var pairs = StitchPairs(ea, eb, sa, sb);
            for (int k = 0; k < pairs.Length; k += 4)
            {
                int i = pairs[k], j = pairs[k + 1], lo = pairs[k + 2], hi = pairs[k + 3];
                int p = polyBase[ta] + ea.BorderPoly[i], q = polyBase[tb] + eb.BorderPoly[j];
                Portal(p, q, ea.BorderRev[i], vertical, line, off + lo, off + hi);
                Portal(q, p, eb.BorderRev[j], vertical, line, off + lo, off + hi);
            }
        }

        for (int ty = 0; ty < c; ty++)
        {
            for (int tx = 0; tx < c; tx++)
            {
                int t = ty * c + tx;
                if (tx + 1 < c) Stitch(t, t + 1, 1, 0, true, (tx + 1) * tileCells, ty * tileCells);
                if (ty + 1 < c) Stitch(t, t + c, 3, 2, false, (ty + 1) * tileCells, tx * tileCells);
            }
        }

        // CSR 化邻接
        var neiStart = new int[pCount + 1];
        for (int k = 0; k < edgeP.Count; k++) neiStart[edgeP[k] + 1]++;
        for (int p = 0; p < pCount; p++) neiStart[p + 1] += neiStart[p];
        var fill = new int[pCount];
        Array.Copy(neiStart, fill, pCount);
        var nei = new int[edgeP.Count];
        var pax = new int[edgeP.Count]; var pay = new int[edgeP.Count];
        var pbx = new int[edgeP.Count]; var pby = new int[edgeP.Count];
        for (int k = 0; k < edgeP.Count; k++)
        {
            int s = fill[edgeP[k]]++;
            nei[s] = edgeQ[k];
            pax[s] = edgeAx[k]; pay[s] = edgeAy[k];
            pbx[s] = edgeBx[k]; pby[s] = edgeBy[k];
        }

        // cell → 全局 poly;逐 poly 平均代价
        var polyOf = new int[n * n];
        Array.Fill(polyOf, -1);
        var polyCostAcc = new Fix64[pCount];
        var cnt = new int[pCount];
        for (int t = 0; t < cc; t++)
        {
            var e = tiles[t] ?? EmptyEntry;
            int ox = (t % c) * tileCells, oy = (t / c) * tileCells;
            int pb = polyBase[t];
            for (int y = 0; y < tileCells; y++)
            {
                for (int x = 0; x < tileCells; x++)
                {
                    int lp = e.PolyOf[y * tileCells + x];
                    if (lp < 0) continue;
                    int cell = (oy + y) * n + ox + x;
                    int g = pb + lp;
                    polyOf[cell] = g;
                    polyCostAcc[g] += cost[cell];
                    cnt[g]++;
                }
            }
        }

        var polyCost = new Fix64[pCount];
        for (int p = 0; p < pCount; p++) polyCost[p] = cnt[p] > 0 ? polyCostAcc[p] / cnt[p] : Fix64.OneValue;

        return new NavMeshView
        {
            Count = pCount,
            RegionCount = regionCount,
            Vx = vx,
            Vy = vy,
            PolyStart = polyStart,
            PolyVerts = polyVerts,
            PolyTile = polyTile,
            NeiStart = neiStart,
            Nei = nei,
            PaX = pax,
            PaY = pay,
            PbX = pbx,
            PbY = pby,
            PolyOf = polyOf,
            PolyCost = polyCost,
        };
    }

    private static readonly NavTileEntry EmptyEntry = new()
    {
        Uid = -1,
        Count = 0,
        RegionCount = 0,
        Vx = Array.Empty<int>(),
        Vy = Array.Empty<int>(),
        PolyStart = new[] { 0 },
        PolyVerts = Array.Empty<int>(),
        NeiStart = new[] { 0 },
        Nei = Array.Empty<int>(),
        NeiA = Array.Empty<int>(),
        NeiB = Array.Empty<int>(),
        BMinX = Array.Empty<int>(),
        BMinY = Array.Empty<int>(),
        BMaxX = Array.Empty<int>(),
        BMaxY = Array.Empty<int>(),
        PolyOf = Array.Empty<int>(),
        BorderPoly = Array.Empty<int>(),
        BorderSide = Array.Empty<byte>(),
        BorderLo = Array.Empty<int>(),
        BorderHi = Array.Empty<int>(),
        BorderRev = Array.Empty<byte>(),
    };
}
