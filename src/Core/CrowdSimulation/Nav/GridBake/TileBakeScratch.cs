using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation;

namespace Ludots.Core.CrowdSimulation.Nav.GridBake;

/// <summary>
/// 一次 tile 烘焙的全部临时缓冲与可复用多边形数组。
/// 构造时按 tile 边长把缓冲定死。轮廓池或顶点池用尽时仍会 new；RentLoop 池空时把新表交给调用方。
/// </summary>
internal sealed class TileBakeScratch
{
    public readonly int NavCellsPerTile;
    public readonly int Nn;
    public readonly int MaxVerts;
    public readonly int PolyMax;
    public readonly int VertMax;
    public readonly int EdgeMax;
    public readonly int BorderMax;

    public readonly byte[] Pass;
    public readonly byte[] Dist;
    public readonly int[] Reg;
    public readonly int[] Off;
    public readonly int[] Sorted;
    public readonly int[] Pending;
    public readonly int[] StackBuf;
    public readonly int[] Assign;
    public readonly int[] WArea;
    public readonly int[] WParent;
    public readonly int[] WRemap;
    public readonly List<(int Q, int C)>[] WNb;
    public readonly Dictionary<int, int>[] WNbIndex;
    public readonly List<int> WIds;
    public readonly List<int> WAggOrder;
    public readonly Dictionary<int, int> WAggCount;
    public readonly AreaBySize Comparer;

    public readonly byte[] Flags;
    public readonly List<List<int>>[] Loops;
    public readonly List<int> Raw;
    public readonly List<int> Keep;
    public readonly List<int> OutIdx;
    private readonly List<int>[] _loopPool;
    private int _loopPoolCount;
    private int _loopsLive;

    public readonly PolygonBakeScratch Polys = new();
    public readonly Dictionary<int, int> Vmap;
    public readonly Dictionary<int, int> Emap;
    public readonly Dictionary<int, int> EdgeIndex;
    public readonly List<int> Vx;
    public readonly List<int> Vy;
    public readonly List<int> NbFlat;
    public readonly List<int> BdFlat;
    public readonly List<int> TriIdx;
    public readonly List<int> Tris;
    public readonly List<int> Merged;
    public readonly List<int[]> PolyList;
    public readonly List<int[]> TriPoly;
    public readonly List<(int Key, int Val)> Edges;
    public readonly List<List<int>> HoleLoops;
    public readonly List<List<int>> HoleList;
    public readonly List<List<int>> Borrowed;
    public readonly List<int> MergeA;
    public readonly List<int> MergeB;
    public readonly int[] Queue;
    public readonly int[] Neighbor4;
    private readonly Stack<int[]>[] _rent;
    private int _mergeSide;

    private List<int> _dpRaw = null!;
    private int _dpN;
    private double _dpE2;
    private double _dpM2;

