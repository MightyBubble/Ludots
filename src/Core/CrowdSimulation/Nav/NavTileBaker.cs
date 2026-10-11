using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Nav.Recast;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 单块 nav tile 的烘焙产物(位置无关,内容相同的 tile 共享一份)。
/// 坐标为 tile 局部格角点(0..T);side:0 = x0,1 = xT,2 = y0,3 = yT。
/// </summary>
public sealed class NavTileEntry
{
    public required int Uid { get; set; }
    public required int Count { get; init; }
    public required int RegionCount { get; init; }
    public required int[] Vx { get; init; }
    public required int[] Vy { get; init; }
    public required int[] PolyStart { get; init; }
    public required int[] PolyVerts { get; init; }
    public required int[] NeiStart { get; init; }
    public required int[] Nei { get; init; }
    public required int[] NeiA { get; init; }
    public required int[] NeiB { get; init; }
    public required int[] BMinX { get; init; }
    public required int[] BMinY { get; init; }
    public required int[] BMaxX { get; init; }
    public required int[] BMaxY { get; init; }
    /// <summary>cell → poly(tile 内 T² 下标;点在凸多边形上 + 洪泛补齐)。</summary>
    public required int[] PolyOf { get; init; }
    public required int[] BorderPoly { get; init; }
    public required byte[] BorderSide { get; init; }
    public required int[] BorderLo { get; init; }
    public required int[] BorderHi { get; init; }
    public required byte[] BorderRev { get; init; }
}

/// <summary>
/// bakeTile 移植:tile 自身 T×T 窗口上的 2D Recast 管线
/// (距离场 → 分水岭区域 → 轮廓 → 三角化 → 凸多边形合并)。多边形永不跨导航区域。
/// </summary>
public static class NavTileBaker
{
    private const int KEY = 1 << 21;

