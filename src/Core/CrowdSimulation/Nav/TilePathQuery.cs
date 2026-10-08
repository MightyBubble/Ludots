using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// TILE 寻址的 NavMesh 查询(tileQuery.js findTilePath 移植,RT-16 LY-3 / BV-3):
/// 多边形 A* + 漏斗,直接跑在某上下文的 tile 条目上,不经过拼装全局网格:
///   poly 引用 = (tile × 2 + 层) × T² + 局部多边形号(层 0 地面 · 1 桥面)
///   同层同 tile → 条目内部邻接;同层邻 tile → 两边界的重叠缝合(带记忆化)
///   地面 ↔ 桥面 → 仅在桥头 portal 格,以格心点 portal 相连
/// 侧边顺序(−y, −x, +x, +y)与拼装网格一致,地面查询的邻居展开序与 findPolyPath 相同。
/// </summary>
public static class TilePathQuery
{
    // sa, sb, dx, dy(与参考实现 SIDES 同序,邻居展开序即对拍序)
    private static readonly int[,] Sides = { { 2, 3, 0, -1 }, { 0, 1, -1, 0 }, { 1, 0, 1, 0 }, { 3, 2, 0, 1 } };

    /// <summary>查询暂存(RT-03:每 worker 一份,不可重入)。tile 代价备忘是条目的纯函数,跨查询保留。</summary>
    public sealed class Scratch
    {
        public NavMinHeap Heap = new(256);
        public Dictionary<int, Fix64> G = new();
        public Dictionary<int, Fix64> Ex = new();
        public Dictionary<int, Fix64> Ey = new();
        public Dictionary<int, int> Parent = new();
        public HashSet<int> Closed = new();
        /// <summary>逐 (navId, tile 条目) 的地面多边形代价备忘(参考实现挂在 nav 上;worker 跨上下文
        /// 复用暂存,键必须带上下文与条目引用——增量重烘换条目即失效,旧 poly 表不复用)。</summary>
        public Dictionary<(int NavId, NavTileEntry Entry), Fix64[]> GroundCostMemo = new();
        public List<Fix64> Portals = new();
        public HashSet<int> PortalSeen = new();
    }

    public sealed class Result
    {
        public required int[] Polys { get; init; }
        /// <summary>拉直折线(格单位,x, y 平铺)。</summary>
        public required Fix64[] Points { get; init; }
        public required Fix64 Cost { get; init; }
    }

