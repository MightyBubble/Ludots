using System;
using System.Collections.Generic;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Vision
{
    public sealed class FogGlobalFieldVisualProjector
    {
        private FogField[] _fields = Array.Empty<FogField>();
        private FogCellState[] _fogCells = Array.Empty<FogCellState>();
        private FogCell[] _dirtyCells = Array.Empty<FogCell>();
        private GlobalFieldVisualCell[] _visualCells = Array.Empty<GlobalFieldVisualCell>();
        private readonly IntRect[] _dirtyRects = new IntRect[1];
        private readonly Dictionary<GlobalFieldVisualId, IntRect> _boundsById = new();

        public int LastProjectedFieldCount { get; private set; }
        public int LastProjectedCellCount { get; private set; }
        public int LastProjectedDirtyRectCount { get; private set; }

        public void Project(FogFieldStore store, GlobalFieldVisualBuffer buffer)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            EnsureFieldCapacity(store.Count);
            int fieldCount = store.Count == 0 ? 0 : store.CopyFields(_fields.AsSpan(0, _fields.Length));
            LastProjectedFieldCount = 0;
            LastProjectedCellCount = 0;
            LastProjectedDirtyRectCount = 0;

            for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                FogField field = _fields[fieldIndex];
                int cellCapacity = field.NonDefaultCount;
                EnsureFogCellCapacity(cellCapacity);
                EnsureVisualCellCapacity(cellCapacity);
                int cellCount = field.CopyCells(_fogCells.AsSpan(0, cellCapacity));

                IntRect cellBounds = BuildVisualCells(_fogCells.AsSpan(0, cellCount), _visualCells);
                int dirtyRectCount = BuildDirtyRects(field);
                GlobalFieldVisualId id = new(
                    GlobalFieldVisualKind.Fog,
                    field.ScopeKeyId,
                    field.LayerId.Value,
                    surfaceKeyId: 0);
                IntRect bounds = ResolveStableBounds(
                    id,
                    cellBounds,
                    dirtyRectCount == 0 ? default : _dirtyRects[0],
                    dirtyRectCount > 0);
                if (!HasArea(bounds))
                {
                    continue;
                }

                var descriptor = new GlobalFieldVisualDescriptor(
                    id,
                    field.CellSizeCm,
                    WorldCmInt2.Zero,
                    bounds,
                    GlobalFieldVisualValueKind.Byte);

                buffer.Upsert(
                    descriptor,
                    _visualCells.AsSpan(0, cellCount),
                    _dirtyRects.AsSpan(0, dirtyRectCount));
                field.ClearDirty();

                LastProjectedFieldCount++;
                LastProjectedCellCount += cellCount;
                LastProjectedDirtyRectCount += dirtyRectCount;
            }
        }

        private IntRect ResolveStableBounds(
            GlobalFieldVisualId id,
            IntRect cellBounds,
            IntRect dirtyBounds,
            bool hasDirtyBounds)
        {
            bool hasBounds = _boundsById.TryGetValue(id, out IntRect bounds) && HasArea(bounds);
            if (HasArea(cellBounds))
            {
                bounds = hasBounds ? Union(bounds, cellBounds) : cellBounds;
                hasBounds = true;
            }

            if (hasDirtyBounds && HasArea(dirtyBounds))
            {
                bounds = hasBounds ? Union(bounds, dirtyBounds) : dirtyBounds;
                hasBounds = true;
            }

            if (hasBounds)
            {
                _boundsById[id] = bounds;
                return bounds;
            }

            return default;
        }

        private int BuildDirtyRects(FogField field)
        {
            if (field.DirtyCount <= 0)
            {
                return 0;
            }

            EnsureDirtyCellCapacity(field.DirtyCount);
            int dirtyCount = field.EnumerateDirtyCells(_dirtyCells.AsSpan(0, field.DirtyCount));
            if (dirtyCount <= 0)
            {
                return 0;
            }

            int minX = _dirtyCells[0].X;
            int maxX = minX;
            int minY = _dirtyCells[0].Y;
            int maxY = minY;
            for (int i = 1; i < dirtyCount; i++)
            {
                FogCell cell = _dirtyCells[i];
                if (cell.X < minX) minX = cell.X;
                if (cell.X > maxX) maxX = cell.X;
                if (cell.Y < minY) minY = cell.Y;
                if (cell.Y > maxY) maxY = cell.Y;
            }

            _dirtyRects[0] = new IntRect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
            return 1;
        }

        private static bool HasArea(IntRect rect) => rect.Width > 0 && rect.Height > 0;

        private static IntRect Union(IntRect left, IntRect right)
        {
            int x = Math.Min(left.Left, right.Left);
            int y = Math.Min(left.Top, right.Top);
            int rightEdge = Math.Max(left.Right, right.Right);
            int bottom = Math.Max(left.Bottom, right.Bottom);
            return new IntRect(x, y, rightEdge - x, bottom - y);
        }

        private static IntRect BuildVisualCells(
            ReadOnlySpan<FogCellState> source,
            GlobalFieldVisualCell[] destination)
        {
            if (source.IsEmpty)
            {
                return new IntRect(0, 0, 0, 0);
            }

            int minX = source[0].Cell.X;
            int maxX = minX;
            int minY = source[0].Cell.Y;
            int maxY = minY;

            for (int i = 0; i < source.Length; i++)
            {
                ref readonly FogCellState state = ref source[i];
                FogCell cell = state.Cell;
                destination[i] = new GlobalFieldVisualCell(
                    new FieldCell2D(cell.X, cell.Y),
                    (byte)state.Visibility);

                if (cell.X < minX) minX = cell.X;
                if (cell.X > maxX) maxX = cell.X;
                if (cell.Y < minY) minY = cell.Y;
                if (cell.Y > maxY) maxY = cell.Y;
            }

            return new IntRect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
        }

        private void EnsureFieldCapacity(int required)
        {
            if (required <= _fields.Length)
            {
                return;
            }

            Array.Resize(ref _fields, NextCapacity(_fields.Length, required));
        }

        private void EnsureFogCellCapacity(int required)
        {
            if (required <= _fogCells.Length)
            {
                return;
            }

            Array.Resize(ref _fogCells, NextCapacity(_fogCells.Length, required));
        }

        private void EnsureDirtyCellCapacity(int required)
        {
            if (required <= _dirtyCells.Length)
            {
                return;
            }

            Array.Resize(ref _dirtyCells, NextCapacity(_dirtyCells.Length, required));
        }

        private void EnsureVisualCellCapacity(int required)
        {
            if (required <= _visualCells.Length)
            {
                return;
            }

            Array.Resize(ref _visualCells, NextCapacity(_visualCells.Length, required));
        }

        private static int NextCapacity(int current, int required)
        {
            int next = Math.Max(4, current);
            while (next < required)
            {
                next *= 2;
            }

            return next;
        }
    }
}
