using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 走廊查询:
/// 无跳跃链接的上下文先走 tile 寻址 NavMesh A* + 漏斗;有链接直接 HPA*
/// (链接是抽象边,NavMesh 上没有);NavMesh 失败也回落 HPA*。命中的 tile 标进走廊掩码。
/// 桥面起点:先沿桥面 BFS 到最近的桥头 portal,再从那里查地面图,
/// 途经的桥面 tile 并入走廊。
/// </summary>
public static class CorridorQuery
{
    /// <summary>一次走廊查询里 NavMesh 与 HPA* 各自的秒表 tick。桥面 BFS 不计入。</summary>
    public struct CorridorStageTicks
    {
        public long NavMesh;
        public long Hpa;
    }

    public enum Branch : byte
    {
        None = 0,
        Tile = 1,
        Hpa = 2,
    }

    public sealed class Result
    {
        public Branch Kind;
        /// <summary>折线(格单位,x, y 平铺)。活前缀是 <see cref="PointCount"/>，null = 不可达。</summary>
        public Fix64[]? Points;
        public int PointCount;
        /// <summary>tile 分支的 poly 引用链(对拍用)。活前缀是 <see cref="PolyCount"/>。</summary>
        public int[]? Polys;
        public int PolyCount;
        /// <summary>HPA 分支的 tile 走廊与入口格(对拍用)。</summary>
        public int[]? TileIds;
        public int[]? Cells;
        public int CellCount;
        public Fix64 Cost;
    }

    /// <summary>查询暂存(每 worker 一份)。</summary>
    public sealed class Scratch
    {
        public Scratch(int tileCellCount)
        {
            Tile = new TilePathQuery.Scratch();
            Hpa = new HpaQuery.Scratch(tileCellCount);
            Tile.Prepare(tileCellCount);
        }

        public TilePathQuery.Scratch Tile { get; }
        public HpaQuery.Scratch Hpa { get; }
        public Dictionary<int, int> UpperLayerPrev { get; } = new();
        public List<int> UpperLayerQueue { get; } = new();
        public Fix64[] PointBuf { get; private set; } = new Fix64[4096];
        public int[] LayerBuf { get; private set; } = new int[2048];
        public List<int> Trace = new(4096);
        public readonly Result Found = new();

        public void EnsurePoints(int count)
        {
            if (PointBuf.Length < count)
            {
                var grown = new Fix64[Math.Max(count, PointBuf.Length * 2)];
                Array.Copy(PointBuf, grown, Math.Min(PointBuf.Length, count));
                PointBuf = grown;
            }
        }

        public void EnsureLayers(int count)
        {
            if (LayerBuf.Length < count) LayerBuf = new int[Math.Max(count, LayerBuf.Length * 2)];
        }

        public Result Publish(Branch kind, int pointCount, int[]? polys, int polyCount, int[]? tileIds, int[]? cells, int cellCount, Fix64 cost)
        {
            Found.Kind = kind;
            Found.PointCount = pointCount;
            Found.Points = pointCount == 0 ? null : PointBuf;
            Found.Polys = polys;
            Found.PolyCount = polyCount;
            Found.TileIds = tileIds;
            Found.Cells = cells;
            Found.CellCount = cellCount;
            Found.Cost = cost;
            return Found;
        }
    }

    public static Result CorridorTo(
        NavContext nav,
        CrowdSimulationRuntimeConfig config,
        int from,
        int to,
        byte[] mask,
        int level,
        Scratch s)
    {
        var unused = default(CorridorStageTicks);
        return CorridorTo(nav, config, from, to, mask, level, s, ref unused);
    }

    public static Result CorridorTo(
        NavContext nav,
        CrowdSimulationRuntimeConfig config,
        int from,
        int to,
        byte[] mask,
        int level,
        Scratch s,
        ref CorridorStageTicks stages)
    {
        if (level != 0)
        {
            int exit = UpperLayerExit(nav, from, mask, s);
            if (exit < 0) return s.Publish(Branch.None, 0, null, 0, null, null, 0, default);
            var sub = CorridorTo(nav, config, exit, to, mask, 0, s, ref stages);
            if (sub.Points == null) return s.Publish(sub.Kind, 0, null, 0, null, null, 0, default);
            s.EnsurePoints(sub.PointCount + 2);
            Array.Copy(s.PointBuf, 0, s.PointBuf, 2, sub.PointCount);
            s.PointBuf[0] = Fix64.FromInt(from % nav.NavCellsPerSide) + Fix64.HalfValue;
            s.PointBuf[1] = Fix64.FromInt(from / nav.NavCellsPerSide) + Fix64.HalfValue;
            return s.Publish(sub.Kind, sub.PointCount + 2, sub.Polys, sub.PolyCount, sub.TileIds, sub.Cells, sub.CellCount, sub.Cost);
        }

        TilePathQuery.Result? r = null;
        if (nav.Links == null)
        {
            long navMark = Stopwatch.GetTimestamp();
            r = TilePathQuery.FindTilePath(nav, config, from, to, layers: true, s.Tile);
            stages.NavMesh += Stopwatch.GetTimestamp() - navMark;
        }

        if (r != null)
        {
            TilePathQuery.MarkNavTiles(nav, config, r.Polys, r.PolyCount, mask);
            s.EnsurePoints(r.PointCount);
            Array.Copy(r.Points, s.PointBuf, r.PointCount);
            return s.Publish(Branch.Tile, r.PointCount, r.Polys, r.PolyCount, null, null, 0, r.Cost);
        }

        long hpaMark = Stopwatch.GetTimestamp();
        var p = HpaQuery.FindPath(nav, from, to, s.Hpa);
        stages.Hpa += Stopwatch.GetTimestamp() - hpaMark;
        if (p == null) return s.Publish(Branch.None, 0, null, 0, null, null, 0, default);
        for (int i = 0; i < p.TileCount; i++) mask[p.TileIds[i]] = 1;
        int navCellsPerSide = nav.NavCellsPerSide;
        s.EnsurePoints(p.CellCount * 2);
        for (int i = 0; i < p.CellCount; i++)
        {
            s.PointBuf[i * 2] = Fix64.FromInt(p.Cells[i] % navCellsPerSide) + Fix64.HalfValue;
            s.PointBuf[i * 2 + 1] = Fix64.FromInt(p.Cells[i] / navCellsPerSide) + Fix64.HalfValue;
        }

        return s.Publish(Branch.Hpa, p.CellCount * 2, null, 0, p.TileIds, p.Cells, p.CellCount, p.Cost);
    }

