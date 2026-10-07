using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 流场(flowfield.js 移植,LY-5 双层):积分场(自终点的 Dijkstra,限于 HPA 走廊掩码)
/// + 每格 8 向下降指针 + 拉直路点(按 Dijkstra 弹出序处理,父先子后 → 任意角绷紧路径)。
/// 每个数组都是 2 × N²:下标 = 层 × N² + 格(层 0 地面,1 桥面);两层仅在双层可走的
/// 桥头格相交(0 代价 0 长度边;经该边到达的格 wp = 另一层同格)。
/// len = 每格绷紧路线的几何长度(格),与地形代价无关,是所有到达阈值的长度单位。
/// </summary>
public sealed class FlowField
{
    public required Fix64[] Integ { get; set; }
    public required Fix64[] Len { get; set; }
    public required int[] Wp { get; set; }
    /// <summary>跳跃链接的出路格 → 链接号(仅地面,N²;无链接能力为 null)。</summary>
    public int[]? Lk { get; set; }
    /// <summary>随池缓冲保留的 Lk 后备数组(避免反复分配)。</summary>
    public int[]? LkBuf { get; set; }
    public NavLinkSet? Links { get; set; }
    public byte[]? Mask { get; set; }
    public int Reached { get; set; }
    public int Goal { get; set; }
    public int NavId { get; set; }
    /// <summary>来源池(还回时物归原主,池才能平台化)。</summary>
    public FlowPool? OriginPool { get; set; }
}

/// <summary>
/// 流场缓冲池(flowPool.js 移植,路径服务计算侧):建成场离开服务(交给仿真侧),
/// 仿真侧弃用时还回缓冲,稳态规划零分配。构建暂存(堆 / 弹出序)由池主独占。
/// Give/Take 带锁:仿真侧还回与 worker 取用可能并发;结果与时序无关。
/// </summary>
public sealed class FlowPool
{
    private readonly int _capacity;
    private readonly Stack<(Fix64[] Integ, Fix64[] Len, int[] Wp, int[]? LkBuf)> _free = new();
    private readonly object _freeLock = new();

    public FlowPool(int cellCount, int capacity)
    {
        LayeredCount = 2 * cellCount * cellCount;
        GroundCount = cellCount * cellCount;
        _capacity = capacity;
        Heap = new NavMinHeap(8192);
        Order = new int[LayeredCount];
    }

    /// <summary>2 × N²(双层数组长度)。</summary>
    public int LayeredCount { get; }
    /// <summary>N²(地面层格数)。</summary>
    public int GroundCount { get; }
    public NavMinHeap Heap { get; }
    public int[] Order { get; }
    public int Allocated { get; private set; }
    public int Reused { get; private set; }
    public int FreeCount { get { lock (_freeLock) { return _free.Count; } } }

    /// <summary>取未初始化缓冲(构建方会填满它读到的每个数组)。</summary>
    public FlowField Take()
    {
        (Fix64[] Integ, Fix64[] Len, int[] Wp, int[]? LkBuf) b;
        lock (_freeLock)
        {
            if (_free.Count > 0) { b = _free.Pop(); Reused++; }
            else { b = (null!, null!, null!, null); }
        }

        if (b.Integ == null)
        {
            Allocated++;
            b = (new Fix64[LayeredCount], new Fix64[LayeredCount], new int[LayeredCount], null);
        }

        return new FlowField { Integ = b.Integ, Len = b.Len, Wp = b.Wp, LkBuf = b.LkBuf, OriginPool = this };
    }

    public void Give(FlowField f)
    {
        if (f.Integ.Length != LayeredCount) return;
        lock (_freeLock)
        {
            if (_free.Count < _capacity) _free.Push((f.Integ, f.Len, f.Wp, f.LkBuf));
        }
    }
}

/// <summary>流场构建与读取(buildFlowField / tracePath / padMask / maskSubset / orMask 移植)。</summary>
public static class FlowFieldBuilder
{
    private const int Cross = -2;

