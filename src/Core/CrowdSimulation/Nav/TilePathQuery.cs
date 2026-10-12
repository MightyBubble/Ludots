using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// TILE 寻址的 NavMesh 查询(tileQuery.js findTilePath 移植):
/// 多边形 A* + 漏斗,直接跑在某上下文的 tile 条目上,不经过拼装全局网格:
///   poly 引用 = (tile × 2 + 层) × navCellsPerTile² + 局部多边形号(层 0 地面 · 1 桥面)
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
        public NavMinHeap Heap = new(8192);
        public Dictionary<int, Fix64> GScore = new(4096);
        public Dictionary<int, Fix64> Ex = new(4096);
        public Dictionary<int, Fix64> Ey = new(4096);
        public Dictionary<int, int> Parent = new(4096);
        public HashSet<int> Closed = new(4096);
        /// <summary>逐 (navId, tile 条目) 的地面多边形代价备忘(参考实现挂在 nav 上;worker 跨上下文
        /// 复用暂存,键必须带上下文与条目引用——增量重烘换条目即失效,旧 poly 表不复用)。</summary>
        public Dictionary<(int NavId, NavTileEntry Entry), Fix64[]> GroundCostMemo = new(1024);
        public List<Fix64> Portals = new();
        public List<Fix64> FunnelPoints = new();
        public HashSet<int> PortalSeen = new();
        public List<int> Chain = new();
        public List<int> Stitch = new();
        public int[] PolyBuf = new int[1024];
        public int PolyCount;
        public Fix64[] PointBuf = new Fix64[4096];
        public int PointCount;
        public readonly Result Found = new();
        public int[] CostCount = Array.Empty<int>();
        private readonly Stack<Fix64[]> _costFree = new();
        private int _costWidth;
        internal readonly NeighborFn Accept;
        public int Mode;
        public Fix64 Gu, Ux, Uy, Gx, Gy, Cu, MinCost, Inset, TwoInset, Half;
        public int U, Gp, NavCellsPerTile;
        public bool PortalDone;
        public int ChainNext;
        public NavContext Nav = null!;

        public Scratch()
        {
            Accept = AcceptNeighbor;
        }

        /// <summary>代价表和计数缓冲按 tile 格数一次留够。查询里再租空表才分配。</summary>
        public void Prepare(int tileCellCount)
        {
            _costWidth = tileCellCount;
            if (CostCount.Length < tileCellCount) CostCount = new int[tileCellCount];
            GScore.EnsureCapacity(4096);
            Ex.EnsureCapacity(4096);
            Ey.EnsureCapacity(4096);
            Parent.EnsureCapacity(4096);
            Closed.EnsureCapacity(4096);
            GroundCostMemo.EnsureCapacity(1024);
            while (_costFree.Count < 1024) _costFree.Push(new Fix64[tileCellCount]);
        }

        public Fix64[] RentCost()
        {
            if (_costFree.Count > 0) return _costFree.Pop();
            return new Fix64[Math.Max(_costWidth, 1)];
        }

        private void AcceptNeighbor(int v, Fix64 pax, Fix64 pay, Fix64 pbx, Fix64 pby)
        {
            if (Mode == 0) AcceptSearch(v, pax, pay, pbx, pby);
            else AcceptPortal(v, pax, pay, pbx, pby);
        }

        private void AcceptSearch(int v, Fix64 pax, Fix64 pay, Fix64 pbx, Fix64 pby)
        {
            if (Closed.Contains(v)) return;
            Fix64 mx = (pax + pbx) * Half, my = (pay + pby) * Half;
            var h = Hypot(Gx - mx, Gy - my);
            var ng = Gu + Hypot(mx - Ux, my - Uy) * Cu;
            if (v == Gp) ng += h * PolyCost(Nav, NavCellsPerTile, v, this);
            if (!GScore.TryGetValue(v, out var gv) || ng < gv)
            {
                GScore[v] = ng;
                Parent[v] = U;
                Ex[v] = mx;
                Ey[v] = my;
                Heap.Push(v == Gp ? ng : ng + h * MinCost, v);
            }
        }

        private void AcceptPortal(int v, Fix64 pax, Fix64 pay, Fix64 pbx, Fix64 pby)
        {
            if (PortalDone || v != ChainNext) return;
            PortalDone = true;
            Fix64 lx = pbx, ly = pby, rx = pax, ry = pay;
            var len = Hypot(rx - lx, ry - ly);
            if (len > TwoInset)
            {
                var k = Inset / len;
                var dx = (rx - lx) * k;
                var dy = (ry - ly) * k;
                lx += dx;
                ly += dy;
                rx -= dx;
                ry -= dy;
            }
            else
            {
                lx = rx = (lx + rx) / 2;
                ly = ry = (ly + ry) / 2;
            }

            Portals.Add(lx);
            Portals.Add(ly);
            Portals.Add(rx);
            Portals.Add(ry);
        }
    }

    public sealed class Result
    {
        public int[] Polys = Array.Empty<int>();
        public int PolyCount;
        /// <summary>拉直折线(格单位,x, y 平铺)。活前缀是 <see cref="PointCount"/>。</summary>
        public Fix64[] Points = Array.Empty<Fix64>();
        public int PointCount;
        public Fix64 Cost;
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
        int navCellsPerSide = nav.NavCellsPerSide, navCellsPerTile = config.Hpa.NavCellsPerTile;
        int sp = GroundRef(nav, navCellsPerTile, startCell), gp = GroundRef(nav, navCellsPerTile, goalCell);
        if (sp < 0 || gp < 0) return null;

        var half = Fix64.HalfValue;
        Fix64 sx = Fix64.FromInt(startCell % navCellsPerSide) + half, sy = Fix64.FromInt(startCell / navCellsPerSide) + half;
        Fix64 gx = Fix64.FromInt(goalCell % navCellsPerSide) + half, gy = Fix64.FromInt(goalCell / navCellsPerSide) + half;
        var minCost = nav.MinCost;
        var inset = config.Navmesh.PortalInsetNavCells;

        var g = s.GScore; var ex = s.Ex; var ey = s.Ey; var parent = s.Parent; var closed = s.Closed;
        g.Clear(); ex.Clear(); ey.Clear(); parent.Clear(); closed.Clear();
        var heap = s.Heap;
        heap.Clear();
        g[sp] = Fix64.Zero; ex[sp] = sx; ey[sp] = sy; parent[sp] = -1;
        heap.Push(Hypot(gx - sx, gy - sy) * minCost, sp);
        s.Nav = nav;
        s.NavCellsPerTile = navCellsPerTile;
        s.Half = half;
        s.Gx = gx;
        s.Gy = gy;
        s.Gp = gp;
        s.MinCost = minCost;
        s.Mode = 0;
        bool found = sp == gp;
        while (heap.Size > 0 && !found)
        {
            int u = heap.Pop();
            if (!closed.Add(u)) continue;
            if (u == gp) { found = true; break; }
            s.U = u;
            s.Gu = g[u];
            s.Ux = ex[u];
            s.Uy = ey[u];
            s.Cu = PolyCost(nav, navCellsPerTile, u, s);
            EachNeighbor(nav, navCellsPerTile, u, layers, s, s.Accept);
        }

        if (!found) return null;
        var chain = s.Chain;
        chain.Clear();
        for (int p = gp; p != -1; p = parent[p]) chain.Add(p);
        chain.Reverse();

        var portals = s.Portals;
        portals.Clear();
        portals.Add(sx); portals.Add(sy); portals.Add(sx); portals.Add(sy);
        s.Inset = inset;
        s.TwoInset = 2 * inset;
        s.Mode = 1;
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            s.PortalDone = false;
            s.ChainNext = chain[i + 1];
            EachNeighbor(nav, navCellsPerTile, chain[i], layers, s, s.Accept);
        }

        portals.Add(gx); portals.Add(gy); portals.Add(gx); portals.Add(gy);
        NavFunnel.FunnelInto(portals, s.FunnelPoints);
        if (s.PolyBuf.Length < chain.Count)
        {
            s.PolyBuf = new int[Math.Max(chain.Count, s.PolyBuf.Length * 2)];
        }

        for (int i = 0; i < chain.Count; i++) s.PolyBuf[i] = chain[i];
        s.PolyCount = chain.Count;
        if (s.PointBuf.Length < s.FunnelPoints.Count)
        {
            s.PointBuf = new Fix64[Math.Max(s.FunnelPoints.Count, s.PointBuf.Length * 2)];
        }

        for (int i = 0; i < s.FunnelPoints.Count; i++) s.PointBuf[i] = s.FunnelPoints[i];
        s.PointCount = s.FunnelPoints.Count;
        s.Found.Polys = s.PolyBuf;
        s.Found.PolyCount = s.PolyCount;
        s.Found.Points = s.PointBuf;
        s.Found.PointCount = s.PointCount;
        s.Found.Cost = g[gp];
        return s.Found;
    }

    /// <summary>把路线经过的 tile 标进走廊掩码(markTileClusters 移植)。</summary>
    public static void MarkNavTiles(NavContext nav, CrowdSimulationRuntimeConfig config, int[] polys, byte[] mask)
        => MarkNavTiles(nav, config, polys, polys.Length, mask);

    public static void MarkNavTiles(NavContext nav, CrowdSimulationRuntimeConfig config, int[] polys, int polyCount, byte[] mask)
    {
        int navCellsPerTile = config.Hpa.NavCellsPerTile, l = navCellsPerTile * navCellsPerTile;
        for (int i = 0; i < polyCount; i++) mask[(polys[i] / l) >> 1] = 1;
    }

    // 地面格的 poly 引用,-1 = 该格无多边形
    private static int GroundRef(NavContext nav, int navCellsPerTile, int cell)
    {
        int navCellsPerSide = nav.NavCellsPerSide, navTilesPerSide = navCellsPerSide / navCellsPerTile;
        int x = cell % navCellsPerSide, y = cell / navCellsPerSide;
        int tile = (y / navCellsPerTile) * navTilesPerSide + x / navCellsPerTile;
        int p = nav.Tiles![tile].PolyOf[(y % navCellsPerTile) * navCellsPerTile + x % navCellsPerTile];
        return p < 0 ? -1 : tile * 2 * navCellsPerTile * navCellsPerTile + p;
    }

    // 地面 tile 的逐多边形代价(格代价均值;查询间备忘,键钉在 tile 条目引用上——重烘换条目即失效)
    private static Fix64[] GroundCost(NavContext nav, int navCellsPerTile, int tile, Scratch s)
    {
        int navCellsPerSide = nav.NavCellsPerSide, navTilesPerSide = navCellsPerSide / navCellsPerTile;
        var e = nav.Tiles![tile];
        if (s.GroundCostMemo.TryGetValue((nav.Id, e), out var memo)) return memo;
        int ox = tile % navTilesPerSide * navCellsPerTile, oy = tile / navTilesPerSide * navCellsPerTile;
        var pc = s.RentCost();
        if (pc.Length < e.Count) pc = new Fix64[e.Count];
        Array.Clear(pc, 0, e.Count);
        if (s.CostCount.Length < e.Count) s.CostCount = new int[e.Count];
        var cnt = s.CostCount;
        Array.Clear(cnt, 0, e.Count);
        for (int y = 0; y < navCellsPerTile; y++)
        {
            for (int x = 0; x < navCellsPerTile; x++)
            {
                int p = e.PolyOf[y * navCellsPerTile + x];
                if (p >= 0) { pc[p] += nav.Cost[(oy + y) * navCellsPerSide + ox + x]; cnt[p]++; }
            }
        }

        for (int p = 0; p < e.Count; p++) pc[p] = cnt[p] > 0 ? pc[p] / cnt[p] : Fix64.OneValue;
        s.GroundCostMemo[(nav.Id, e)] = pc;
        return pc;
    }

    private static Fix64 PolyCost(NavContext nav, int navCellsPerTile, int refr, Scratch s)
    {
        int l = navCellsPerTile * navCellsPerTile, tl = refr / l;
        return (tl & 1) != 0
            ? nav.UpperTiles![tl >> 1].PolyCost[refr - tl * l]
            : GroundCost(nav, navCellsPerTile, tl >> 1, s)[refr - tl * l];
    }

    private static NavTileEntry? EntryOf(NavContext nav, int tile, int layer)
        => layer != 0
            ? (nav.UpperTiles != null && nav.UpperTiles.TryGetValue(tile, out var u) ? u.Entry : null)
            : nav.Tiles![tile];

    internal delegate void NeighborFn(int refr, Fix64 ax, Fix64 ay, Fix64 bx, Fix64 by);

    // 对 ref 的每个邻居调 f(portal 端点以属主绕向给出)
    private static void EachNeighbor(NavContext nav, int navCellsPerTile, int refr, bool layers, Scratch s, NeighborFn f)
    {
        int navCellsPerSide = nav.NavCellsPerSide, navTilesPerSide = navCellsPerSide / navCellsPerTile, l = navCellsPerTile * navCellsPerTile;
        int tl = refr / l, p = refr - tl * l, tile = tl >> 1, layer = tl & 1;
        var e = EntryOf(nav, tile, layer)!;
        int tx = tile % navTilesPerSide, ty = tile / navTilesPerSide, ox = tx * navCellsPerTile, oy = ty * navCellsPerTile;
        for (int k = e.NeiStart[p]; k < e.NeiStart[p + 1]; k++)
        {
            int a = e.NeiA[k], b = e.NeiB[k];
            f(tl * l + e.Nei[k], Fix64.FromInt(e.Vx[a] + ox), Fix64.FromInt(e.Vy[a] + oy), Fix64.FromInt(e.Vx[b] + ox), Fix64.FromInt(e.Vy[b] + oy));
        }

        for (int side = 0; side < 4; side++)
        {
            int sa = Sides[side, 0], sb = Sides[side, 1], dx = Sides[side, 2], dy = Sides[side, 3];
            int nx = tx + dx, ny = ty + dy;
            if (nx < 0 || ny < 0 || nx >= navTilesPerSide || ny >= navTilesPerSide) continue;
            int tb = ny * navTilesPerSide + nx;
            var eb = EntryOf(nav, tb, layer);
            if (eb == null) continue;
            var pairs = s.Stitch;
            NavMeshAssembler.StitchInto(e, eb, sa, sb, pairs);
            bool vertical = dx != 0;
            int line = vertical ? (dx > 0 ? ox + navCellsPerTile : ox) : (dy > 0 ? oy + navCellsPerTile : oy);
            int off = vertical ? oy : ox;
            int refBase = (tb * 2 + layer) * l;
            for (int k = 0; k < pairs.Count; k += 4)
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
            var px = Fix64.FromInt(ox + cell % navCellsPerTile) + Fix64.HalfValue;
            var py = Fix64.FromInt(oy + cell / navCellsPerTile) + Fix64.HalfValue;
            f((tile * 2 + (layer ^ 1)) * l + q, px, py, px, py);
        }
    }

    private static Fix64 Hypot(Fix64 x, Fix64 y) => Fix64Math.Sqrt(x * x + y * y);
}
