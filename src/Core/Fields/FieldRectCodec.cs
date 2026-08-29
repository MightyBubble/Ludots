using System;
using System.Collections.Generic;

namespace Ludots.Core.Fields
{
    /// <summary>
    /// Inclusive axis-aligned rect stroke for discrete-id field authoring and save.
    /// Region identity is carried as the catalog region id (1-based); key resolution is the caller's job.
    /// </summary>
    public readonly struct FieldCellRectStroke
    {
        public FieldCellRectStroke(int x0, int y0, int x1, int y1, int regionId)
        {
            if (x1 < x0 || y1 < y0)
            {
                throw new ArgumentException("Rect ends must not precede starts.");
            }

            if (regionId < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(regionId), "Region id must be >= 1.");
            }

            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            RegionId = regionId;
        }

        public int X0 { get; }
        public int Y0 { get; }
        public int X1 { get; }
        public int Y1 { get; }
        public int RegionId { get; }

        public long CellCount => (long)(X1 - X0 + 1) * (Y1 - Y0 + 1);

        public bool Contains(int x, int y) =>
            x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

        public bool Overlaps(in FieldCellRectStroke other) =>
            !(X1 < other.X0 || other.X1 < X0 || Y1 < other.Y0 || other.Y1 < Y0);
    }

    /// <summary>
    /// Coalesces sparse non-default cells into inclusive rect strokes (row RLE, then vertical merge).
    /// Used by authoring save and the fields save participant so dense provinces stay tiny on disk.
    /// </summary>
    public static class FieldRectCodec
    {
        public static List<FieldCellRectStroke> CoalesceFromField(ChunkedField2D<int> field)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            var runs = new List<(int Y, int X0, int X1, int RegionId)>();
            int cellCount = field.Grid.ChunkSizeCells * field.Grid.ChunkSizeCells;
            for (int chunkIndex = 0; chunkIndex < field.ChunkCount; chunkIndex++)
            {
                FieldChunk2D<int> chunk = field.GetChunkAt(chunkIndex);
                int size = field.Grid.ChunkSizeCells;
                for (int localY = 0; localY < size; localY++)
                {
                    int runStart = -1;
                    int runValue = 0;
                    for (int localX = 0; localX < size; localX++)
                    {
                        int local = (localY * size) + localX;
                        int value = chunk.Get(local);
                        if (value == 0)
                        {
                            FlushRun(runs, field, chunk, localY, runStart, localX - 1, runValue);
                            runStart = -1;
                            continue;
                        }

                        if (runStart < 0)
                        {
                            runStart = localX;
                            runValue = value;
                            continue;
                        }

                        if (value != runValue)
                        {
                            FlushRun(runs, field, chunk, localY, runStart, localX - 1, runValue);
                            runStart = localX;
                            runValue = value;
                        }
                    }

                    FlushRun(runs, field, chunk, localY, runStart, size - 1, runValue);
                }
            }

            return MergeVertical(MergeHorizontal(runs));
        }

        public static List<FieldCellRectStroke> CoalescePoints(
            IReadOnlyList<(int X, int Y, int RegionId)> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            var runs = new List<(int Y, int X0, int X1, int RegionId)>(points.Count);
            if (points.Count == 0)
            {
                return new List<FieldCellRectStroke>();
            }

            var ordered = new List<(int X, int Y, int RegionId)>(points.Count);
            ordered.AddRange(points);
            ordered.Sort(static (a, b) =>
            {
                int byRegion = a.RegionId.CompareTo(b.RegionId);
                if (byRegion != 0)
                {
                    return byRegion;
                }

                int byY = a.Y.CompareTo(b.Y);
                return byY != 0 ? byY : a.X.CompareTo(b.X);
            });

            int index = 0;
            while (index < ordered.Count)
            {
                int regionId = ordered[index].RegionId;
                int y = ordered[index].Y;
                int x0 = ordered[index].X;
                int x1 = x0;
                index++;
                while (index < ordered.Count &&
                       ordered[index].RegionId == regionId &&
                       ordered[index].Y == y &&
                       ordered[index].X == x1 + 1)
                {
                    x1 = ordered[index].X;
                    index++;
                }

                runs.Add((y, x0, x1, regionId));
            }

            return MergeVertical(MergeHorizontal(runs));
        }

        private static void FlushRun(
            List<(int Y, int X0, int X1, int RegionId)> runs,
            ChunkedField2D<int> field,
            FieldChunk2D<int> chunk,
            int localY,
            int localX0,
            int localX1,
            int regionId)
        {
            if (localX0 < 0 || localX1 < localX0 || regionId == 0)
            {
                return;
            }

            FieldCell2D start = field.Grid.CellFromChunkLocal(chunk.ChunkX, chunk.ChunkY, (localY * field.Grid.ChunkSizeCells) + localX0);
            FieldCell2D end = field.Grid.CellFromChunkLocal(chunk.ChunkX, chunk.ChunkY, (localY * field.Grid.ChunkSizeCells) + localX1);
            runs.Add((start.Y, start.X, end.X, regionId));
        }

        private static List<(int Y, int X0, int X1, int RegionId)> MergeHorizontal(
            List<(int Y, int X0, int X1, int RegionId)> runs)
        {
            if (runs.Count == 0)
            {
                return runs;
            }

            runs.Sort(static (a, b) =>
            {
                int byRegion = a.RegionId.CompareTo(b.RegionId);
                if (byRegion != 0)
                {
                    return byRegion;
                }

                int byY = a.Y.CompareTo(b.Y);
                return byY != 0 ? byY : a.X0.CompareTo(b.X0);
            });

            var merged = new List<(int Y, int X0, int X1, int RegionId)>(runs.Count);
            (int y, int x0, int x1, int regionId) current = runs[0];
            for (int i = 1; i < runs.Count; i++)
            {
                (int y, int x0, int x1, int regionId) next = runs[i];
                if (next.regionId == current.regionId &&
                    next.y == current.y &&
                    next.x0 == current.x1 + 1)
                {
                    current.x1 = next.x1;
                    continue;
                }

                merged.Add(current);
                current = next;
            }

            merged.Add(current);
            return merged;
        }

        private static List<FieldCellRectStroke> MergeVertical(
            List<(int Y, int X0, int X1, int RegionId)> runs)
        {
            if (runs.Count == 0)
            {
                return new List<FieldCellRectStroke>();
            }

            runs.Sort(static (a, b) =>
            {
                int byRegion = a.RegionId.CompareTo(b.RegionId);
                if (byRegion != 0)
                {
                    return byRegion;
                }

                int byX0 = a.X0.CompareTo(b.X0);
                if (byX0 != 0)
                {
                    return byX0;
                }

                int byX1 = a.X1.CompareTo(b.X1);
                return byX1 != 0 ? byX1 : a.Y.CompareTo(b.Y);
            });

            var rects = new List<FieldCellRectStroke>(runs.Count);
            int i = 0;
            while (i < runs.Count)
            {
                (int y, int x0, int x1, int regionId) = runs[i];
                int y1 = y;
                i++;
                while (i < runs.Count &&
                       runs[i].RegionId == regionId &&
                       runs[i].X0 == x0 &&
                       runs[i].X1 == x1 &&
                       runs[i].Y == y1 + 1)
                {
                    y1 = runs[i].Y;
                    i++;
                }

                rects.Add(new FieldCellRectStroke(x0, y, x1, y1, regionId));
            }

            return rects;
        }
    }
}
