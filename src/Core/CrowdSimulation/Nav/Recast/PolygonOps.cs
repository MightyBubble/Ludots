using System;
using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Nav.Recast;

/// <summary>
/// 多边形算子（recast/polys.js 移植）：面积 / 反转 / 洞合并 / 耳切三角化 / 凸合并。
/// 全部是格角点整数几何。平局次序用输入序号作次键,与稳定排序一致。
/// </summary>
public static class PolygonOps
{
    public static int Area2(List<int> p)
    {
        int a = 0;
        int n = p.Count >> 1;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            a += p[i * 2] * p[j * 2 + 1] - p[j * 2] * p[i * 2 + 1];
        }

        return a;
    }

    internal static List<int> Reversed(List<int> p, TileBakeScratch scratch)
    {
        var output = scratch.RentLoop();
        scratch.Borrowed.Add(output);
        for (int i = (p.Count >> 1) - 1; i >= 0; i--) { output.Add(p[i * 2]); output.Add(p[i * 2 + 1]); }
        return output;
    }

    private static int Orient(int ax, int ay, int bx, int by, int cx, int cy)
        => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

    private static bool ProperCross(int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy)
    {
        int d1 = Orient(cx, cy, dx, dy, ax, ay), d2 = Orient(cx, cy, dx, dy, bx, by);
        int d3 = Orient(ax, ay, bx, by, cx, cy), d4 = Orient(ax, ay, bx, by, dx, dy);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static bool Blocked(int ox, int oy, int hx, int hy, List<List<int>> loops)
    {
        foreach (var l in loops)
        {
            int n = l.Count >> 1;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int ax = l[i * 2], ay = l[i * 2 + 1], bx = l[j * 2], by = l[j * 2 + 1];
                if ((ax == ox && ay == oy) || (bx == ox && by == oy) || (ax == hx && ay == hy) || (bx == hx && by == hy)) continue;
                if (ProperCross(ox, oy, hx, hy, ax, ay, bx, by)) return true;
            }
        }

        return false;
    }

    /// <summary>洞合并:每个洞的最左顶点桥接到最近可见的外环顶点。最左 x 相同时保洞的输入序;距离相同时保顶点序。</summary>
    internal static List<int> MergeHoles(List<int> outer, List<List<int>> holes, TileBakeScratch scratch)
    {
        var ranks = scratch.Polys;
        int holeCount = holes.Count;
        StableOrder.Ensure(ref ranks.HoleRank, holeCount);
        StableOrder.Ensure(ref ranks.HoleVertex, holeCount);
        for (int h = 0; h < holeCount; h++)
        {
            var poly = holes[h];
            int hi = 0;
            for (int i = 1; i < (poly.Count >> 1); i++)
            {
                if (poly[i * 2] < poly[hi * 2] || (poly[i * 2] == poly[hi * 2] && poly[i * 2 + 1] < poly[hi * 2 + 1])) hi = i;
            }

            ranks.HoleVertex[h] = hi;
            ranks.HoleRank[h] = new StableInt(poly[hi * 2], h);
        }

        StableOrder.Sort(ranks.HoleRank, holeCount);
        for (int ordered = 0; ordered < holeCount; ordered++)
        {
            int hole = ranks.HoleRank[ordered].Seq;
            var h = holes[hole];
            int hi = ranks.HoleVertex[hole];
            int hx = h[hi * 2], hy = h[hi * 2 + 1];
            int on = outer.Count >> 1;
            StableOrder.Ensure(ref ranks.CandRank, on);
            for (int i = 0; i < on; i++)
            {
                long dx = outer[i * 2] - hx, dy = outer[i * 2 + 1] - hy;
                ranks.CandRank[i] = new StableLong(dx * dx + dy * dy, i);
            }

            StableOrder.Sort(ranks.CandRank, on);
            int pick = ranks.CandRank[0].Seq;
            var loops = scratch.HoleLoops;
            loops.Clear();
            loops.Add(outer);
            loops.AddRange(holes);
            for (int k = 0; k < Math.Min(16, on); k++)
            {
                int i = ranks.CandRank[k].Seq;
                if (!Blocked(outer[i * 2], outer[i * 2 + 1], hx, hy, loops)) { pick = i; break; }
            }

            int hn = h.Count >> 1;
            var merged = scratch.MergeTarget();
            for (int k = 0; k <= pick; k++) { merged.Add(outer[k * 2]); merged.Add(outer[k * 2 + 1]); }
            for (int m = 0; m <= hn; m++) { int k = (hi + m) % hn; merged.Add(h[k * 2]); merged.Add(h[k * 2 + 1]); }
            for (int k = pick; k < on; k++) { merged.Add(outer[k * 2]); merged.Add(outer[k * 2 + 1]); }
            outer = merged;
        }

        return outer;
    }

    /// <summary>耳切三角化(正向简单多边形,允许桥接洞)。返回顶点下标三元组。</summary>
    internal static List<int> Triangulate(List<int> p, TileBakeScratch scratch)
    {
        int n = p.Count >> 1;
        var idx = scratch.TriIdx;
        idx.Clear();
        for (int i = 0; i < n; i++) idx.Add(i);
        static int X(List<int> poly, int i) => poly[i * 2];
        static int Y(List<int> poly, int i) => poly[i * 2 + 1];
        static int Cross(List<int> poly, int a, int b, int c) => (X(poly, b) - X(poly, a)) * (Y(poly, c) - Y(poly, a)) - (Y(poly, b) - Y(poly, a)) * (X(poly, c) - X(poly, a));
        static bool Same(List<int> poly, int u, int v) => X(poly, u) == X(poly, v) && Y(poly, u) == Y(poly, v);
        var tris = scratch.Tris;
        tris.Clear();
        int k = 0, fails = 0;
        while (idx.Count > 3)
        {
            int m = idx.Count;
            if (k >= m) k = 0;
            int a = idx[(k + m - 1) % m], b = idx[k], c = idx[(k + 1) % m];
            bool ear = Cross(p, a, b, c) > 0;
            if (ear)
            {
                for (int t = 0; t < m; t++)
                {
                    int v = idx[t];
                    if (v == a || v == b || v == c || Same(p, v, a) || Same(p, v, b) || Same(p, v, c)) continue;
                    if (Cross(p, a, b, v) >= 0 && Cross(p, b, c, v) >= 0 && Cross(p, c, a, v) >= 0) { ear = false; break; }
                }
            }

            if (ear || fails > m)
            {
                if (Cross(p, a, b, c) > 0) { tris.Add(a); tris.Add(b); tris.Add(c); }
                idx.RemoveAt(k);
                fails = 0;
                if (k > 0) k--;
            }
            else { k++; fails++; }
        }

        if (idx.Count == 3 && Cross(p, idx[0], idx[1], idx[2]) > 0) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
        return tris;
    }

    private const int KEY = 1 << 21;

    /// <summary>凸合并:沿最长共享边贪婪合并邻居,保持凸性与顶点数上限。</summary>
    internal static List<int[]> MergePolys(List<int[]> polys, IReadOnlyList<int> vx, IReadOnlyList<int> vy, int maxVerts, TileBakeScratch scratch)
    {
        static bool Convex(List<int> p, IReadOnlyList<int> vx, IReadOnlyList<int> vy)
        {
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                int a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
                if ((vx[b] - vx[a]) * (vy[c] - vy[a]) - (vy[b] - vy[a]) * (vx[c] - vx[a]) < 0) return false;
            }

            return true;
        }

        for (; ; )
        {
            // 插入序边表(JS Map 语义:同键覆盖不挪位)
            var edges = scratch.Edges;
            var edgeIndex = scratch.EdgeIndex;
            edges.Clear();
            edgeIndex.Clear();
            for (int pi = 0; pi < polys.Count; pi++)
            {
                var p = polys[pi];
                if (p == null) continue;
                for (int e = 0; e < p.Length; e++)
                {
                    int key = p[e] * KEY + p[(e + 1) % p.Length];
                    int val = pi * 16 + e;
                    if (edgeIndex.TryGetValue(key, out int at)) edges[at] = (key, val);
                    else { edgeIndex[key] = edges.Count; edges.Add((key, val)); }
                }
            }

            int best = -1; int[]? bm = null; int bi = -1, bj = -1;
            foreach (var (key, val) in edges)
            {
                int a = key / KEY, b = key % KEY;
                if (!edgeIndex.TryGetValue(b * KEY + a, out int revAt)) continue;
                int rev = edges[revAt].Val;
                int pi = val >> 4, pj = rev >> 4;
                if (pi >= pj) continue;
                var A = polys[pi]!; var B = polys[pj]!;
                if (A.Length + B.Length - 2 > maxVerts) continue;
                int len = (vx[a] - vx[b]) * (vx[a] - vx[b]) + (vy[a] - vy[b]) * (vy[a] - vy[b]);
                if (len <= best) continue;
                int ea = val & 15, eb = rev & 15;
                var merged = scratch.Merged;
                merged.Clear();
                for (int k = 0; k < A.Length; k++) merged.Add(A[(ea + 1 + k) % A.Length]);
                for (int k = 0; k < B.Length - 2; k++) merged.Add(B[(eb + 2 + k) % B.Length]);
                if (!Convex(merged, vx, vy)) continue;
                best = len;
                if (bm != null) scratch.ReturnVerts(bm);
                bm = scratch.RentVerts(merged.Count);
                for (int k = 0; k < merged.Count; k++) bm[k] = merged[k];
                bi = pi; bj = pj;
            }

            if (bm == null) break;
            scratch.ReturnVerts(polys[bi]);
            scratch.ReturnVerts(polys[bj]);
            polys[bi] = bm;
            polys[bj] = null!;
        }

        int write = 0;
        for (int i = 0; i < polys.Count; i++)
        {
            if (polys[i] != null) polys[write++] = polys[i];
        }

        if (write != polys.Count) polys.RemoveRange(write, polys.Count - write);
        return polys;
    }
}

/// <summary>单次 tile 烘焙内复用的洞/候选排序缓冲。归 NavTileCache,不跨缓存共享。</summary>
public sealed class PolygonBakeScratch
{
    internal StableInt[] HoleRank = Array.Empty<StableInt>();
    internal int[] HoleVertex = Array.Empty<int>();
    internal StableLong[] CandRank = Array.Empty<StableLong>();
}
