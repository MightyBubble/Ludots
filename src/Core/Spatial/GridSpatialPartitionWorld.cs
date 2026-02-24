using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public sealed class GridSpatialPartitionWorld : ISpatialPartitionWorld
    {
        private readonly int _cellSize;
        private readonly Dictionary<long, List<Entity>> _cells;

        public GridSpatialPartitionWorld(int cellSize = 16, int initialCellCapacity = 1024)
        {
            if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (initialCellCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCellCapacity));
            _cellSize = cellSize;
            _cells = new Dictionary<long, List<Entity>>(initialCellCapacity);
        }

        public void Clear()
        {
            foreach (var list in _cells.Values) list.Clear();
        }

        /// <summary>
        /// ISpatialPartitionWorld: add entity at a single cell.
        /// </summary>
        public void Add(Entity entity, int cellX, int cellY)
        {
            long key = GetKey(cellX, cellY);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<Entity>(4);
                _cells[key] = list;
            }
            list.Add(entity);
        }

        /// <summary>
        /// ISpatialPartitionWorld: remove entity from a single cell.
        /// </summary>
        public void Remove(Entity entity, int cellX, int cellY)
        {
            long key = GetKey(cellX, cellY);
            if (_cells.TryGetValue(key, out var list))
            {
                list.Remove(entity);
            }
        }

        /// <summary>
        /// Add entity to all internal cells covered by the given bounds.
        /// </summary>
        public void Add(Entity entity, in IntRect bounds)
        {
            int minX = MathUtil.FloorDiv(bounds.X, _cellSize);
            int minY = MathUtil.FloorDiv(bounds.Y, _cellSize);
            int maxX = MathUtil.FloorDiv(bounds.X + bounds.Width, _cellSize);
            int maxY = MathUtil.FloorDiv(bounds.Y + bounds.Height, _cellSize);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    long key = GetKey(x, y);
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<Entity>(4);
                        _cells[key] = list;
                    }
                    list.Add(entity);
                }
            }
        }

        public void Remove(Entity entity, in IntRect bounds)
        {
            int minX = MathUtil.FloorDiv(bounds.X, _cellSize);
            int minY = MathUtil.FloorDiv(bounds.Y, _cellSize);
            int maxX = MathUtil.FloorDiv(bounds.X + bounds.Width, _cellSize);
            int maxY = MathUtil.FloorDiv(bounds.Y + bounds.Height, _cellSize);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    long key = GetKey(x, y);
                    if (_cells.TryGetValue(key, out var list))
                    {
                        list.Remove(entity);
                    }
                }
            }
        }

        public int Query(in IntRect bounds, Span<Entity> buffer, out int dropped)
        {
            int count = 0;
            dropped = 0;

            int minX = MathUtil.FloorDiv(bounds.X, _cellSize);
            int minY = MathUtil.FloorDiv(bounds.Y, _cellSize);
            int maxX = MathUtil.FloorDiv(bounds.X + bounds.Width, _cellSize);
            int maxY = MathUtil.FloorDiv(bounds.Y + bounds.Height, _cellSize);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    long key = GetKey(x, y);
                    if (!_cells.TryGetValue(key, out var list)) continue;

                    for (int i = 0; i < list.Count; i++)
                    {
                        if (count < buffer.Length)
                        {
                            buffer[count++] = list[i];
                        }
                        else
                        {
                            dropped++;
                        }
                    }
                }
            }

            return count;
        }

        private static long GetKey(int cellX, int cellY) => ((long)cellX << 32) | (uint)cellY;
    }
}
