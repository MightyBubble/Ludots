using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Presentation;

internal struct CrowdDebugSegment
{
    public float X0;
    public float Y0;
    public float X1;
    public float Y1;
    public byte Kind;
}

internal static class CrowdDebugSegmentKind
{
    public const byte Nav = 0;
    public const byte Plan = 1;
    public const byte Tile = 2;
    public const byte TwoWay = 3;
    public const byte OneWay = 4;
    public const byte Leader = 5;
    public const byte Funnel = 6;
}

internal static class CrowdDebugOverlayGeometry
{
    public static int TemplateIndex(int layer, int radius, int radiusCount)
    {
        if (radiusCount <= 0) throw new ArgumentOutOfRangeException(nameof(radiusCount));
        if (layer < 0 || radius < 0 || radius >= radiusCount)
            throw new ArgumentOutOfRangeException(nameof(radius));
        return layer * radiusCount + radius;
    }

    public static int StepFor(int spanX, int spanY, int maxCells)
    {
        if (maxCells < 1) throw new ArgumentOutOfRangeException(nameof(maxCells));
        if (spanX <= 0 || spanY <= 0) return 1;
        long cells = (long)spanX * spanY;
        if (cells <= maxCells) return 1;
        int step = (int)Math.Ceiling(Math.Sqrt(cells / (double)maxCells));
        if (step < 1) step = 1;
        while ((long)((spanX + step - 1) / step) * ((spanY + step - 1) / step) > maxCells)
            step++;
        return step;
    }

