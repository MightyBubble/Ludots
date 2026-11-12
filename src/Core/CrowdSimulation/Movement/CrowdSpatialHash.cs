using System;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 稀疏占格计数排序空间哈希(Fix64 厘米域):每子步重建一次,
/// 局部避让(物理)与意图层的同指令接触查询(到达辅助)共读。
/// 格尺寸 = 2 × 最大半径 / rings;半径 r 的单位读 ring(r) = ceil((r + 最大半径) / 格尺寸) 环,
/// 邻格按环序紧凑排列,每单位的读取范围是它所在格邻表的前缀。
/// 无移动快路径:单位数不变且每个单位格位与查询环都未变时整次复用。
/// </summary>
public sealed class CrowdSpatialHash
{
    private static readonly sbyte[] NeighborOx = { 0, 1, -1, 0, 0, 1, -1, 1, -1 };
    private static readonly sbyte[] NeighborOy = { 0, 0, 0, 1, -1, 1, 1, -1, -1 };

    private readonly int _dim;
    private readonly int _rings;
    private readonly Fix64 _reachCm;
    private readonly int[] _start, _count, _cursor;
    private readonly uint[] _stamp;
    private readonly byte[] _cellRing;
    private uint _generation;
    private int _lastN = -1;

    public readonly Fix64 CellSizeCm;
    public int Rings => _rings;
    /// <summary>网格边长(格)与查询触及距离(厘米):格剔除的矩形几何用。</summary>
    public int CellsPerSide => _dim;
    public Fix64 Reach => _reachCm;
    public int CellRingOf(int cell) => _cellRing[cell];
    public int StartOf(int cell) => _start[cell];
    public int CountOf(int cell) => _count[cell];
    public readonly int[] Active;
    public int ActiveCount { get; private set; }
    public int Reused { get; private set; }

    /// <summary>占格 → 邻表槽位;邻表按环前缀紧凑。</summary>
    public readonly int[] Slot;
    public readonly byte[] RingCount;
    public readonly int[] NeighborStart, NeighborEnd, NeighborSlot;
    public readonly int[] Items;
    public readonly byte[] ItemRing;
    public readonly byte[] UnitRing;
    public readonly int[] CellOf;
    public readonly int Width;
    public readonly int[] RingEnd;
    public readonly sbyte[] Ox, Oy;

    /// <summary>格边长与触及距离为定点精确值。整数截断会系统性移格划分。</summary>
    public CrowdSpatialHash(int worldSizeCm, Fix64 hashCellSizeCm, int capacity, int rings, Fix64 reachCm)
    {
        CellSizeCm = hashCellSizeCm;
        long cellRaw = hashCellSizeCm.RawValue;
        _cellSizeInt = cellRaw > 0 && (cellRaw & ((1L << Fix64.FractionalBits) - 1)) == 0
            ? (int)(cellRaw >> Fix64.FractionalBits)
            : 0;
        _rings = rings;
        _reachCm = reachCm;
        _dim = (int)Fix64.Ceiling(Fix64.FromInt(worldSizeCm) / hashCellSizeCm).ToLong();
        int cells = _dim * _dim;
        (Ox, Oy, RingEnd) = BuildRingOffsets(rings);
        Width = Ox.Length;
        _start = new int[cells];
        _count = new int[cells];
        _cursor = new int[cells];
        _stamp = new uint[cells];
        _cellRing = new byte[cells];
        Active = new int[cells];
        Slot = new int[cells];
        RingCount = new byte[capacity * (rings + 1)];
        NeighborStart = new int[capacity * Width];
        NeighborEnd = new int[capacity * Width];
        NeighborSlot = new int[capacity * Width];
        Items = new int[capacity];
        ItemRing = new byte[capacity];
        UnitRing = new byte[capacity];
        CellOf = new int[capacity];
    }