    // 桥面 BFS 到最近的桥头 portal(双层可走),途经 tile 并入走廊
    private static int UpperLayerExit(NavContext nav, int from, byte[] mask, Scratch s)
    {
        int navCellsPerSide = nav.NavCellsPerSide;
        int navCellsPerTile = nav.Hpa!.NavCellsPerTile, cc = nav.Hpa.NavTilesPerSide;
        var prev = s.UpperLayerPrev;
        var q = s.UpperLayerQueue;
        prev.Clear(); q.Clear();
        prev[from] = -1; q.Add(from);
        for (int h = 0; h < q.Count; h++)
        {
            int cell = q[h];
            if (nav.Portal[cell] != 0 && nav.Passable[cell] != 0)
            {
                for (int k = cell; k >= 0; k = prev[k]) mask[NavGridSteps.TileOf(k, navCellsPerSide, navCellsPerTile, cc)] = 1;
                return cell;
            }

            int x = cell % navCellsPerSide, y = cell / navCellsPerSide;
            TryUpperLayer(nav, prev, q, cell, navCellsPerSide, x + 1, y);
            TryUpperLayer(nav, prev, q, cell, navCellsPerSide, x - 1, y);
            TryUpperLayer(nav, prev, q, cell, navCellsPerSide, x, y + 1);
            TryUpperLayer(nav, prev, q, cell, navCellsPerSide, x, y - 1);
        }

        return -1;
    }

    private static void TryUpperLayer(NavContext nav, Dictionary<int, int> prev, List<int> q, int cell, int navCellsPerSide, int nx, int ny)
    {
        if (nx < 0 || ny < 0 || nx >= navCellsPerSide || ny >= navCellsPerSide) return;
        int v = ny * navCellsPerSide + nx;
        if (nav.UpPass[v] != 0 && !prev.ContainsKey(v)) { prev[v] = cell; q.Add(v); }
    }

    /// <summary>
    /// 链接层的领队折线:HPA 路点太粗(只到入口),
    /// 领队路径从流场路点链描出;链不到终点或太短(少于 4 点)都算失败。
    /// </summary>
    public static (Fix64[] Points, int[]? Layers)? FlowPoints(FlowField flow, int start)
    {
        var scratch = new Scratch(1);
        var hit = FlowPoints(flow, start, scratch);
        if (hit == null) return null;
        var pts = new Fix64[hit.Value.PointCount];
        Array.Copy(hit.Value.Points, pts, pts.Length);
        int[]? layers = null;
        if (hit.Value.Layers != null)
        {
            layers = new int[hit.Value.LayerCount];
            Array.Copy(hit.Value.Layers, layers, layers.Length);
        }

        return (pts, layers);
    }

    public static (Fix64[] Points, int PointCount, int[]? Layers, int LayerCount)? FlowPoints(FlowField flow, int start, Scratch s)
    {
        int n2 = flow.Integ.Length / 2;
        int n = (int)Math.Sqrt(n2);
        var cells = s.Trace;
        FlowFieldBuilder.TracePathInto(flow, start, n2, cells);
        if (cells[cells.Count - 1] != flow.Goal) return null;
        if (cells.Count < 4) return null;
        s.EnsurePoints(cells.Count * 2);
        s.EnsureLayers(cells.Count);
        bool anyUpperLayer = false;
        for (int i = 0; i < cells.Count; i++)
        {
            int k = cells[i], c = k % n2;
            s.PointBuf[i * 2] = Fix64.FromInt(c % n) + Fix64.HalfValue;
            s.PointBuf[i * 2 + 1] = Fix64.FromInt(c / n) + Fix64.HalfValue;
            s.LayerBuf[i] = k / n2;
            anyUpperLayer |= s.LayerBuf[i] != 0;
        }

        return (s.PointBuf, cells.Count * 2, anyUpperLayer ? s.LayerBuf : null, anyUpperLayer ? cells.Count : 0);
    }
}