    public static int UnionCorridorTiles(
        IReadOnlyList<CrowdNavGroupSet.Group?> groups,
        int player,
        int layer,
        int radius,
        byte[] union,
        int[] tiles,
        out int firstGroup)
    {
        Array.Clear(union);
        firstGroup = -1;
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group == null || group.Player != player || group.LayerIdx != layer || group.RIdx != radius || group.Flow == null)
                continue;
            if (firstGroup < 0) firstGroup = i;
            var mask = group.Flow.Mask;
            if (mask == null) continue;
            int len = mask.Length < union.Length ? mask.Length : union.Length;
            for (int t = 0; t < len; t++)
            {
                if (mask[t] != 0) union[t] = 1;
            }
        }

        int count = 0;
        for (int t = 0; t < union.Length; t++)
        {
            if (union[t] == 0) continue;
            if (count >= tiles.Length)
                throw new InvalidOperationException("CrowdSimulationDebugView.json: maxFieldCells 装不下走廊 tile。");
            tiles[count++] = t;
        }

        return count;
    }

    public static int CopyDirtyTiles(IReadOnlyList<int>? changed, int[] dest)
    {
        if (changed == null) return 0;
        if (changed.Count > dest.Length)
            throw new InvalidOperationException("CrowdSimulationDebugView.json: maxFieldCells 装不下脏 tile。");
        for (int i = 0; i < changed.Count; i++) dest[i] = changed[i];
        return changed.Count;
    }

    public static int AppendJumps(NavLinkSet? links, int navCellsPerSide, int cellCm, CrowdDebugSegment[] dest, int offset)
    {
        if (links == null) return 0;
        int written = 0;
        for (int e = 0; e < links.Count; e++)
        {
            if (offset + written >= dest.Length)
                throw new InvalidOperationException("CrowdSimulationDebugView.json: maxWorldSegments 装不下跳跃链接。");
            CellCenter(links.From[e], navCellsPerSide, cellCm, out float x0, out float y0);
            CellCenter(links.To[e], navCellsPerSide, cellCm, out float x1, out float y1);
            dest[offset + written++] = new CrowdDebugSegment
            {
                X0 = x0,
                Y0 = y0,
                X1 = x1,
                Y1 = y1,
                Kind = links.TwoWay[e] != 0 ? CrowdDebugSegmentKind.TwoWay : CrowdDebugSegmentKind.OneWay,
            };
        }

        return written;
    }

    public static int AppendHpaEdges(HpaGraph? hpa, int navCellsPerSide, int cellCm, CrowdDebugSegment[] dest, int offset)
    {
        if (hpa == null) return 0;
        int written = 0;
        var blocks = hpa.Blocks;
        for (int b = 0; b < blocks.Length; b++)
        {
            var block = blocks[b];
            int live = block.CellCount;
            for (int i = 0; i < live; i++)
            {
                int from = block.Cells[i];
                int end = block.AdjStart[i + 1];
                for (int k = block.AdjStart[i]; k < end; k++)
                {
                    int to = block.AdjTo[k];
                    if (from >= to) continue;
                    if (offset + written >= dest.Length)
                        throw new InvalidOperationException("CrowdSimulationDebugView.json: maxWorldSegments 装不下抽象图边。");
                    CellCenter(from, navCellsPerSide, cellCm, out float x0, out float y0);
                    CellCenter(to, navCellsPerSide, cellCm, out float x1, out float y1);
                    dest[offset + written++] = new CrowdDebugSegment
                    {
                        X0 = x0,
                        Y0 = y0,
                        X1 = x1,
                        Y1 = y1,
                        Kind = CrowdDebugSegmentKind.Plan,
                    };
                }
            }
        }

        return written;
    }

    public static int CopyHpaNodes(HpaGraph? hpa, int[] dest)
    {
        if (hpa == null) return 0;
        int written = 0;
        var blocks = hpa.Blocks;
        for (int b = 0; b < blocks.Length; b++)
        {
            var block = blocks[b];
            int live = block.CellCount;
            for (int i = 0; i < live; i++)
            {
                if (written >= dest.Length)
                    throw new InvalidOperationException("CrowdSimulationDebugView.json: maxWorldSegments 装不下抽象图节点。");
                dest[written++] = block.Cells[i];
            }
        }

        return written;
    }

    public static int AppendPolygons(NavContext nav, int navCellsPerSide, int navCellsPerTile, int cellCm, CrowdDebugSegment[] dest, int offset)
    {
        int tilesPerSide = navCellsPerSide / navCellsPerTile;
        int written = 0;
        var tiles = nav.Tiles;
        if (tiles != null)
        {
            for (int ty = 0; ty < tilesPerSide; ty++)
            {
                for (int tx = 0; tx < tilesPerSide; tx++)
                {
                    written += AppendEntry(tiles[ty * tilesPerSide + tx], tx * navCellsPerTile, ty * navCellsPerTile, cellCm, dest, offset + written);
                }
            }
        }

        if (nav.UpperTiles != null)
        {
            foreach (var pair in nav.UpperTiles)
            {
                int tile = pair.Key;
                int tx = tile % tilesPerSide;
                int ty = tile / tilesPerSide;
                written += AppendEntry(pair.Value.Entry, tx * navCellsPerTile, ty * navCellsPerTile, cellCm, dest, offset + written);
            }
        }

        return written;
    }

    public static int AppendFunnel(
        IReadOnlyList<CrowdNavGroupSet.Group?> groups,
        int player,
        CrowdLeader?[] seen,
        ref int seenCount,
        CrowdDebugSegment[] dest,
        int offset)
    {
        int written = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group == null || group.Player != player) continue;
            var leader = group.Leader;
            if (leader == null || leader.Done) continue;
            bool dup = false;
            for (int s = 0; s < seenCount; s++)
            {
                if (ReferenceEquals(seen[s], leader))
                {
                    dup = true;
                    break;
                }
            }

            if (dup) continue;
            if (seenCount >= seen.Length)
                throw new InvalidOperationException("CrowdSimulationDebugView.json: maxWorldSegments 装不下领队。");
            seen[seenCount++] = leader;
            int pathCount = leader.PathCount;
            if (pathCount < 2) continue;
            int start = leader.Seg;
            if (start < 1) start = 1;
            if (start >= pathCount) start = pathCount - 1;
            float x = leader.XCm.ToFloat();
            float y = leader.YCm.ToFloat();
            bool first = true;
            for (int p = start; p < pathCount; p++)
            {
                if (offset + written >= dest.Length)
                    throw new InvalidOperationException("CrowdSimulationDebugView.json: maxWorldSegments 装不下漏斗路径。");
                var point = leader.PathAt(p);
                float nx = point.X.ToFloat();
                float ny = point.Y.ToFloat();
                dest[offset + written++] = new CrowdDebugSegment
                {
                    X0 = x,
                    Y0 = y,
                    X1 = nx,
                    Y1 = ny,
                    Kind = first ? CrowdDebugSegmentKind.Leader : CrowdDebugSegmentKind.Funnel,
                };
                first = false;
                x = nx;
                y = ny;
            }
        }

        return written;
    }

    public static int CopyBlockedCells(byte[] blocked, int navCellsPerSide, int x0, int y0, int x1, int y1, int step, int[] dest)
    {
        if (step < 1) throw new ArgumentOutOfRangeException(nameof(step));
        int count = 0;
        for (int y = y0; y < y1; y += step)
        {
            int row = y * navCellsPerSide;
            for (int x = x0; x < x1; x += step)
            {
                if (blocked[row + x] == 0) continue;
                if (count >= dest.Length)
                    throw new InvalidOperationException("CrowdSimulationDebugView.json: maxFieldCells 装不下阻挡格。");
                dest[count++] = row + x;
            }
        }

        return count;
    }

    public static int AppendTileGrid(int navCellsPerSide, int navCellsPerTile, int cellCm, CrowdDebugSegment[] dest, int offset)
    {
        int tiles = navCellsPerSide / navCellsPerTile;
        int span = navCellsPerSide * cellCm;
        int written = 0;
        for (int b = 0; b <= tiles; b++)
        {
            float at = b * navCellsPerTile * cellCm;
            if (offset + written + 1 >= dest.Length)
                throw new InvalidOperationException("CrowdSimulationDebugView.json: maxWorldSegments 装不下 tile 网格。");
            dest[offset + written++] = new CrowdDebugSegment { X0 = at, Y0 = 0f, X1 = at, Y1 = span, Kind = CrowdDebugSegmentKind.Tile };
            dest[offset + written++] = new CrowdDebugSegment { X0 = 0f, Y0 = at, X1 = span, Y1 = at, Kind = CrowdDebugSegmentKind.Tile };
        }

        return written;
    }

    public static int AppendDirtyOutlines(IReadOnlyList<int>? tiles, int tilesPerSide, int navCellsPerTile, int cellCm, CrowdDebugSegment[] dest, int offset)
    {
        if (tiles == null) return 0;
        int step = navCellsPerTile * cellCm;
        int written = 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            int tile = tiles[i];
            float x = (tile % tilesPerSide) * step;
            float y = (tile / tilesPerSide) * step;
            if (offset + written + 3 >= dest.Length)
                throw new InvalidOperationException("CrowdSimulationDebugView.json: maxWorldSegments 装不下脏 tile。");
            dest[offset + written++] = new CrowdDebugSegment { X0 = x, Y0 = y, X1 = x + step, Y1 = y, Kind = CrowdDebugSegmentKind.Tile };
            dest[offset + written++] = new CrowdDebugSegment { X0 = x + step, Y0 = y, X1 = x + step, Y1 = y + step, Kind = CrowdDebugSegmentKind.Tile };
            dest[offset + written++] = new CrowdDebugSegment { X0 = x + step, Y0 = y + step, X1 = x, Y1 = y + step, Kind = CrowdDebugSegmentKind.Tile };
            dest[offset + written++] = new CrowdDebugSegment { X0 = x, Y0 = y + step, X1 = x, Y1 = y, Kind = CrowdDebugSegmentKind.Tile };
        }

        return written;
    }

    public static int ClipSegments(
        ReadOnlySpan<CrowdDebugSegment> source,
        float minX,
        float minY,
        float maxX,
        float maxY,
        Span<CrowdDebugSegment> dest)
    {
        int count = 0;
        for (int i = 0; i < source.Length; i++)
        {
            var segment = source[i];
            if (!Clip(ref segment.X0, ref segment.Y0, ref segment.X1, ref segment.Y1, minX, minY, maxX, maxY))
                continue;
            if (count >= dest.Length) break;
            dest[count++] = segment;
        }

        return count;
    }

    public static void CellCenter(int cell, int navCellsPerSide, int cellCm, out float x, out float y)
    {
        int n2 = navCellsPerSide * navCellsPerSide;
        int c = cell >= n2 ? cell - n2 : cell;
        x = (c % navCellsPerSide + 0.5f) * cellCm;
        y = (c / navCellsPerSide + 0.5f) * cellCm;
    }

    private static int AppendEntry(NavTileEntry? entry, int ox, int oy, int cellCm, CrowdDebugSegment[] dest, int offset)
    {
        if (entry == null) return 0;
        int written = 0;
        for (int p = 0; p < entry.Count; p++)
        {
            int start = entry.PolyStart[p];
            int end = entry.PolyStart[p + 1];
            int count = end - start;
            if (count < 2) continue;
            for (int k = 0; k < count; k++)
            {
                int v0 = entry.PolyVerts[start + k];
                int v1 = entry.PolyVerts[start + (k + 1) % count];
                if (offset + written >= dest.Length)
                    throw new InvalidOperationException("CrowdSimulationDebugView.json: maxWorldSegments 装不下导航多边形。");
                dest[offset + written++] = new CrowdDebugSegment
                {
                    X0 = (ox + entry.Vx[v0]) * cellCm,
                    Y0 = (oy + entry.Vy[v0]) * cellCm,
                    X1 = (ox + entry.Vx[v1]) * cellCm,
                    Y1 = (oy + entry.Vy[v1]) * cellCm,
                    Kind = CrowdDebugSegmentKind.Nav,
                };
            }
        }

        return written;
    }

    private static int OutCode(float x, float y, float minX, float minY, float maxX, float maxY)
    {
        int code = 0;
        if (x < minX) code |= 1;
        else if (x > maxX) code |= 2;
        if (y < minY) code |= 4;
        else if (y > maxY) code |= 8;
        return code;
    }

    private static bool Clip(ref float x0, ref float y0, ref float x1, ref float y1, float minX, float minY, float maxX, float maxY)
    {
        int c0 = OutCode(x0, y0, minX, minY, maxX, maxY);
        int c1 = OutCode(x1, y1, minX, minY, maxX, maxY);
        for (int guard = 0; guard < 8; guard++)
        {
            if ((c0 | c1) == 0) return true;
            if ((c0 & c1) != 0) return false;
            int outCode = c0 != 0 ? c0 : c1;
            float x, y;
            if ((outCode & 8) != 0)
            {
                x = x0 + (x1 - x0) * (maxY - y0) / (y1 - y0);
                y = maxY;
            }
            else if ((outCode & 4) != 0)
            {
                x = x0 + (x1 - x0) * (minY - y0) / (y1 - y0);
                y = minY;
            }
            else if ((outCode & 2) != 0)
            {
                y = y0 + (y1 - y0) * (maxX - x0) / (x1 - x0);
                x = maxX;
            }
            else
            {
                y = y0 + (y1 - y0) * (minX - x0) / (x1 - x0);
                x = minX;
            }

            if (outCode == c0)
            {
                x0 = x;
                y0 = y;
                c0 = OutCode(x0, y0, minX, minY, maxX, maxY);
            }
            else
            {
                x1 = x;
                y1 = y;
                c1 = OutCode(x1, y1, minX, minY, maxX, maxY);
            }
        }

        return (c0 | c1) == 0;
    }
}
