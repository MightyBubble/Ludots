using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>
/// 空间哈希的稠密计数排序对照。格号、查询环和邻接偏移在这里单独算，不读哈希内部的中间量。
/// </summary>
internal static class CrowdSpatialHashReference
{
    public static void Compare(CrowdSpatialHash hash, Fix64Vec2[] positions, Fix64[] radiiCm, int count)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (positions.Length < count || radiiCm.Length < count)
        {
            throw new ArgumentException("位置或半径短于单位数。");
        }

        int rings = hash.Rings;
        int dim = hash.CellsPerSide;
        var (ox, oy, ringEnd) = Offsets(rings);
        if (ox.Length != hash.Width || ringEnd.Length != rings + 1)
        {
            Fail( $"邻接表宽度 {hash.Width} 与环表 {ringEnd.Length} 对不上");
        }

        for (int i = 0; i < ox.Length; i++)
        {
            if (hash.Ox[i] != ox[i] || hash.Oy[i] != oy[i])
            {
                Fail( $"邻接偏移 [{i}] 与环序不一致");
            }
        }

        for (int r = 0; r < ringEnd.Length; r++)
        {
            if (hash.RingEnd[r] != ringEnd[r]) Fail( $"环 {r} 的结束下标不一致");
        }

        int cells = dim * dim;
        int cellSizeInt = IntegerCellSize(hash.CellSizeCm);
        var cellOf = new int[count];
        var unitRing = new byte[count];
        var seen = new bool[cells];
        var active = new List<int>(count);
        var perCell = new int[cells];
        var cellRing = new byte[cells];
        for (int i = 0; i < count; i++)
        {
            int cx = CellAt(positions[i].X, hash.CellSizeCm, cellSizeInt, dim);
            int cy = CellAt(positions[i].Y, hash.CellSizeCm, cellSizeInt, dim);
            int cell = cy * dim + cx;
            int ring = RingOf(radiiCm[i], hash.Reach, hash.CellSizeCm, rings);
            cellOf[i] = cell;
            unitRing[i] = (byte)ring;
            if (!seen[cell])
            {
                seen[cell] = true;
                active.Add(cell);
            }

            perCell[cell]++;
            if (ring > cellRing[cell]) cellRing[cell] = (byte)ring;
        }

        if (hash.ActiveCount != active.Count)
        {
            Fail( $"占格数 {hash.ActiveCount} ≠ 对照 {active.Count}");
        }

        var start = new int[cells];
        var slotOf = new int[cells];
        int offset = 0;
        for (int k = 0; k < active.Count; k++)
        {
            int cell = active[k];
            if (hash.Active[k] != cell) Fail($"占格序 [{k}] {hash.Active[k]} ≠ 对照 {cell}");
            start[cell] = offset;
            slotOf[cell] = k;
            if (hash.StartOf(cell) != offset) Fail($"格 {cell} 起点 {hash.StartOf(cell)} ≠ 对照 {offset}");
            if (hash.CountOf(cell) != perCell[cell]) Fail($"格 {cell} 单位数 {hash.CountOf(cell)} ≠ 对照 {perCell[cell]}");
            if (hash.Slot[cell] != k) Fail($"格 {cell} 槽位 {hash.Slot[cell]} ≠ 对照 {k}");
            if (hash.CellRingOf(cell) != cellRing[cell]) Fail($"格 {cell} 查询环 {hash.CellRingOf(cell)} ≠ 对照 {cellRing[cell]}");
            offset += perCell[cell];
        }

        var cursor = (int[])start.Clone();
        var items = new int[count];
        var itemRing = new byte[count];
        for (int i = 0; i < count; i++)
        {
            if (hash.CellOf[i] != cellOf[i]) Fail( $"单位 {i} 所在格 {hash.CellOf[i]} ≠ 对照 {cellOf[i]}");
            if (hash.UnitRing[i] != unitRing[i]) Fail( $"单位 {i} 查询环 {hash.UnitRing[i]} ≠ 对照 {unitRing[i]}");
            int place = cursor[cellOf[i]]++;
            items[place] = i;
            itemRing[place] = unitRing[i];
        }

