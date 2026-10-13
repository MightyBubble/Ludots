using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 流场(双层):积分场(自终点的 Dijkstra,限于 HPA 走廊掩码)
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
    /// <summary>走廊掩码的池内缓冲。Build 把当次掩码抄进来，调用方的暂存可以马上复用。</summary>
    public byte[]? MaskBuf { get; set; }
    public NavLinkSet? Links { get; set; }
    public byte[]? Mask { get; set; }
    public int Reached { get; set; }
    public int Goal { get; set; }
    public int NavId { get; set; }
    /// <summary>本缓冲被 Build 成功填过的次数。池内复用继续累加。诊断身份，不进合计。</summary>
    public int BuildRevision { get; internal set; }
    /// <summary>来源池(还回时物归原主,池才能平台化)。</summary>
    public FlowPool? OriginPool { get; set; }
    /// <summary>跟随的组加上在途请求。和 <see cref="Cached"/> 都是 0 才可以回池。</summary>
    public int Refs { get; set; }
    /// <summary>仍占着结果缓存的一个条目。</summary>
    public bool Cached { get; set; }
    /// <summary>已经在来源池的空闲栈里。重复 Give 直接返回。</summary>
    public bool Pooled { get; set; }
}

/// <summary>
/// 流场缓冲池(路径服务计算侧):建成场离开服务(交给仿真侧),
/// 仿真侧弃用时还回缓冲,稳态规划零分配。构建暂存(堆 / 弹出序)由池主独占。
/// Give/Take 带锁:仿真侧还回与 worker 取用可能并发;结果与时序无关。
/// </summary>
public sealed class FlowPool
{
    private readonly int _capacity;
    private readonly Stack<FlowField> _free = new();
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

    /// <summary>空闲栈按引用查找。枚举会分配，只给调试校验用。</summary>
    internal bool Holds(FlowField field)
    {
        lock (_freeLock)
        {
            foreach (var item in _free)
            {
                if (ReferenceEquals(item, field)) return true;
            }
        }

        return false;
    }

    /// <summary>取未初始化缓冲(构建方会填满它读到的每个数组)。场对象和数组一起复用。</summary>
    public FlowField Take()
    {
        lock (_freeLock)
        {
            if (_free.Count > 0)
            {
                Reused++;
                var reused = _free.Pop();
                reused.Pooled = false;
                reused.Refs = 0;
                reused.Cached = false;
                return reused;
            }
        }

        Allocated++;
        return new FlowField
        {
            Integ = new Fix64[LayeredCount],
            Len = new Fix64[LayeredCount],
            Wp = new int[LayeredCount],
            LkBuf = null,
            OriginPool = this,
        };
    }

    public void Give(FlowField f)
    {
        if (f.Integ.Length != LayeredCount) return;
        // 组或缓存在用的缓冲不能进空闲栈。Admit 和换场可能先后还回同一份。
        if (f.Refs > 0 || f.Cached) return;
        f.Links = null;
        f.Lk = null;
        lock (_freeLock)
        {
            if (f.Pooled) return;
            if (_free.Count >= _capacity) return;
            f.Pooled = true;
            _free.Push(f);
        }
    }
}

/// <summary>流场构建与读取。</summary>
public static class FlowFieldBuilder
{
    private const int Cross = -2;

