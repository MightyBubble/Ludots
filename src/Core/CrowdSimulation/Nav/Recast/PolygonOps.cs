using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.Core.CrowdSimulation.Nav.Recast;

/// <summary>
/// 多边形算子（recast/polys.js 移植）：面积 / 反转 / 洞合并 / 耳切三角化 / 凸合并。
/// 全部是格角点整数几何;JS 稳定排序在 C# 用 OrderBy(稳定)或显式次键复现。
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

    public static List<int> Reversed(List<int> p)
    {
        var output = new List<int>(p.Count);
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

    /// <summary>洞合并:每个洞的最左顶点桥接到最近可见的外环顶点。</summary>
    public static List<int> MergeHoles(List<int> outer, List<List<int>> holes)
    {
        var hs = holes.Select(h =>
        {
            int hi = 0;
            for (int i = 1; i < (h.Count >> 1); i++)
            {
                if (h[i * 2] < h[hi * 2] || (h[i * 2] == h[hi * 2] && h[i * 2 + 1] < h[hi * 2 + 1])) hi = i;
            }

            return (H: h, Hi: hi);
        }).OrderBy(e => e.H[e.Hi * 2]).ToList(); // OrderBy 稳定 = JS sort 稳定

        foreach (var (h, hi) in hs)
        {
            int hx = h[hi * 2], hy = h[hi * 2 + 1];
            int on = outer.Count >> 1;
            var cand = new List<(long D, int I)>();
            for (int i = 0; i < on; i++)
            {
                long dx = outer[i * 2] - hx, dy = outer[i * 2 + 1] - hy;
                cand.Add((dx * dx + dy * dy, i));
            }

            cand = cand.OrderBy(c => c.D).ToList();
            int pick = cand[0].I;
            var loops = new List<List<int>> { outer };
            loops.AddRange(holes);
            for (int k = 0; k < Math.Min(16, cand.Count); k++)
            {
                int i = cand[k].I;
                if (!Blocked(outer[i * 2], outer[i * 2 + 1], hx, hy, loops)) { pick = i; break; }
            }

            int hn = h.Count >> 1;
            var merged = new List<int>();
            for (int k = 0; k <= pick; k++) { merged.Add(outer[k * 2]); merged.Add(outer[k * 2 + 1]); }
            for (int m = 0; m <= hn; m++) { int k = (hi + m) % hn; merged.Add(h[k * 2]); merged.Add(h[k * 2 + 1]); }
            for (int k = pick; k < on; k++) { merged.Add(outer[k * 2]); merged.Add(outer[k * 2 + 1]); }
            outer = merged;
        }

        return outer;
    }

    /// <summary>耳切三角化(正向简单多边形,允许桥接洞)。返回顶点下标三元组。</summary>
    public static List<int> Triangulate(List<int> p)
    {
        int n = p.Count >> 1;
        var idx = new List<int>();
        for (int i = 0; i < n; i++) idx.Add(i);
        int X(int i) => p[i * 2];
        int Y(int i) => p[i * 2 + 1];
        int Cross(int a, int b, int c) => (X(b) - X(a)) * (Y(c) - Y(a)) - (Y(b) - Y(a)) * (X(c) - X(a));
        bool Same(int u, int v) => X(u) == X(v) && Y(u) == Y(v);
        var tris = new List<int>();
        int k = 0, fails = 0;
        while (idx.Count > 3)
        {
            int m = idx.Count;
            if (k >= m) k = 0;
            int a = idx[(k + m - 1) % m], b = idx[k], c = idx[(k + 1) % m];
            bool ear = Cross(a, b, c) > 0;
            if (ear)
            {
                for (int t = 0; t < m; t++)
                {
                    int v = idx[t];
                    if (v == a || v == b || v == c || Same(v, a) || Same(v, b) || Same(v, c)) continue;
                    if (Cross(a, b, v) >= 0 && Cross(b, c, v) >= 0 && Cross(c, a, v) >= 0) { ear = false; break; }
                }
            }

            if (ear || fails > m)
            {
                if (Cross(a, b, c) > 0) { tris.Add(a); tris.Add(b); tris.Add(c); }
                idx.RemoveAt(k);
                fails = 0;
                if (k > 0) k--;
            }
            else { k++; fails++; }
        }

        if (idx.Count == 3 && Cross(idx[0], idx[1], idx[2]) > 0) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
        return tris;
    }

    private const int KEY = 1 << 21;

    /// <summary>凸合并:沿最长共享边贪婪合并邻居,保持凸性与顶点数上限。</summary>
    public static List<int[]> MergePolys(List<int[]> polys, int[] vx, int[] vy, int maxVerts)
    {
        bool Convex(int[] p)
        {
            int n = p.Length;
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
            var edges = new List<(int Key, int Val)>();
            var edgeIndex = new Dictionary<int, int>();
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
                var merged = new List<int>();
                for (int k = 0; k < A.Length; k++) merged.Add(A[(ea + 1 + k) % A.Length]);
                for (int k = 0; k < B.Length - 2; k++) merged.Add(B[(eb + 2 + k) % B.Length]);
                if (!Convex(merged.ToArray())) continue;
                best = len; bm = merged.ToArray(); bi = pi; bj = pj;
            }

            if (bm == null) break;
            polys[bi] = bm;
            polys[bj] = null!;
        }

        return polys.Where(p => p != null).ToList()!;
    }
}