    /// <summary>返回 null = 起终点无多边形或不连通。layers = false 时不上桥面。</summary>
    public static Result? FindTilePath(
        NavContext nav,
        CrowdSimulationRuntimeConfig config,
        int startCell,
        int goalCell,
        bool layers,
        Scratch s)
    {
        ArgumentNullException.ThrowIfNull(nav.Tiles);
        int n = nav.CellCount, t = config.Hpa.ClusterSize;
        int sp = GroundRef(nav, t, startCell), gp = GroundRef(nav, t, goalCell);
        if (sp < 0 || gp < 0) return null;

        var half = Fix64.HalfValue;
        Fix64 sx = Fix64.FromInt(startCell % n) + half, sy = Fix64.FromInt(startCell / n) + half;
        Fix64 gx = Fix64.FromInt(goalCell % n) + half, gy = Fix64.FromInt(goalCell / n) + half;
        var minCost = nav.MinCost;
        var inset = config.Navmesh.PortalInsetCells;

        var g = s.G; var ex = s.Ex; var ey = s.Ey; var parent = s.Parent; var closed = s.Closed;
        g.Clear(); ex.Clear(); ey.Clear(); parent.Clear(); closed.Clear();
        var heap = s.Heap;
        heap.Clear();
        g[sp] = Fix64.Zero; ex[sp] = sx; ey[sp] = sy; parent[sp] = -1;
        heap.Push(Hypot(gx - sx, gy - sy) * minCost, sp);
        bool found = sp == gp;
        while (heap.Size > 0 && !found)
        {
            int u = heap.Pop();
            if (!closed.Add(u)) continue;
            if (u == gp) { found = true; break; }
            var gu = g[u]; var ux = ex[u]; var uy = ey[u]; var cu = PolyCost(nav, t, u, s);
            EachNeighbor(nav, t, u, layers, s, (v, pax, pay, pbx, pby) =>
            {
                if (closed.Contains(v)) return;
                Fix64 mx = (pax + pbx) * half, my = (pay + pby) * half;
                var h = Hypot(gx - mx, gy - my);
                var ng = gu + Hypot(mx - ux, my - uy) * cu;
                if (v == gp) ng += h * PolyCost(nav, t, v, s);
                if (!g.TryGetValue(v, out var gv) || ng < gv)
                {
                    g[v] = ng; parent[v] = u; ex[v] = mx; ey[v] = my;
                    heap.Push(v == gp ? ng : ng + h * minCost, v);
                }
            });
        }

        if (!found) return null;
        var chain = new List<int>();
        for (int p = gp; p != -1; p = parent[p]) chain.Add(p);
        chain.Reverse();

        var portals = s.Portals;
        portals.Clear();
        portals.Add(sx); portals.Add(sy); portals.Add(sx); portals.Add(sy);
        var twoInset = 2 * inset;
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            bool done = false;
            EachNeighbor(nav, t, chain[i], layers, s, (v, pax, pay, pbx, pby) =>
            {
                if (done || v != chain[i + 1]) return;
                done = true;
                // 多边形绕向为正,经 A→B 边离开:左 = B,右 = A
                Fix64 lx = pbx, ly = pby, rx = pax, ry = pay;
                var len = Hypot(rx - lx, ry - ly);
                if (len > twoInset)
                {
                    var k = inset / len; var dx = (rx - lx) * k; var dy = (ry - ly) * k;
                    lx += dx; ly += dy; rx -= dx; ry -= dy;
                }
                else
                {
                    lx = rx = (lx + rx) / 2; ly = ry = (ly + ry) / 2;
                }

                portals.Add(lx); portals.Add(ly); portals.Add(rx); portals.Add(ry);
            });
        }