    public TileBakeScratch(int navCellsPerTile, int maxVertsPerPoly)
    {
        NavCellsPerTile = navCellsPerTile;
        Nn = navCellsPerTile * navCellsPerTile;
        MaxVerts = maxVertsPerPoly;
        PolyMax = Nn;
        VertMax = (navCellsPerTile + 1) * (navCellsPerTile + 1);
        EdgeMax = PolyMax * maxVertsPerPoly;
        BorderMax = EdgeMax;

        Pass = new byte[Nn];
        Dist = new byte[Nn];
        Reg = new int[Nn];
        Off = new int[257];
        Sorted = new int[Nn];
        Pending = new int[Nn];
        StackBuf = new int[Nn * 4 + 4];
        Assign = new int[Nn * 2];
        int maxR = Nn + 1;
        WArea = new int[maxR];
        WParent = new int[maxR];
        WRemap = new int[maxR];
        WNb = new List<(int, int)>[maxR];
        WNbIndex = new Dictionary<int, int>[maxR];
        for (int i = 0; i < maxR; i++)
        {
            WNb[i] = new List<(int, int)>(32);
            WNbIndex[i] = new Dictionary<int, int>(32);
        }

        WIds = new List<int>(maxR);
        WAggOrder = new List<int>(16);
        WAggCount = new Dictionary<int, int>(16);
        Comparer = new AreaBySize();
        Flags = new byte[Nn];
        Loops = new List<List<int>>[maxR];
        for (int i = 0; i < maxR; i++) Loops[i] = new List<List<int>>(4);
        Raw = new List<int>(Nn * 3);
        Keep = new List<int>(CrowdSimulationLimits.HotPathSlotReserve);
        OutIdx = new List<int>(CrowdSimulationLimits.HotPathSlotReserve);
        _loopPool = new List<int>[128];
        for (int i = 0; i < _loopPool.Length; i++) _loopPool[i] = new List<int>(CrowdSimulationLimits.HotPathSpanReserve);
        _loopPoolCount = _loopPool.Length;

        StableOrder.Ensure(ref Polys.HoleRank, Nn);
        StableOrder.Ensure(ref Polys.HoleVertex, Nn);
        StableOrder.Ensure(ref Polys.CandRank, VertMax);
        Vmap = new Dictionary<int, int>(VertMax);
        Emap = new Dictionary<int, int>(EdgeMax);
        EdgeIndex = new Dictionary<int, int>(EdgeMax);
        Vx = new List<int>(VertMax);
        Vy = new List<int>(VertMax);
        NbFlat = new List<int>(EdgeMax * 3);
        BdFlat = new List<int>(BorderMax * 5);
        TriIdx = new List<int>(VertMax);
        Tris = new List<int>(EdgeMax);
        Merged = new List<int>(maxVertsPerPoly);
        PolyList = new List<int[]>(PolyMax);
        TriPoly = new List<int[]>(PolyMax);
        Edges = new List<(int, int)>(EdgeMax);
        HoleLoops = new List<List<int>>(32);
        HoleList = new List<List<int>>(16);
        Borrowed = new List<List<int>>(32);
        MergeA = new List<int>(VertMax * 2);
        MergeB = new List<int>(VertMax * 2);
        Queue = new int[Nn];
        Neighbor4 = new int[4];
        _rent = new Stack<int[]>[maxVertsPerPoly + 1];
        for (int len = 3; len <= maxVertsPerPoly; len++)
        {
            var stack = new Stack<int[]>(len == 3 ? 1024 : 512);
            int n = len == 3 ? 1024 : 512;
            for (int i = 0; i < n; i++) stack.Push(new int[len]);
            _rent[len] = stack;
        }
    }

    public void BeginTile()
    {
        ReturnLiveLoops();
        ReturnPolyArrays(PolyList);
        ReturnPolyArrays(TriPoly);
        Vmap.Clear();
        Emap.Clear();
        EdgeIndex.Clear();
        Vx.Clear();
        Vy.Clear();
        NbFlat.Clear();
        BdFlat.Clear();
        TriIdx.Clear();
        Tris.Clear();
        Merged.Clear();
        PolyList.Clear();
        TriPoly.Clear();
        Edges.Clear();
        HoleLoops.Clear();
        HoleList.Clear();
        for (int i = 0; i < Borrowed.Count; i++) ReturnLoop(Borrowed[i]);
        Borrowed.Clear();
        MergeA.Clear();
        MergeB.Clear();
    }

    public List<int> RentLoop()
    {
        if (_loopPoolCount == 0)
        {
            var grown = new List<int>[_loopPool.Length * 2];
            Array.Copy(_loopPool, grown, _loopPool.Length);
            for (int i = _loopPool.Length; i < grown.Length; i++) grown[i] = new List<int>(CrowdSimulationLimits.HotPathSpanReserve);
            // 池在构造时已按轮廓上限留够;这里只在超出时长大,调用方会在对拍里看见。
            return new List<int>(CrowdSimulationLimits.HotPathSpanReserve);
        }

        var list = _loopPool[--_loopPoolCount];
        list.Clear();
        return list;
    }

    public void ReturnLoop(List<int> list)
    {
        if (_loopPoolCount < _loopPool.Length) _loopPool[_loopPoolCount++] = list;
    }

    public int[] RentVerts(int length)
    {
        var stack = _rent[length];
        if (stack != null && stack.Count > 0) return stack.Pop();
        return new int[length];
    }

    public void ReturnVerts(int[] array)
    {
        var stack = _rent[array.Length];
        stack?.Push(array);
    }