    private static (sbyte[] Ox, sbyte[] Oy, int[] RingEnd) BuildRingOffsets(int rings)
    {
        var ox = new System.Collections.Generic.List<sbyte>(NeighborOx);
        var oy = new System.Collections.Generic.List<sbyte>(NeighborOy);
        var ringEnd = new System.Collections.Generic.List<int> { 1, 9 };
        for (int r = 2; r <= rings; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r) { ox.Add((sbyte)dx); oy.Add((sbyte)dy); }
                }
            }

            ringEnd.Add((2 * r + 1) * (2 * r + 1));
        }

        return (ox.ToArray(), oy.ToArray(), ringEnd.ToArray());
    }

    /// <summary>把 stamp 代推到给定值。下一次 Build 先加一，加一溢出时会清 stamp。</summary>
    internal void PrimeStampGeneration(uint generation) => _generation = generation;

    private readonly int _cellSizeInt;

    private int CellAtCm(Fix64 vCm)
    {
        // 格长是正整数时走 raw 整数除（向零）。否则走定点除法。负坐标的商再钳到 0，所以负格号仍是 0。
        int c = _cellSizeInt != 0
            ? (int)Fix64.DivPositiveInt(vCm, _cellSizeInt).ToLong()
            : (int)(vCm / CellSizeCm).ToLong();
        return c < 0 ? 0 : c >= _dim ? _dim - 1 : c;
    }

    private int RingOf(Fix64 radiusCm)
    {
        int ring = (int)Fix64.Ceiling((radiusCm + _reachCm) / CellSizeCm).ToLong();
        return ring > _rings ? _rings : ring < 1 ? 1 : ring;
    }

    /// <summary>重建(每子步一次;x/y/radius 按单位稠密序)。</summary>
    public void Build(Fix64Vec2[] positions, Fix64[] radiiCm, int n)
    {
        if (n > Items.Length) throw new InvalidOperationException("CrowdSpatialHash capacity exceeded");
        if (n == _lastN && _generation != 0)
        {
            int i = 0;
            for (; i < n; i++)
            {
                if (CellOf[i] != CellAtCm(positions[i].Y) * _dim + CellAtCm(positions[i].X)) break;
                if (UnitRing[i] != RingOf(radiiCm[i])) break;
            }

            if (i == n) { Reused++; return; }
        }

        _lastN = n;
        uint generation = _generation + 1;
        if (generation == 0)
        {
            Array.Clear(_stamp);
            generation = 1;
        }

        _generation = generation;
        int rr = _rings + 1;
        int activeCount = 0;
        for (int i = 0; i < n; i++)
        {
            int c = CellAtCm(positions[i].Y) * _dim + CellAtCm(positions[i].X);
            int ring = RingOf(radiiCm[i]);
            UnitRing[i] = (byte)ring;
            CellOf[i] = c;
            if (_stamp[c] != generation)
            {
                _stamp[c] = generation;
                _count[c] = 0;
                _cellRing[c] = 0;
                Active[activeCount++] = c;
            }

            _count[c]++;
            if (ring > _cellRing[c]) _cellRing[c] = (byte)ring;
        }

        int offset = 0;
        for (int k = 0; k < activeCount; k++)
        {
            int c = Active[k];
            _start[c] = offset;
            _cursor[c] = offset;
            Slot[c] = k;
            offset += _count[c];
        }

        for (int k = 0; k < activeCount; k++)
        {
            int c = Active[k], cy = c / _dim, cx = c - cy * _dim, baseIdx = k * Width, need = _cellRing[c];
            int found = 0, o = 0;
            for (int r = 0; r <= need; r++)
            {
                for (int e = RingEnd[r]; o < e; o++)
                {
                    int nx = cx + Ox[o], ny = cy + Oy[o];
                    if (nx < 0 || ny < 0 || nx >= _dim || ny >= _dim) continue;
                    int cellIndex = ny * _dim + nx;
                    if (_stamp[cellIndex] == generation)
                    {
                        int pos = baseIdx + found++;
                        NeighborStart[pos] = _start[cellIndex];
                        NeighborEnd[pos] = _start[cellIndex] + _count[cellIndex];
                        NeighborSlot[pos] = Slot[cellIndex];
                    }
                }

                RingCount[k * rr + r] = (byte)found;
            }
        }

        ActiveCount = activeCount;
        // 按单位下标插入:格内邻序与稠密序一致(回放可复现)。
        for (int i = 0; i < n; i++)
        {
            int p = _cursor[CellOf[i]]++;
            Items[p] = i;
            ItemRing[p] = UnitRing[i];
        }
    }

    public void CellRect(Fix64 minX, Fix64 minY, Fix64 maxX, Fix64 maxY, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = CellAtCm(minX);
        y0 = CellAtCm(minY);
        x1 = CellAtCm(maxX) + 1;
        y1 = CellAtCm(maxY) + 1;
        if (x1 > _dim) x1 = _dim;
        if (y1 > _dim) y1 = _dim;
    }

    /// <summary>过期格的计数在重建前仍留着，只认本代戳。</summary>
    public int CopyItemsInRect(int x0, int y0, int x1, int y1, int[] dest)
    {
        if (x0 < 0) x0 = 0;
        if (y0 < 0) y0 = 0;
        if (x1 > _dim) x1 = _dim;
        if (y1 > _dim) y1 = _dim;
        int written = 0;
        uint generation = _generation;
        for (int y = y0; y < y1 && written < dest.Length; y++)
        {
            int row = y * _dim;
            for (int x = x0; x < x1 && written < dest.Length; x++)
            {
                int cell = row + x;
                if (_stamp[cell] != generation || _count[cell] == 0) continue;
                int start = _start[cell];
                int n = _count[cell];
                for (int i = 0; i < n && written < dest.Length; i++)
                    dest[written++] = Items[start + i];
            }
        }

        return written;
    }
}