        for (int i = 0; i < count; i++)
        {
            if (hash.Items[i] != items[i]) Fail( $"格内序 [{i}] 单位 {hash.Items[i]} ≠ 对照 {items[i]}");
            if (hash.ItemRing[i] != itemRing[i]) Fail( $"格内序 [{i}] 环 {hash.ItemRing[i]} ≠ 对照 {itemRing[i]}");
        }

        int width = ox.Length;
        int ringSpan = rings + 1;
        for (int k = 0; k < active.Count; k++)
        {
            int cell = active[k];
            int cy = cell / dim;
            int cx = cell - cy * dim;
            int need = cellRing[cell];
            int found = 0;
            int o = 0;
            for (int r = 0; r <= need; r++)
            {
                for (int end = ringEnd[r]; o < end; o++)
                {
                    int nx = cx + ox[o];
                    int ny = cy + oy[o];
                    if (nx < 0 || ny < 0 || nx >= dim || ny >= dim) continue;
                    int neighbor = ny * dim + nx;
                    if (!seen[neighbor]) continue;
                    int pos = k * width + found;
                    if (hash.NeighborStart[pos] != start[neighbor]
                        || hash.NeighborEnd[pos] != start[neighbor] + perCell[neighbor]
                        || hash.NeighborSlot[pos] != slotOf[neighbor])
                    {
                        Fail( $"格 {cell} 环 {r} 的邻居区间与对照不一致");
                    }

                    found++;
                }

                if (hash.RingCount[k * ringSpan + r] != found)
                {
                    Fail( $"格 {cell} 环 {r} 邻居数 {hash.RingCount[k * ringSpan + r]} ≠ 对照 {found}");
                }
            }
        }
    }

    private static int IntegerCellSize(Fix64 cellSize)
    {
        long raw = cellSize.RawValue;
        long frac = (1L << Fix64.FractionalBits) - 1;
        return raw > 0 && (raw & frac) == 0 ? (int)(raw >> Fix64.FractionalBits) : 0;
    }

    private static int CellAt(Fix64 valueCm, Fix64 cellSize, int cellSizeInt, int dim)
    {
        int cell = cellSizeInt != 0
            ? (int)Fix64.DivPositiveInt(valueCm, cellSizeInt).ToLong()
            : (int)(valueCm / cellSize).ToLong();
        if (cell < 0) return 0;
        return cell >= dim ? dim - 1 : cell;
    }

    private static int RingOf(Fix64 radiusCm, Fix64 reachCm, Fix64 cellSize, int rings)
    {
        int ring = (int)Fix64.Ceiling((radiusCm + reachCm) / cellSize).ToLong();
        if (ring > rings) return rings;
        return ring < 1 ? 1 : ring;
    }

    private static (sbyte[] Ox, sbyte[] Oy, int[] RingEnd) Offsets(int rings)
    {
        var ox = new List<sbyte> { 0, 1, -1, 0, 0, 1, -1, 1, -1 };
        var oy = new List<sbyte> { 0, 0, 0, 1, -1, 1, 1, -1, -1 };
        var ringEnd = new List<int> { 1, 9 };
        for (int r = 2; r <= rings; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r)
                    {
                        ox.Add((sbyte)dx);
                        oy.Add((sbyte)dy);
                    }
                }
            }

            ringEnd.Add((2 * r + 1) * (2 * r + 1));
        }

        return (ox.ToArray(), oy.ToArray(), ringEnd.ToArray());
    }

    private static void Fail(string detail) =>
        throw new CrowdSimulationInvariantException(
            $"空间哈希与稠密计数排序不一致：{detail}",
            -1, -1, -1, -1, -1, -1, -1, -1);
}
