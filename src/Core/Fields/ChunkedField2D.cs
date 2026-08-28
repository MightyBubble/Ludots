using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Fields
{
    public sealed class ChunkedField2D<T>
        where T : struct
    {
        /// <summary>
        /// Chunks are indexed by a two-level page table instead of a hash map:
        /// on dense boards (hundreds of thousands of materialized chunks) hash-bucket
        /// lookups degrade into µs-class pointer chasing, while the second level stays
        /// one bounded array read. Page slots hold chunk indices, -1 marks empty.
        /// </summary>
        private const int PageShift = 5;
        private const int PageSide = 1 << PageShift;
        private const int PageMask = PageSide - 1;

        private readonly T _defaultValue;
        private readonly IFieldValueCodec<T> _codec;
        private readonly Dictionary<long, int[]> _pages;
        private FieldChunk2D<T>[] _chunks;
        private int _chunkCount;
        private int _dirtyCount;
        private int _nonDefaultCount;
        private long _changeStamp;

        public ChunkedField2D(FieldGridSpec2D grid, T defaultValue = default, int initialChunkCapacity = 8)
            : this(grid, FieldValueCodec<T>.Instance, defaultValue, initialChunkCapacity)
        {
        }

        public ChunkedField2D(
            FieldGridSpec2D grid,
            IFieldValueCodec<T> codec,
            T defaultValue = default,
            int initialChunkCapacity = 8)
        {
            if (codec is null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            if (initialChunkCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialChunkCapacity));
            }

            Grid = grid;
            _defaultValue = defaultValue;
            _codec = codec;
            _chunks = new FieldChunk2D<T>[initialChunkCapacity];
            _pages = new Dictionary<long, int[]>();
        }

        public FieldGridSpec2D Grid { get; }
        public int ChannelCount => _codec.ChannelCount;
        public FieldChannelKind ChannelKind => _codec.ChannelKind;
        public int ChunkCount => _chunkCount;
        public int DirtyCount => _dirtyCount;
        public int NonDefaultCount => _nonDefaultCount;
        public long ChangeStamp => _changeStamp;

        public FieldDirtyCursor<T> OpenDirtyCursor() => new(this);

        public FieldCell2D WorldToCell(WorldCmInt2 world) => Grid.WorldToCell(world);
        public WorldCmInt2 CellCenterToWorld(FieldCell2D cell) => Grid.CellCenterToWorld(cell);

        public T Get(FieldCell2D cell)
        {
            int chunkX = Grid.ChunkCoord(cell.X);
            int chunkY = Grid.ChunkCoord(cell.Y);
            if (!TryGetChunkIndex(chunkX, chunkY, out int chunkIndex))
            {
                return _defaultValue;
            }

            int local = Grid.LocalIndex(cell.X, cell.Y);
            return _chunks[chunkIndex].Get(local);
        }

        public bool TryGet(FieldCell2D cell, out T value)
        {
            int chunkX = Grid.ChunkCoord(cell.X);
            int chunkY = Grid.ChunkCoord(cell.Y);
            if (!TryGetChunkIndex(chunkX, chunkY, out int chunkIndex))
            {
                value = _defaultValue;
                return false;
            }

            int local = Grid.LocalIndex(cell.X, cell.Y);
            value = _chunks[chunkIndex].Get(local);
            return true;
        }

        public bool Set(FieldCell2D cell, T value)
        {
            FieldChunk2D<T> chunk = GetOrCreateChunk(Grid.ChunkCoord(cell.X), Grid.ChunkCoord(cell.Y));
            int local = Grid.LocalIndex(cell.X, cell.Y);
            return WriteLocal(chunk, local, value);
        }

        /// <summary>
        /// Inclusive fill. Writes every cell in the rect; callers that already know the area is empty
        /// still pay per-cell equality checks so NonDefaultCount stays exact.
        /// </summary>
        public int FillRect(int x0, int y0, int x1, int y1, T value)
        {
            if (x1 < x0 || y1 < y0)
            {
                throw new ArgumentException("FillRect ends must not precede starts.");
            }

            int changed = 0;
            int chunkMinX = Grid.ChunkCoord(x0);
            int chunkMaxX = Grid.ChunkCoord(x1);
            int chunkMinY = Grid.ChunkCoord(y0);
            int chunkMaxY = Grid.ChunkCoord(y1);
            int size = Grid.ChunkSizeCells;
            for (int chunkY = chunkMinY; chunkY <= chunkMaxY; chunkY++)
            {
                for (int chunkX = chunkMinX; chunkX <= chunkMaxX; chunkX++)
                {
                    FieldChunk2D<T> chunk = GetOrCreateChunk(chunkX, chunkY);
                    int worldX0 = chunkX * size;
                    int worldY0 = chunkY * size;
                    int localX0 = Math.Max(0, x0 - worldX0);
                    int localY0 = Math.Max(0, y0 - worldY0);
                    int localX1 = Math.Min(size - 1, x1 - worldX0);
                    int localY1 = Math.Min(size - 1, y1 - worldY0);
                    for (int localY = localY0; localY <= localY1; localY++)
                    {
                        int row = localY * size;
                        for (int localX = localX0; localX <= localX1; localX++)
                        {
                            if (WriteLocal(chunk, row + localX, value))
                            {
                                changed++;
                            }
                        }
                    }
                }
            }

            return changed;
        }

        private bool WriteLocal(FieldChunk2D<T> chunk, int local, T value)
        {
            T current = chunk.Get(local);
            if (_codec.ValueEquals(current, value))
            {
                return false;
            }

            bool wasDefault = _codec.ValueEquals(current, _defaultValue);
            bool isDefault = _codec.ValueEquals(value, _defaultValue);
            if (wasDefault && !isDefault)
            {
                _nonDefaultCount++;
            }
            else if (!wasDefault && isDefault)
            {
                _nonDefaultCount--;
            }

            chunk.Set(local, value);
            MarkDirtyInChunk(chunk, local);
            MarkChanged(chunk);
            return true;
        }

        public void MarkDirty(FieldCell2D cell)
        {
            FieldChunk2D<T> chunk = GetOrCreateChunk(Grid.ChunkCoord(cell.X), Grid.ChunkCoord(cell.Y));
            MarkDirtyInChunk(chunk, Grid.LocalIndex(cell.X, cell.Y));
            MarkChanged(chunk);
        }

        public void MarkDirtyRegion(IntRect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            int endY = rect.Y + rect.Height;
            int endX = rect.X + rect.Width;
            for (int y = rect.Y; y < endY; y++)
            {
                for (int x = rect.X; x < endX; x++)
                {
                    MarkDirty(new FieldCell2D(x, y));
                }
            }
        }

        public int ReplaceValue(T from, T to)
        {
            if (_codec.ValueEquals(from, to))
            {
                return 0;
            }

            int replaced = 0;
            int cellCount = Grid.ChunkSizeCells * Grid.ChunkSizeCells;
            for (int chunkIndex = 0; chunkIndex < _chunkCount; chunkIndex++)
            {
                FieldChunk2D<T> chunk = _chunks[chunkIndex];
                for (int local = 0; local < cellCount; local++)
                {
                    T current = chunk.Get(local);
                    if (!_codec.ValueEquals(current, from))
                    {
                        continue;
                    }

                    bool wasDefault = _codec.ValueEquals(current, _defaultValue);
                    bool isDefault = _codec.ValueEquals(to, _defaultValue);
                    if (wasDefault && !isDefault)
                    {
                        _nonDefaultCount++;
                    }
                    else if (!wasDefault && isDefault)
                    {
                        _nonDefaultCount--;
                    }

                    chunk.Set(local, to);
                    MarkDirtyInChunk(chunk, local);
                    MarkChanged(chunk);
                    replaced++;
                }
            }

            return replaced;
        }

        public int EnumerateDirtyCells(Span<FieldCell2D> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            for (int chunkIndex = 0; chunkIndex < _chunkCount && written < destination.Length; chunkIndex++)
            {
                FieldChunk2D<T> chunk = _chunks[chunkIndex];
                for (int i = 0; i < chunk.DirtyCount && written < destination.Length; i++)
                {
                    destination[written++] = Grid.CellFromChunkLocal(chunk.ChunkX, chunk.ChunkY, chunk.DirtyLocals[i]);
                }
            }

            return written;
        }

        public int CopyNonDefaultCells(Span<FieldCellValue2D<T>> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            int cellCount = Grid.ChunkSizeCells * Grid.ChunkSizeCells;
            for (int chunkIndex = 0; chunkIndex < _chunkCount && written < destination.Length; chunkIndex++)
            {
                FieldChunk2D<T> chunk = _chunks[chunkIndex];
                for (int local = 0; local < cellCount && written < destination.Length; local++)
                {
                    T value = chunk.Get(local);
                    if (_codec.ValueEquals(value, _defaultValue))
                    {
                        continue;
                    }

                    destination[written++] = new FieldCellValue2D<T>(
                        Grid.CellFromChunkLocal(chunk.ChunkX, chunk.ChunkY, local),
                        value);
                }
            }

            return written;
        }

        /// <summary>Monotonic stamp of the last value change under this cell; 0 = never written.</summary>
        public long GetChangeStamp(FieldCell2D cell)
        {
            if (!TryGetChunkIndex(Grid.ChunkCoord(cell.X), Grid.ChunkCoord(cell.Y), out int chunkIndex))
            {
                return 0;
            }

            return _chunks[chunkIndex].ChangeStamp;
        }

        public FieldChunk2D<T> GetChunkAt(int index)
        {
            if ((uint)index >= (uint)_chunkCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _chunks[index];
        }

        public void ClearDirty()
        {
            for (int chunkIndex = 0; chunkIndex < _chunkCount; chunkIndex++)
            {
                _chunks[chunkIndex].ClearDirty();
            }

            _dirtyCount = 0;
        }

        public void Clear()
        {
            _pages.Clear();
            _chunkCount = 0;
            _dirtyCount = 0;
            _nonDefaultCount = 0;
        }

        private static long PageKey(int chunkX, int chunkY) =>
            ((long)(chunkX >> PageShift) << 32) ^ (uint)(chunkY >> PageShift);

        private static int PageSlot(int chunkX, int chunkY) =>
            ((chunkY & PageMask) << PageShift) | (chunkX & PageMask);

        private bool TryGetChunkIndex(int chunkX, int chunkY, out int chunkIndex)
        {
            if (!_pages.TryGetValue(PageKey(chunkX, chunkY), out int[]? page))
            {
                chunkIndex = -1;
                return false;
            }

            chunkIndex = page[PageSlot(chunkX, chunkY)];
            return chunkIndex >= 0;
        }

        private FieldChunk2D<T> GetOrCreateChunk(int chunkX, int chunkY)
        {
            if (!_pages.TryGetValue(PageKey(chunkX, chunkY), out int[]? page))
            {
                page = new int[PageSide * PageSide];
                Array.Fill(page, -1);
                _pages.Add(PageKey(chunkX, chunkY), page);
            }

            int slot = PageSlot(chunkX, chunkY);
            int existing = page![slot];
            if (existing >= 0)
            {
                return _chunks[existing];
            }

            EnsureChunkCapacity(_chunkCount + 1);
            int index = _chunkCount++;
            FieldChunk2D<T> chunk = new(chunkX, chunkY, Grid.ChunkSizeCells, _defaultValue, _codec);
            _chunks[index] = chunk;
            page[slot] = index;
            return chunk;
        }

        private void EnsureChunkCapacity(int required)
        {
            if (required <= _chunks.Length)
            {
                return;
            }

            int next = _chunks.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _chunks, next);
        }

        private void MarkDirtyInChunk(FieldChunk2D<T> chunk, int localIndex)
        {
            if (chunk.TryMarkDirty(localIndex))
            {
                _dirtyCount++;
            }
        }

        private void MarkChanged(FieldChunk2D<T> chunk)
        {
            _changeStamp++;
            chunk.MarkChanged(_changeStamp);
        }
    }
}
