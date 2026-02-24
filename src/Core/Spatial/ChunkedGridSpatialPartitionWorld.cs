using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public sealed class ChunkedGridSpatialPartitionWorld : ISpatialPartitionWorld
    {
        private readonly int _chunkShift;
        private readonly int _chunkSize;
        private readonly int _chunkMask;
        private readonly Dictionary<long, Chunk> _chunks;

        public ChunkedGridSpatialPartitionWorld(int chunkSizeCells = 64, int initialChunkCapacity = 1024)
        {
            if (chunkSizeCells <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSizeCells));
            if ((chunkSizeCells & (chunkSizeCells - 1)) != 0) throw new ArgumentException("chunkSizeCells must be a power of two.", nameof(chunkSizeCells));
            if (initialChunkCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialChunkCapacity));

            _chunkSize = chunkSizeCells;
            _chunkMask = chunkSizeCells - 1;
            _chunkShift = BitOperations.TrailingZeroCount((uint)chunkSizeCells);
            _chunks = new Dictionary<long, Chunk>(initialChunkCapacity);
        }

        public void Clear()
        {
            foreach (var c in _chunks.Values) c.Clear();
        }

        public void Add(Entity entity, int cellX, int cellY)
        {
            long key = GetChunkKey(cellX >> _chunkShift, cellY >> _chunkShift);
            if (!_chunks.TryGetValue(key, out var chunk))
            {
                chunk = new Chunk(_chunkSize);
                _chunks[key] = chunk;
            }

            int lx = cellX & _chunkMask;
            int ly = cellY & _chunkMask;
            chunk.Add(entity, lx, ly);
        }

        public void Remove(Entity entity, int cellX, int cellY)
        {
            long key = GetChunkKey(cellX >> _chunkShift, cellY >> _chunkShift);
            if (!_chunks.TryGetValue(key, out var chunk)) return;
            int lx = cellX & _chunkMask;
            int ly = cellY & _chunkMask;
            chunk.Remove(entity, lx, ly);
        }

        public int Query(in IntRect cellRect, Span<Entity> buffer, out int dropped)
        {
            int count = 0;
            dropped = 0;

            int minCellX = cellRect.X;
            int minCellY = cellRect.Y;
            int maxCellX = cellRect.X + cellRect.Width;
            int maxCellY = cellRect.Y + cellRect.Height;

            int minChunkX = MathUtil.FloorDiv(minCellX, _chunkSize);
            int minChunkY = MathUtil.FloorDiv(minCellY, _chunkSize);
            int maxChunkX = MathUtil.FloorDiv(maxCellX, _chunkSize);
            int maxChunkY = MathUtil.FloorDiv(maxCellY, _chunkSize);

            for (int cy = minChunkY; cy <= maxChunkY; cy++)
            {
                for (int cx = minChunkX; cx <= maxChunkX; cx++)
                {
                    long key = GetChunkKey(cx, cy);
                    if (!_chunks.TryGetValue(key, out var chunk)) continue;

                    int chunkMinCellX = cx * _chunkSize;
                    int chunkMinCellY = cy * _chunkSize;

                    int startX = Math.Max(minCellX - chunkMinCellX, 0);
                    int startY = Math.Max(minCellY - chunkMinCellY, 0);
                    int endX = Math.Min(maxCellX - chunkMinCellX, _chunkSize - 1);
                    int endY = Math.Min(maxCellY - chunkMinCellY, _chunkSize - 1);

                    for (int y = startY; y <= endY; y++)
                    {
                        for (int x = startX; x <= endX; x++)
                        {
                            var list = chunk.GetCellList(x, y);
                            if (list == null) continue;
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (count < buffer.Length) buffer[count++] = list[i];
                                else dropped++;
                            }
                        }
                    }
                }
            }

            return count;
        }

        private static long GetChunkKey(int chunkX, int chunkY) => ((long)chunkX << 32) | (uint)chunkY;

        private sealed class Chunk
        {
            private readonly int _size;
            private readonly List<Entity>[] _cells;

            public Chunk(int size)
            {
                _size = size;
                _cells = new List<Entity>[size * size];
            }

            public void Clear()
            {
                for (int i = 0; i < _cells.Length; i++)
                {
                    _cells[i]?.Clear();
                }
            }

            public void Add(Entity entity, int localX, int localY)
            {
                int idx = (localY * _size) + localX;
                var list = _cells[idx];
                if (list == null)
                {
                    list = new List<Entity>(4);
                    _cells[idx] = list;
                }
                list.Add(entity);
            }

            public void Remove(Entity entity, int localX, int localY)
            {
                int idx = (localY * _size) + localX;
                _cells[idx]?.Remove(entity);
            }

            public List<Entity> GetCellList(int localX, int localY)
            {
                int idx = (localY * _size) + localX;
                return _cells[idx];
            }
        }
    }
}