        portals.Add(gx); portals.Add(gy); portals.Add(gx); portals.Add(gy);
        return new Result { Polys = chain.ToArray(), Points = NavFunnel.Funnel(portals).ToArray(), Cost = g[gp] };
    }

    /// <summary>把路线经过的 tile 标进走廊掩码(markTileClusters 移植)。</summary>
    public static void MarkTileClusters(NavContext nav, CrowdSimulationRuntimeConfig config, int[] polys, byte[] mask)
    {
        int t = config.Hpa.ClusterSize, l = t * t;
        foreach (int p in polys) mask[(p / l) >> 1] = 1;
    }

    // 地面格的 poly 引用,-1 = 该格无多边形
    private static int GroundRef(NavContext nav, int t, int cell)
    {
        int n = nav.CellCount, c = n / t;
        int x = cell % n, y = cell / n;
        int tile = (y / t) * c + x / t;
        int p = nav.Tiles![tile].PolyOf[(y % t) * t + x % t];
        return p < 0 ? -1 : tile * 2 * t * t + p;
    }

    // 地面 tile 的逐多边形代价(格代价均值;查询间备忘,键钉在 tile 条目引用上——重烘换条目即失效)
    private static Fix64[] GroundCost(NavContext nav, int t, int tile, Scratch s)
    {
        int n = nav.CellCount, c = n / t;
        var e = nav.Tiles![tile];
        if (s.GroundCostMemo.TryGetValue((nav.Id, e), out var memo)) return memo;
        int ox = tile % c * t, oy = tile / c * t;
        var pc = new Fix64[e.Count];
        var cnt = new int[e.Count];
        for (int y = 0; y < t; y++)
        {
            for (int x = 0; x < t; x++)
            {
                int p = e.PolyOf[y * t + x];
                if (p >= 0) { pc[p] += nav.Cost[(oy + y) * n + ox + x]; cnt[p]++; }
            }
        }

        for (int p = 0; p < e.Count; p++) pc[p] = cnt[p] > 0 ? pc[p] / cnt[p] : Fix64.OneValue;
        s.GroundCostMemo[(nav.Id, e)] = pc;
        return pc;
    }

    private static Fix64 PolyCost(NavContext nav, int t, int refr, Scratch s)
    {
        int l = t * t, tl = refr / l;
        return (tl & 1) != 0
            ? nav.UpperTiles![tl >> 1].PolyCost[refr - tl * l]
            : GroundCost(nav, t, tl >> 1, s)[refr - tl * l];
    }

    private static NavTileEntry? EntryOf(NavContext nav, int tile, int layer)
        => layer != 0
            ? (nav.UpperTiles != null && nav.UpperTiles.TryGetValue(tile, out var u) ? u.Entry : null)
            : nav.Tiles![tile];

    private delegate void NeighborFn(int refr, Fix64 ax, Fix64 ay, Fix64 bx, Fix64 by);

    // 对 ref 的每个邻居调 f(portal 端点以属主绕向给出)
    private static void EachNeighbor(NavContext nav, int t, int refr, bool layers, Scratch s, NeighborFn f)
    {
        int n = nav.CellCount, c = n / t, l = t * t;
        int tl = refr / l, p = refr - tl * l, tile = tl >> 1, layer = tl & 1;
        var e = EntryOf(nav, tile, layer)!;
        int tx = tile % c, ty = tile / c, ox = tx * t, oy = ty * t;
        for (int k = e.NeiStart[p]; k < e.NeiStart[p + 1]; k++)
        {
            int a = e.NeiA[k], b = e.NeiB[k];
            f(tl * l + e.Nei[k], Fix64.FromInt(e.Vx[a] + ox), Fix64.FromInt(e.Vy[a] + oy), Fix64.FromInt(e.Vx[b] + ox), Fix64.FromInt(e.Vy[b] + oy));
        }

        for (int side = 0; side < 4; side++)
        {
            int sa = Sides[side, 0], sb = Sides[side, 1], dx = Sides[side, 2], dy = Sides[side, 3];
            int nx = tx + dx, ny = ty + dy;
            if (nx < 0 || ny < 0 || nx >= c || ny >= c) continue;
            int tb = ny * c + nx;
            var eb = EntryOf(nav, tb, layer);
            if (eb == null) continue;
            var pairs = NavMeshAssembler.StitchPairs(e, eb, sa, sb);
            bool vertical = dx != 0;
            int line = vertical ? (dx > 0 ? ox + t : ox) : (dy > 0 ? oy + t : oy);
            int off = vertical ? oy : ox;
            int refBase = (tb * 2 + layer) * l;
            for (int k = 0; k < pairs.Length; k += 4)
            {
                int i = pairs[k];
                if (e.BorderPoly[i] != p) continue;
                int lo = off + pairs[k + 2], hi = off + pairs[k + 3];
                int a = e.BorderRev[i] != 0 ? hi : lo, b = e.BorderRev[i] != 0 ? lo : hi;
                if (vertical) f(refBase + eb.BorderPoly[pairs[k + 1]], Fix64.FromInt(line), Fix64.FromInt(a), Fix64.FromInt(line), Fix64.FromInt(b));
                else f(refBase + eb.BorderPoly[pairs[k + 1]], Fix64.FromInt(a), Fix64.FromInt(line), Fix64.FromInt(b), Fix64.FromInt(line));
            }
        }

        if (!layers || nav.UpperTiles == null || !nav.UpperTiles.TryGetValue(tile, out var up)) return;
        var g = nav.Tiles![tile];
        var seen = s.PortalSeen;
        seen.Clear();
        foreach (int cell in up.Portals)
        {
            int own = layer != 0 ? up.Entry.PolyOf[cell] : g.PolyOf[cell];
            int q = layer != 0 ? g.PolyOf[cell] : up.Entry.PolyOf[cell];
            if (own != p || q < 0 || !seen.Add(q)) continue;
            var px = Fix64.FromInt(ox + cell % t) + Fix64.HalfValue;
            var py = Fix64.FromInt(oy + cell / t) + Fix64.HalfValue;
            f((tile * 2 + (layer ^ 1)) * l + q, px, py, px, py);
        }
    }

    private static Fix64 Hypot(Fix64 x, Fix64 y) => Fix64Math.Sqrt(x * x + y * y);
}