    public static NavTileEntry Bake(ushort[] pass, int t, double minRegionArea, double maxSimplificationError, double maxEdgeLen, int maxVertsPerPoly, PolygonBakeScratch scratch)
    {
        int tt = t * t;
        var dist = DistanceField.ComputeClearance(ToBytes(pass, tt), t);
        var (reg, regionCount) = WatershedRegions.Build(ToBytes(pass, tt), dist, t, (int)minRegionArea);
        var loops = ContourTracer.BuildContours(reg, regionCount, t, maxSimplificationError, maxEdgeLen);

        var vmap = new Dictionary<int, int>();
        var vx = new List<int>();
        var vy = new List<int>();
        int Vid(int x, int y)
        {
            int k = y * (t + 1) + x;
            if (!vmap.TryGetValue(k, out int id))
            {
                id = vx.Count;
                vmap[k] = id;
                vx.Add(x);
                vy.Add(y);
            }

            return id;
        }

        var polys = new List<int[]>();
        for (int r = 1; r <= regionCount; r++)
        {
            var ls = loops[r];
            if (ls.Count == 0) continue;
            int oi = 0, best = -1;
            for (int k = 0; k < ls.Count; k++)
            {
                int a = Math.Abs(PolygonOps.Area2(ls[k]));
                if (a > best) { best = a; oi = k; }
            }

            var outer = PolygonOps.Area2(ls[oi]) > 0 ? ls[oi] : PolygonOps.Reversed(ls[oi]);
            var holes = new List<List<int>>();
            for (int k = 0; k < ls.Count; k++)
            {
                if (k == oi) continue;
                holes.Add(PolygonOps.Area2(ls[k]) < 0 ? ls[k] : PolygonOps.Reversed(ls[k]));
            }

            var poly = holes.Count > 0 ? PolygonOps.MergeHoles(outer, holes, scratch) : outer;
            var tri = PolygonOps.Triangulate(poly);
            var tp = new List<int[]>();
            for (int k = 0; k < tri.Count; k += 3)
            {
                int a = Vid(poly[tri[k] * 2], poly[tri[k] * 2 + 1]);
                int b = Vid(poly[tri[k + 1] * 2], poly[tri[k + 1] * 2 + 1]);
                int c = Vid(poly[tri[k + 2] * 2], poly[tri[k + 2] * 2 + 1]);
                if (a != b && b != c && a != c) tp.Add(new[] { a, b, c });
            }

            foreach (var p in PolygonOps.MergePolys(tp, vx, vy, maxVertsPerPoly)) polys.Add(p);
        }

        int pCount = polys.Count;
        var polyStart = new int[pCount + 1];
        for (int i = 0; i < pCount; i++) polyStart[i + 1] = polyStart[i] + polys[i].Length;
        var polyVerts = new int[polyStart[pCount]];
        var bminx = new int[pCount]; var bminy = new int[pCount];
        var bmaxx = new int[pCount]; var bmaxy = new int[pCount];
        var emap = new Dictionary<int, int>();
        var vxArr = vx.ToArray(); var vyArr = vy.ToArray();
        for (int i = 0; i < pCount; i++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            var p = polys[i];
            for (int k = 0; k < p.Length; k++)
            {
                int v = p[k];
                polyVerts[polyStart[i] + k] = v;
                x0 = Math.Min(x0, vxArr[v]); y0 = Math.Min(y0, vyArr[v]);
                x1 = Math.Max(x1, vxArr[v]); y1 = Math.Max(y1, vyArr[v]);
                emap[v * KEY + p[(k + 1) % p.Length]] = i;
            }

            bminx[i] = x0; bminy[i] = y0; bmaxx[i] = x1; bmaxy[i] = y1;
        }

        var nb = new List<int>();
        var bd = new List<int>();
        var neiStart = new int[pCount + 1];
        for (int i = 0; i < pCount; i++)
        {
            var p = polys[i];
            for (int k = 0; k < p.Length; k++)
            {
                int a = p[k], b = p[(k + 1) % p.Length];
                if (emap.TryGetValue(b * KEY + a, out int q) && q != i) { nb.Add(q); nb.Add(a); nb.Add(b); continue; }

                int ax = vxArr[a], ay = vyArr[a], bx = vxArr[b], by = vyArr[b];
                if (ax == bx && (ax == 0 || ax == t))
                {
                    bd.Add(i); bd.Add(ax == 0 ? 0 : 1); bd.Add(Math.Min(ay, by)); bd.Add(Math.Max(ay, by)); bd.Add(ay > by ? 1 : 0);
                }
                else if (ay == by && (ay == 0 || ay == t))
                {
                    bd.Add(i); bd.Add(ay == 0 ? 2 : 3); bd.Add(Math.Min(ax, bx)); bd.Add(Math.Max(ax, bx)); bd.Add(ax > bx ? 1 : 0);
                }
            }

            neiStart[i + 1] = nb.Count / 3;
        }

        int eCount = nb.Count / 3;
        var nei = new int[eCount]; var neiA = new int[eCount]; var neiB = new int[eCount];
        for (int k = 0; k < eCount; k++) { nei[k] = nb[k * 3]; neiA[k] = nb[k * 3 + 1]; neiB[k] = nb[k * 3 + 2]; }
        int bCount = bd.Count / 5;
        var borderPoly = new int[bCount]; var borderSide = new byte[bCount];
        var borderLo = new int[bCount]; var borderHi = new int[bCount]; var borderRev = new byte[bCount];
        for (int k = 0; k < bCount; k++)
        {
            borderPoly[k] = bd[k * 5]; borderSide[k] = (byte)bd[k * 5 + 1];
            borderLo[k] = bd[k * 5 + 2]; borderHi[k] = bd[k * 5 + 3]; borderRev[k] = (byte)bd[k * 5 + 4];
        }

        // cell → poly:格心点在凸多边形内,残余格先同区域邻居洪泛再任意可走邻居
        var polyOf = new int[tt];
        Array.Fill(polyOf, -1);
        var queue = new int[tt];
        int qt = 0;
        var passBytes = ToBytes(pass, tt);
        for (int p = 0; p < pCount; p++)
        {
            int s = polyStart[p], e = polyStart[p + 1];
            for (int y = bminy[p]; y < bmaxy[p]; y++)
            {
                for (int x = bminx[p]; x < bmaxx[p]; x++)
                {
                    int i = y * t + x;
                    if (passBytes[i] == 0 || polyOf[i] >= 0) continue;
                    double px = x + 0.5, py = y + 0.5;
                    bool inside = true;
                    for (int k = s; k < e && inside; k++)
                    {
                        int a = polyVerts[k], b = polyVerts[k + 1 < e ? k + 1 : s];
                        if ((vxArr[b] - vxArr[a]) * (py - vyArr[a]) - (vyArr[b] - vyArr[a]) * (px - vxArr[a]) < 0) inside = false;
                    }

                    if (inside) { polyOf[i] = p; queue[qt++] = i; }
                }
            }
        }

        foreach (var sameArea in new[] { true, false })
        {
            for (int qh = 0; qh < qt; qh++)
            {
                int c = queue[qh], x = c % t, y = c / t;
                Span<int> ns = stackalloc int[] { x > 0 ? c - 1 : -1, x < t - 1 ? c + 1 : -1, y > 0 ? c - t : -1, y < t - 1 ? c + t : -1 };
                foreach (int ni in ns)
                {
                    if (ni >= 0 && passBytes[ni] != 0 && polyOf[ni] < 0 && (!sameArea || passBytes[ni] == passBytes[c]))
                    {
                        polyOf[ni] = polyOf[c];
                        queue[qt++] = ni;
                    }
                }
            }
        }

        return new NavTileEntry
        {
            Uid = -1,
            Count = pCount,
            RegionCount = regionCount,
            Vx = vxArr,
            Vy = vyArr,
            PolyStart = polyStart,
            PolyVerts = polyVerts,
            NeiStart = neiStart,
            Nei = nei,
            NeiA = neiA,
            NeiB = neiB,
            BMinX = bminx,
            BMinY = bminy,
            BMaxX = bmaxx,
            BMaxY = bmaxy,
            PolyOf = polyOf,
            BorderPoly = borderPoly,
            BorderSide = borderSide,
            BorderLo = borderLo,
            BorderHi = borderHi,
            BorderRev = borderRev,
        };
    }

    private static byte[] ToBytes(ushort[] pass, int tt)
    {
        // 注意:pass 是区域码(0 = 阻挡,否则导航区域编号 + 1),不是 0/1——区域码是分水岭的分界语义
        var output = new byte[tt];
        for (int i = 0; i < tt; i++) output[i] = (byte)pass[i];
        return output;
    }
}