    public List<int> Simplify(List<int> raw, double maxError, double maxEdgeLen)
    {
        _dpRaw = raw;
        _dpN = raw.Count / 3;
        _dpE2 = maxError * maxError;
        _dpM2 = maxEdgeLen * maxEdgeLen;
        int n = _dpN;
        Keep.Clear();
        for (int i = 0; i < n; i++)
        {
            if (DpL(i) != DpL((i + 1) % n)) Keep.Add(i);
        }

        if (Keep.Count < 2)
        {
            int ll = 0, ur = 0;
            for (int i = 1; i < n; i++)
            {
                if (DpX(i) < DpX(ll) || (DpX(i) == DpX(ll) && DpY(i) < DpY(ll))) ll = i;
                if (DpX(i) > DpX(ur) || (DpX(i) == DpX(ur) && DpY(i) > DpY(ur))) ur = i;
            }

            Keep.Clear();
            Keep.Add(Math.Min(ll, ur));
            Keep.Add(Math.Max(ll, ur));
        }

        OutIdx.Clear();
        for (int k = 0; k < Keep.Count; k++)
        {
            int a = Keep[k], b = Keep[(k + 1) % Keep.Count];
            OutIdx.Add(a);
            Dp(a, b, DpL((a + 1) % n) == 0);
        }

        var pts = RentLoop();
        for (int t = 0; t < OutIdx.Count; t++)
        {
            int i = OutIdx[t];
            int x = DpX(i), y = DpY(i), m = pts.Count;
            if (m > 0 && pts[m - 2] == x && pts[m - 1] == y) continue;
            pts.Add(x);
            pts.Add(y);
        }

        if (pts.Count >= 4 && pts[0] == pts[pts.Count - 2] && pts[1] == pts[pts.Count - 1]) pts.RemoveRange(pts.Count - 2, 2);
        return pts;
    }

    private void Dp(int a, int b, bool wall)
    {
        int n = _dpN;
        int ax = DpX(a), ay = DpY(a), dx = DpX(b) - ax, dy = DpY(b) - ay;
        int len2 = dx * dx + dy * dy;
        int best = -1, bestC = -1, cnt = 0;
        for (int i = (a + 1) % n; i != b; i = (i + 1) % n)
        {
            cnt++;
            int c = len2 != 0
                ? Math.Abs(dx * (DpY(i) - ay) - dy * (DpX(i) - ax))
                : (DpX(i) - ax) * (DpX(i) - ax) + (DpY(i) - ay) * (DpY(i) - ay);
            if (c > bestC || (c == bestC && best >= 0 && (DpX(i) < DpX(best) || (DpX(i) == DpX(best) && DpY(i) < DpY(best)))))
            {
                bestC = c;
                best = i;
            }
        }

        if (best < 0) return;
        bool split = len2 == 0 || bestC * (double)bestC > _dpE2 * len2;
        if (!split && wall && len2 > _dpM2)
        {
            split = true;
            best = (a + 1 + (cnt >> 1)) % n;
        }

        if (!split) return;
        Dp(a, best, wall);
        OutIdx.Add(best);
        Dp(best, b, wall);
    }

    private int DpX(int i) => _dpRaw[i * 3];

    private int DpY(int i) => _dpRaw[i * 3 + 1];

    private int DpL(int i) => _dpRaw[i * 3 + 2];

    public void NoteLoops(int regionCount) => _loopsLive = regionCount + 1;

    private void ReturnLiveLoops()
    {
        for (int i = 0; i < _loopsLive; i++)
        {
            var bucket = Loops[i];
            for (int k = 0; k < bucket.Count; k++) ReturnLoop(bucket[k]);
            bucket.Clear();
        }

        _loopsLive = 0;
    }

    private void ReturnPolyArrays(List<int[]> polys)
    {
        for (int i = 0; i < polys.Count; i++)
        {
            if (polys[i] != null) ReturnVerts(polys[i]);
        }
    }

    public List<int> MergeTarget()
    {
        var target = (_mergeSide++ & 1) == 0 ? MergeA : MergeB;
        target.Clear();
        return target;
    }

    public sealed class AreaBySize : IComparer<int>
    {
        public int[] Area = Array.Empty<int>();

        public int Compare(int a, int b) => Area[a] != Area[b] ? Area[a] - Area[b] : a - b;
    }
}