    public static FlowField Build(NavContext nav, int goal, byte[] mask, FlowPool pool)
    {
        int n = nav.CellCount, n2 = n * n;
        var links = nav.Links;
        bool layered = nav.UpperTiles is { Count: > 0 };
        int s = nav.Hpa!.ClusterSize, c = nav.Hpa.ClustersPerSide;
        var f = pool.Take();
        var integ = f.Integ; var len = f.Len; var wp = f.Wp;
        var heap = pool.Heap; var order = pool.Order;
        Array.Fill(integ, Fix64.MaxValue);
        Array.Fill(len, Fix64.MaxValue);
        Array.Fill(wp, -1);
        heap.Clear();
        integ[goal] = Fix64.Zero; len[goal] = Fix64.Zero;
        heap.Push(Fix64.Zero, goal);
        int reached = 0;
        while (heap.Size > 0)
        {
            var k = heap.PeekKey();
            int u = heap.Pop();
            if (k > integ[u]) continue;
            order[reached++] = u;
            int lv = u >= n2 ? 1 : 0, o = lv * n2, cell = u - o;
            var grid = lv != 0 ? nav.UpPass : nav.Passable;
            var cst = lv != 0 ? nav.UpCost : nav.Cost;
            int x = cell % n, y = cell / n;
            for (int d = 0; d < 8; d++)
            {
                if (!NavGridSteps.CanStep(grid, n, x, y, d)) continue;
                int vc = (y + NavGridSteps.DY[d]) * n + x + NavGridSteps.DX[d], v = o + vc;
                if (mask[NavGridSteps.ClusterOf(vc, n, s, c)] == 0) continue;
                var nd = k + NavGridSteps.StepCost(cst, cell, vc, d);
                if (nd < integ[v])
                {
                    integ[v] = nd; len[v] = len[u] + (d >= 4 ? NavGridSteps.Sqrt2 : Fix64.OneValue); wp[v] = -1; heap.Push(nd, v);
                }
            }

            if (layered && nav.Portal[cell] != 0 && nav.Passable[cell] != 0 && nav.UpPass[cell] != 0)
            {
                int v = lv != 0 ? cell : n2 + cell;
                if (k < integ[v]) { integ[v] = k; len[v] = len[u]; wp[v] = Cross; heap.Push(k, v); }
            }

            if (links != null && lv == 0)
            {
                for (int j = links.InStart[u], e1 = links.InStart[u + 1]; j < e1; j++)
                {
                    int e = links.InList[j], v = links.From[e];
                    if (mask[NavGridSteps.ClusterOf(v, n, s, c)] == 0) continue;
                    var nd = k + links.Cost[e];
                    if (nd < integ[v])
                    {
                        integ[v] = nd; len[v] = len[u] + Fix64.FromFloat(links.LengthCells[e]); wp[v] = -1; heap.Push(nd, v);
                    }
                }
            }
        }

        int[]? lk = null;
        if (links != null)
        {
            lk = f.LkBuf ?? (f.LkBuf = new int[n2]);
            Array.Fill(lk, -1);
        }

        wp[goal] = goal;
        // 弹出序 = 父先子后:格的路点(及其绷紧长度)在该格处理前已定型
        for (int q = 1; q < reached; q++)
        {
            int i = order[q];
            int lv = i >= n2 ? 1 : 0, o = lv * n2, cell = i - o;
            var grid = lv != 0 ? nav.UpPass : nav.Passable;
            if (wp[i] == Cross)
            {
                int t2 = lv != 0 ? cell : n2 + cell;
                wp[i] = t2; len[i] = len[t2];
                continue;
            }

            int x = cell % n, y = cell / n;
            bool done = false;
            if (links != null && lv == 0)
            {
                for (int j = links.OutStart[i], e1 = links.OutStart[i + 1]; j < e1; j++)
                {
                    int e = links.OutList[j];
                    // 参考实现里 Infinity + cost 仍是 Infinity,不会误选未到达的链接端;
                    // 定点不加守卫会溢出回绕成负数,判等即真(已踩过)
                    if (integ[links.To[e]] < Fix64.MaxValue / 4 && integ[links.To[e]] + links.Cost[e] <= integ[i])
                    {
                        lk![i] = e; wp[i] = i; len[i] = len[links.To[e]] + Fix64.FromFloat(links.LengthCells[e]);
                        done = true;
                        break;
                    }
                }
            }

            if (done) continue;
            var best = integ[i];
            int bd = -1;
            for (int d = 0; d < 8; d++)
            {
                if (!NavGridSteps.CanStep(grid, n, x, y, d)) continue;
                int v = o + (y + NavGridSteps.DY[d]) * n + x + NavGridSteps.DX[d];
                if (integ[v] < best) { best = integ[v]; bd = d; }
            }

            if (bd < 0) continue;
            int p = o + (y + NavGridSteps.DY[bd]) * n + x + NavGridSteps.DX[bd];
            int cand = wp[p];
            // 拉直不跨层(另一层的路点经它的 portal 到达)
            int t = cand >= o && cand < o + n2 && NavGridSteps.LineOfSight(grid, n, cell, cand - o) ? cand : p;
            wp[i] = t;
            int tc = t - o, dx = tc % n - x, dy = tc / n - y;
            len[i] = len[t] + Fix64Math.Sqrt(Fix64.FromInt(dx * dx + dy * dy));
        }

        f.Goal = goal; f.Mask = mask; f.Reached = reached; f.Lk = lk; f.Links = links; f.NavId = nav.Id;
        return f;
    }

    /// <summary>沿拉直路点链走(单位实际行走的绷紧路径),跳跃链接处跳接。</summary>
    public static List<int> TracePath(FlowField flow, int start, int maxSteps)
    {
        var cells = new List<int> { start };
        int c = start;
        for (int step = 0; step < maxSteps && c != flow.Goal; step++)
        {
            int next = flow.Lk != null && c < flow.Lk.Length && flow.Lk[c] >= 0 ? flow.Links!.To[flow.Lk[c]] : flow.Wp[c];
            if (next < 0 || next == c) break;
            cells.Add(next);
            c = next;
        }

        return cells;
    }

    public static byte[] PadMask(byte[] mask, int c, int pad)
    {
        if (pad == 0) return mask;
        var output = new byte[mask.Length];
        for (int cy = 0; cy < c; cy++)
        {
            for (int cx = 0; cx < c; cx++)
            {
                if (mask[cy * c + cx] == 0) continue;
                for (int dy = -pad; dy <= pad; dy++)
                {
                    for (int dx = -pad; dx <= pad; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x >= 0 && y >= 0 && x < c && y < c) output[y * c + x] = 1;
                    }
                }
            }
        }

        return output;
    }

    public static bool MaskSubset(byte[] a, byte[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != 0 && b[i] == 0) return false;
        }

        return true;
    }

    public static byte[] OrMask(byte[] a, byte[] b)
    {
        var output = new byte[a.Length];
        for (int i = 0; i < a.Length; i++) output[i] = (byte)(a[i] | b[i]);
        return output;
    }
}