    public static FlowField Build(NavContext nav, int goal, byte[] mask, FlowPool pool)
    {
        int navCellsPerSide = nav.NavCellsPerSide, n2 = navCellsPerSide * navCellsPerSide;
        var links = nav.Links;
        bool layered = nav.UpperTiles is { Count: > 0 };
        int navCellsPerTile = nav.Hpa!.NavCellsPerTile, navTilesPerSide = nav.Hpa.NavTilesPerSide;
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
            int x = cell % navCellsPerSide, y = cell / navCellsPerSide;
            for (int d = 0; d < 8; d++)
            {
                if (!NavGridSteps.CanStep(grid, navCellsPerSide, x, y, d)) continue;
                int vc = (y + NavGridSteps.DY[d]) * navCellsPerSide + x + NavGridSteps.DX[d], v = o + vc;
                if (mask[NavGridSteps.TileOf(vc, navCellsPerSide, navCellsPerTile, navTilesPerSide)] == 0) continue;
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
                    if (mask[NavGridSteps.TileOf(v, navCellsPerSide, navCellsPerTile, navTilesPerSide)] == 0) continue;
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

            int x = cell % navCellsPerSide, y = cell / navCellsPerSide;
            bool done = false;
            if (links != null && lv == 0)
            {
                for (int j = links.OutStart[i], e1 = links.OutStart[i + 1]; j < e1; j++)
                {
                    int e = links.OutList[j];
                    // 未到达的链接端积分是 MaxValue。不加守卫时 MaxValue + cost 会溢出回绕成负数,判等即真。
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
                if (!NavGridSteps.CanStep(grid, navCellsPerSide, x, y, d)) continue;
                int v = o + (y + NavGridSteps.DY[d]) * navCellsPerSide + x + NavGridSteps.DX[d];
                if (integ[v] < best) { best = integ[v]; bd = d; }
            }

            if (bd < 0) continue;
            int p = o + (y + NavGridSteps.DY[bd]) * navCellsPerSide + x + NavGridSteps.DX[bd];
            int cand = wp[p];
            // 拉直不跨层(另一层的路点经它的 portal 到达)
            int t = cand >= o && cand < o + n2 && NavGridSteps.LineOfSight(grid, navCellsPerSide, cell, cand - o) ? cand : p;
            wp[i] = t;
            int tc = t - o, dx = tc % navCellsPerSide - x, dy = tc / navCellsPerSide - y;
            len[i] = len[t] + Fix64Math.Sqrt(Fix64.FromInt(dx * dx + dy * dy));
        }

        if (f.MaskBuf == null || f.MaskBuf.Length != mask.Length) f.MaskBuf = new byte[mask.Length];
        Array.Copy(mask, f.MaskBuf, mask.Length);
        f.Goal = goal; f.Mask = f.MaskBuf; f.Reached = reached; f.Lk = lk; f.Links = links; f.NavId = nav.Id;
        f.BuildRevision++;
        return f;
    }

    public static void PadMaskInto(byte[] mask, int navTilesPerSide, int pad, byte[] output)
    {
        Array.Clear(output, 0, output.Length);
        if (pad == 0)
        {
            Array.Copy(mask, output, mask.Length);
            return;
        }

        for (int cy = 0; cy < navTilesPerSide; cy++)
        {
            for (int cx = 0; cx < navTilesPerSide; cx++)
            {
                if (mask[cy * navTilesPerSide + cx] == 0) continue;
                for (int dy = -pad; dy <= pad; dy++)
                {
                    for (int dx = -pad; dx <= pad; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x >= 0 && y >= 0 && x < navTilesPerSide && y < navTilesPerSide) output[y * navTilesPerSide + x] = 1;
                    }
                }
            }
        }
    }

    public static void OrMaskInto(byte[] a, byte[] b, byte[] output)
    {
        for (int i = 0; i < a.Length; i++) output[i] = (byte)(a[i] | b[i]);
    }

    /// <summary>沿拉直路点链走(单位实际行走的绷紧路径),跳跃链接处跳接。</summary>
    public static List<int> TracePath(FlowField flow, int start, int maxSteps)
    {
        var cells = new List<int>();
        TracePathInto(flow, start, maxSteps, cells);
        return cells;
    }

    public static void TracePathInto(FlowField flow, int start, int maxSteps, List<int> cells)
    {
        cells.Clear();
        cells.Add(start);
        int c = start;
        for (int step = 0; step < maxSteps && c != flow.Goal; step++)
        {
            int next = flow.Lk != null && c < flow.Lk.Length && flow.Lk[c] >= 0 ? flow.Links!.To[flow.Lk[c]] : flow.Wp[c];
            if (next < 0 || next == c) break;
            cells.Add(next);
            c = next;
        }
    }

    public static byte[] PadMask(byte[] mask, int navTilesPerSide, int pad)
    {
        if (pad == 0) return mask;
        var output = new byte[mask.Length];
        for (int cy = 0; cy < navTilesPerSide; cy++)
        {
            for (int cx = 0; cx < navTilesPerSide; cx++)
            {
                if (mask[cy * navTilesPerSide + cx] == 0) continue;
                for (int dy = -pad; dy <= pad; dy++)
                {
                    for (int dx = -pad; dx <= pad; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x >= 0 && y >= 0 && x < navTilesPerSide && y < navTilesPerSide) output[y * navTilesPerSide + x] = 1;
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
