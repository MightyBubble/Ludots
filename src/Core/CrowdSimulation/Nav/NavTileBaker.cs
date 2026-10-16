using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Nav.GridBake;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 单块 nav tile 的烘焙产物(位置无关,内容相同的 tile 共享一份)。
/// 坐标为 tile 局部格角点(0..navCellsPerTile);side:0 = x0,1 = x 为 navCellsPerTile,2 = y0,3 = y 为 navCellsPerTile。
/// </summary>
public sealed class NavTileEntry
{
    public required int Uid { get; set; }
    public required int Count { get; set; }
    public required int RegionCount { get; set; }
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
    /// <summary>cell → poly(tile 内 navCellsPerTile² 下标;点在凸多边形上 + 洪泛补齐)。</summary>
    public required int[] PolyOf { get; init; }
    public required int[] BorderPoly { get; init; }
    public required byte[] BorderSide { get; init; }
    public required int[] BorderLo { get; init; }
    public required int[] BorderHi { get; init; }
    public required byte[] BorderRev { get; init; }

    /// <summary>负值表示数组长度就是有效长度(一次性精确数组)。池化条目写成有效前缀长度。</summary>
    public int VertexCount { get; set; } = -1;
    public int PolyVertCount { get; set; } = -1;
    public int NeighborCount { get; set; } = -1;
    public int BorderCount { get; set; } = -1;

    public int LiveVertices => VertexCount >= 0 ? VertexCount : Vx.Length;
    public int LivePolyVerts => PolyVertCount >= 0 ? PolyVertCount : PolyVerts.Length;
    public int LiveNeighbors => NeighborCount >= 0 ? NeighborCount : Nei.Length;
    public int LiveBorders => BorderCount >= 0 ? BorderCount : BorderPoly.Length;
    public int LivePolyStart => VertexCount >= 0 ? Count + 1 : PolyStart.Length;
    public int LiveNeiStart => VertexCount >= 0 ? Count + 1 : NeiStart.Length;
    public int LiveBoxes => VertexCount >= 0 ? Count : BMinX.Length;

    internal static NavTileEntry CreateBuffer(int navCellsPerTile, int maxVertsPerPoly)
    {
        int nn = navCellsPerTile * navCellsPerTile;
        int polyMax = nn;
        int vertMax = (navCellsPerTile + 1) * (navCellsPerTile + 1);
        int edgeMax = polyMax * maxVertsPerPoly;
        return new NavTileEntry
        {
            Uid = -1,
            Count = 0,
            RegionCount = 0,
            Vx = new int[vertMax],
            Vy = new int[vertMax],
            PolyStart = new int[polyMax + 1],
            PolyVerts = new int[edgeMax],
            NeiStart = new int[polyMax + 1],
            Nei = new int[edgeMax],
            NeiA = new int[edgeMax],
            NeiB = new int[edgeMax],
            BMinX = new int[polyMax],
            BMinY = new int[polyMax],
            BMaxX = new int[polyMax],
            BMaxY = new int[polyMax],
            PolyOf = new int[nn],
            BorderPoly = new int[edgeMax],
            BorderSide = new byte[edgeMax],
            BorderLo = new int[edgeMax],
            BorderHi = new int[edgeMax],
            BorderRev = new byte[edgeMax],
        };
    }

    internal NavTileEntry CopyExact()
    {
        return new NavTileEntry
        {
            Uid = Uid,
            Count = Count,
            RegionCount = RegionCount,
            Vx = Copy(Vx, LiveVertices),
            Vy = Copy(Vy, LiveVertices),
            PolyStart = Copy(PolyStart, LivePolyStart),
            PolyVerts = Copy(PolyVerts, LivePolyVerts),
            NeiStart = Copy(NeiStart, LiveNeiStart),
            Nei = Copy(Nei, LiveNeighbors),
            NeiA = Copy(NeiA, LiveNeighbors),
            NeiB = Copy(NeiB, LiveNeighbors),
            BMinX = Copy(BMinX, LiveBoxes),
            BMinY = Copy(BMinY, LiveBoxes),
            BMaxX = Copy(BMaxX, LiveBoxes),
            BMaxY = Copy(BMaxY, LiveBoxes),
            PolyOf = Copy(PolyOf, PolyOf.Length),
            BorderPoly = Copy(BorderPoly, LiveBorders),
            BorderSide = Copy(BorderSide, LiveBorders),
            BorderLo = Copy(BorderLo, LiveBorders),
            BorderHi = Copy(BorderHi, LiveBorders),
            BorderRev = Copy(BorderRev, LiveBorders),
        };
    }

    private static int[] Copy(int[] source, int length)
    {
        var output = new int[length];
        Array.Copy(source, output, length);
        return output;
    }

    private static byte[] Copy(byte[] source, int length)
    {
        var output = new byte[length];
        Array.Copy(source, output, length);
        return output;
    }
}

/// <summary>
/// 单 tile 的 2D 格网烘焙：距离场 → 分水岭区域 → 格角点轮廓 → 三角化 → 凸多边形。
/// 多边形不跨导航区域码。产物是整数格角点、cell→poly、两侧共用的 tile 边 portal。
/// </summary>
public static class NavTileBaker
{
    private const int KEY = 1 << 21;

