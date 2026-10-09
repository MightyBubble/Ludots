using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 走廊查询(pathJobs.js corridorTo / deckExit / flowPoints 移植):
/// 无跳跃链接的上下文先走 tile 寻址 NavMesh A* + 漏斗;有链接直接 HPA*
/// (链接是抽象边,NavMesh 上没有);NavMesh 失败也回落 HPA*。命中的 cluster 标进走廊掩码。
/// 桥面起点:先沿桥面 BFS 到最近的桥头 portal,再从那里查地面图,
/// 途经的桥面 cluster 并入走廊。
/// </summary>
public static class CorridorQuery
{
    public enum Branch : byte
    {
        None = 0,
        Tile = 1,
        Hpa = 2,
    }

    public sealed class Result
    {
        public required Branch Kind { get; init; }
        /// <summary>折线(格单位,x, y 平铺);null = 不可达。</summary>
        public required Fix64[]? Points { get; init; }
        /// <summary>tile 分支的 poly 引用链(对拍用)。</summary>
        public int[]? Polys { get; init; }
        /// <summary>HPA 分支的 cluster 走廊与入口格(对拍用)。</summary>
        public int[]? Clusters { get; init; }
        public int[]? Cells { get; init; }
        public Fix64 Cost { get; init; }
    }

    /// <summary>查询暂存(RT-03:每 worker 一份)。</summary>
    public sealed class Scratch
    {
        public Scratch(int clusterCells)
        {
            Tile = new TilePathQuery.Scratch();
            Hpa = new HpaQuery.Scratch(clusterCells);
        }

        public TilePathQuery.Scratch Tile { get; }
        public HpaQuery.Scratch Hpa { get; }
        public Dictionary<int, int> DeckPrev { get; } = new();
        public List<int> DeckQueue { get; } = new();
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
        if (level != 0)
        {
            int exit = DeckExit(nav, from, mask, s);
            if (exit < 0) return new Result { Kind = Branch.None, Points = null };
            var sub = CorridorTo(nav, config, exit, to, mask, 0, s);
            if (sub.Points == null) return new Result { Kind = sub.Kind, Points = null };
            var pts = new List<Fix64>
            {
                Fix64.FromInt(from % nav.CellCount) + Fix64.HalfValue,
                Fix64.FromInt(from / nav.CellCount) + Fix64.HalfValue,
            };
            pts.AddRange(sub.Points);
            return new Result { Kind = sub.Kind, Points = pts.ToArray(), Polys = sub.Polys, Clusters = sub.Clusters, Cells = sub.Cells, Cost = sub.Cost };
        }

        TilePathQuery.Result? r = null;
        if (nav.Links == null)
        {
            r = TilePathQuery.FindTilePath(nav, config, from, to, layers: true, s.Tile);
        }

        if (r != null)
        {
            TilePathQuery.MarkTileClusters(nav, config, r.Polys, mask);
            return new Result { Kind = Branch.Tile, Points = r.Points, Polys = r.Polys, Cost = r.Cost };
        }

        var p = HpaQuery.FindPath(nav, from, to, s.Hpa);
        if (p == null) return new Result { Kind = Branch.None, Points = null };
        foreach (int c in p.Clusters) mask[c] = 1;
        int n = nav.CellCount;
        var hpaPts = new Fix64[p.Cells.Length * 2];
        for (int i = 0; i < p.Cells.Length; i++)
        {
            hpaPts[i * 2] = Fix64.FromInt(p.Cells[i] % n) + Fix64.HalfValue;
            hpaPts[i * 2 + 1] = Fix64.FromInt(p.Cells[i] / n) + Fix64.HalfValue;
        }

        return new Result { Kind = Branch.Hpa, Points = hpaPts, Clusters = p.Clusters, Cells = p.Cells, Cost = p.Cost };
    }

    // 桥面 BFS 到最近的桥头 portal(双层可走),途经 cluster 并入走廊
    private static int DeckExit(NavContext nav, int from, byte[] mask, Scratch s)
    {
        int n = nav.CellCount;
        int cs = nav.Hpa!.ClusterSize, cc = nav.Hpa.ClustersPerSide;
        var prev = s.DeckPrev;
        var q = s.DeckQueue;
        prev.Clear(); q.Clear();
        prev[from] = -1; q.Add(from);
        for (int h = 0; h < q.Count; h++)
        {
            int cell = q[h];
            if (nav.Portal[cell] != 0 && nav.Passable[cell] != 0)
            {
                for (int k = cell; k >= 0; k = prev[k]) mask[NavGridSteps.ClusterOf(k, n, cs, cc)] = 1;
                return cell;
            }

            int x = cell % n, y = cell / n;
            TryEnqueue(x + 1, y);
            TryEnqueue(x - 1, y);
            TryEnqueue(x, y + 1);
            TryEnqueue(x, y - 1);

            void TryEnqueue(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= n || ny >= n) return;
                int v = ny * n + nx;
                if (nav.UpPass[v] != 0 && !prev.ContainsKey(v)) { prev[v] = cell; q.Add(v); }
            }
        }

        return -1;
    }

    /// <summary>
    /// 链接层的领队折线(flowPoints 移植):HPA 路点太粗(只到入口),
    /// 领队路径从流场路点链描出;链不到终点或太短(少于 4 点)都算失败。
    /// </summary>
    public static (Fix64[] Points, int[]? Layers)? FlowPoints(FlowField flow, int start)
    {
        int n2 = flow.Integ.Length / 2;
        int n = (int)Math.Sqrt(n2);
        var cells = FlowFieldBuilder.TracePath(flow, start, n2); // 终止上界:路点链每格至多访一次
        if (cells[cells.Count - 1] != flow.Goal) return null;
        var pts = new Fix64[cells.Count * 2];
        var lv = new int[cells.Count];
        bool anyDeck = false;
        for (int i = 0; i < cells.Count; i++)
        {
            int k = cells[i], c = k % n2;
            pts[i * 2] = Fix64.FromInt(c % n) + Fix64.HalfValue;
            pts[i * 2 + 1] = Fix64.FromInt(c / n) + Fix64.HalfValue;
            lv[i] = k / n2;
            anyDeck |= lv[i] != 0;
        }

        if (cells.Count < 4) return null;
        return (pts, anyDeck ? lv : null);
    }
}