    public static NavTileEntry Bake(ushort[] pass, int navCellsPerTile, double minRegionArea, double maxSimplificationError, double maxEdgeLen, int maxVertsPerPoly)
    {
        var buffers = new TileBakeScratch(navCellsPerTile, maxVertsPerPoly);
        var slot = NavTileEntry.CreateBuffer(navCellsPerTile, maxVertsPerPoly);
        BakeInto(slot, pass, navCellsPerTile, minRegionArea, maxSimplificationError, maxEdgeLen, maxVertsPerPoly, buffers);
        return slot.CopyExact();
    }

    internal static void BakeInto(NavTileEntry entry, ushort[] pass, int navCellsPerTile, double minRegionArea, double maxSimplificationError, double maxEdgeLen, int maxVertsPerPoly, TileBakeScratch scratch)
    {
        scratch.BeginTile();
        int tt = navCellsPerTile * navCellsPerTile;
        var passBytes = scratch.Pass;
        for (int i = 0; i < tt; i++) passBytes[i] = (byte)pass[i];
        DistanceField.ComputeClearance(passBytes, navCellsPerTile, scratch.Dist);
        int regionCount = WatershedRegions.Build(scratch, passBytes, scratch.Dist, navCellsPerTile, (int)minRegionArea);
        ContourTracer.BuildContours(scratch, regionCount, navCellsPerTile, maxSimplificationError, maxEdgeLen);

        var vx = scratch.Vx;
        var vy = scratch.Vy;
        var polys = scratch.PolyList;
        for (int r = 1; r <= regionCount; r++)
        {
            var ls = scratch.Loops[r];
            if (ls.Count == 0) continue;
            int oi = 0, best = -1;
            for (int k = 0; k < ls.Count; k++)
            {
                int a = Math.Abs(PolygonOps.Area2(ls[k]));
                if (a > best) { best = a; oi = k; }
            }

            var outer = PolygonOps.Area2(ls[oi]) > 0 ? ls[oi] : PolygonOps.Reversed(ls[oi], scratch);
            var holes = scratch.HoleList;
            holes.Clear();
            for (int k = 0; k < ls.Count; k++)
            {
                if (k == oi) continue;
                holes.Add(PolygonOps.Area2(ls[k]) < 0 ? ls[k] : PolygonOps.Reversed(ls[k], scratch));
            }

            var poly = holes.Count > 0 ? PolygonOps.MergeHoles(outer, holes, scratch) : outer;
            var tri = PolygonOps.Triangulate(poly, scratch);
            var tp = scratch.TriPoly;
            tp.Clear();
            for (int k = 0; k < tri.Count; k += 3)
            {
                int a = Vid(scratch, navCellsPerTile, poly[tri[k] * 2], poly[tri[k] * 2 + 1]);
                int b = Vid(scratch, navCellsPerTile, poly[tri[k + 1] * 2], poly[tri[k + 1] * 2 + 1]);
                int c = Vid(scratch, navCellsPerTile, poly[tri[k + 2] * 2], poly[tri[k + 2] * 2 + 1]);
                if (a != b && b != c && a != c)
                {
                    var triVerts = scratch.RentVerts(3);
                    triVerts[0] = a;
                    triVerts[1] = b;
                    triVerts[2] = c;
                    tp.Add(triVerts);
                }
            }

            foreach (var p in PolygonOps.MergePolys(tp, vx, vy, maxVertsPerPoly, scratch)) polys.Add(p);
            tp.Clear();
        }

        int pCount = polys.Count;
        if (pCount + 1 > entry.PolyStart.Length)
        {
            throw new InvalidOperationException($"tile 多边形数 {pCount} 超过缓冲 {entry.PolyStart.Length - 1}。");
        }

        var polyStart = entry.PolyStart;
        polyStart[0] = 0;
        for (int i = 0; i < pCount; i++) polyStart[i + 1] = polyStart[i] + polys[i].Length;
        int polyVertCount = polyStart[pCount];
        if (vx.Count > entry.Vx.Length || polyVertCount > entry.PolyVerts.Length)
        {
            throw new InvalidOperationException($"tile 顶点 {vx.Count}/{polyVertCount} 超过缓冲。");
        }

        var polyVerts = entry.PolyVerts;
        var emap = scratch.Emap;
        for (int i = 0; i < vx.Count; i++)
        {
            entry.Vx[i] = vx[i];
            entry.Vy[i] = vy[i];
        }

        for (int i = 0; i < pCount; i++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            var p = polys[i];
            for (int k = 0; k < p.Length; k++)
            {
                int v = p[k];
                polyVerts[polyStart[i] + k] = v;
                x0 = Math.Min(x0, vx[v]); y0 = Math.Min(y0, vy[v]);
                x1 = Math.Max(x1, vx[v]); y1 = Math.Max(y1, vy[v]);
                emap[v * KEY + p[(k + 1) % p.Length]] = i;
            }

            entry.BMinX[i] = x0; entry.BMinY[i] = y0; entry.BMaxX[i] = x1; entry.BMaxY[i] = y1;
        }

        var nb = scratch.NbFlat;
        var bd = scratch.BdFlat;
        var neiStart = entry.NeiStart;
        neiStart[0] = 0;
        for (int i = 0; i < pCount; i++)
        {
            var p = polys[i];
            for (int k = 0; k < p.Length; k++)
            {
                int a = p[k], b = p[(k + 1) % p.Length];
                if (emap.TryGetValue(b * KEY + a, out int q) && q != i) { nb.Add(q); nb.Add(a); nb.Add(b); continue; }

                int ax = vx[a], ay = vy[a], bx = vx[b], by = vy[b];
                if (ax == bx && (ax == 0 || ax == navCellsPerTile))
                {
                    bd.Add(i); bd.Add(ax == 0 ? 0 : 1); bd.Add(Math.Min(ay, by)); bd.Add(Math.Max(ay, by)); bd.Add(ay > by ? 1 : 0);
                }
                else if (ay == by && (ay == 0 || ay == navCellsPerTile))
                {
                    bd.Add(i); bd.Add(ay == 0 ? 2 : 3); bd.Add(Math.Min(ax, bx)); bd.Add(Math.Max(ax, bx)); bd.Add(ax > bx ? 1 : 0);
                }
            }

            neiStart[i + 1] = nb.Count / 3;
        }

        int eCount = nb.Count / 3;
        int bCount = bd.Count / 5;
        if (eCount > entry.Nei.Length || bCount > entry.BorderPoly.Length)
        {
            throw new InvalidOperationException($"tile 邻接 {eCount} 或边界 {bCount} 超过缓冲。");
        }

        for (int k = 0; k < eCount; k++)
        {
            entry.Nei[k] = nb[k * 3];
            entry.NeiA[k] = nb[k * 3 + 1];
            entry.NeiB[k] = nb[k * 3 + 2];
        }

        for (int k = 0; k < bCount; k++)
        {
            entry.BorderPoly[k] = bd[k * 5];
            entry.BorderSide[k] = (byte)bd[k * 5 + 1];
            entry.BorderLo[k] = bd[k * 5 + 2];
            entry.BorderHi[k] = bd[k * 5 + 3];
            entry.BorderRev[k] = (byte)bd[k * 5 + 4];
        }

        // cell → poly:格心点在凸多边形内,残余格先同区域邻居洪泛再任意可走邻居
        var polyOf = entry.PolyOf;
        Array.Fill(polyOf, -1, 0, tt);
        var queue = scratch.Queue;
        int qt = 0;
        for (int p = 0; p < pCount; p++)
        {
            int s = polyStart[p], e = polyStart[p + 1];
            int y0 = entry.BMinY[p], y1 = entry.BMaxY[p], x0 = entry.BMinX[p], x1 = entry.BMaxX[p];
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int i = y * navCellsPerTile + x;
                    if (passBytes[i] == 0 || polyOf[i] >= 0) continue;
                    double px = x + 0.5, py = y + 0.5;
                    bool inside = true;
                    for (int k = s; k < e && inside; k++)
                    {
                        int a = polyVerts[k], b = polyVerts[k + 1 < e ? k + 1 : s];
                        if ((vx[b] - vx[a]) * (py - vy[a]) - (vy[b] - vy[a]) * (px - vx[a]) < 0) inside = false;
                    }

                    if (inside) { polyOf[i] = p; queue[qt++] = i; }
                }
            }
        }

        var ns = scratch.Neighbor4;
        for (int passId = 0; passId < 2; passId++)
        {
            bool sameArea = passId == 0;
            for (int qh = 0; qh < qt; qh++)
            {
                int c = queue[qh], x = c % navCellsPerTile, y = c / navCellsPerTile;
                ns[0] = x > 0 ? c - 1 : -1;
                ns[1] = x < navCellsPerTile - 1 ? c + 1 : -1;
                ns[2] = y > 0 ? c - navCellsPerTile : -1;
                ns[3] = y < navCellsPerTile - 1 ? c + navCellsPerTile : -1;
                for (int n = 0; n < 4; n++)
                {
                    int ni = ns[n];
                    if (ni >= 0 && passBytes[ni] != 0 && polyOf[ni] < 0 && (!sameArea || passBytes[ni] == passBytes[c]))
                    {
                        polyOf[ni] = polyOf[c];
                        queue[qt++] = ni;
                    }
                }
            }
        }

        entry.Count = pCount;
        entry.RegionCount = regionCount;
        entry.VertexCount = vx.Count;
        entry.PolyVertCount = polyVertCount;
        entry.NeighborCount = eCount;
        entry.BorderCount = bCount;
    }

    private static int Vid(TileBakeScratch scratch, int navCellsPerTile, int x, int y)
    {
        int k = y * (navCellsPerTile + 1) + x;
        if (!scratch.Vmap.TryGetValue(k, out int id))
        {
            id = scratch.Vx.Count;
            scratch.Vmap[k] = id;
            scratch.Vx.Add(x);
            scratch.Vy.Add(y);
        }

        return id;
    }
}
