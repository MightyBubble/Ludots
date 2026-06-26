using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Fields
{
    public sealed class ChunkedField2D<T>
        where T : struct
    {
        private readonly T _defaultValue;
        private readonly IFieldValueCodec<T> _codec;
        private readonly Dictionary<long, int> _chunkIndexByKey;
        private FieldChunk2D<T>[] _chunks;
        private int _chunkCount;
        private int _dirtyCount;
        private int _nonDefaultCount;

        public ChunkedField2D(FieldGridSpec2D grid, T defaultValue = default, int initialChunkCapacity = 8)
        {
            if (initialChunkCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialChunkCapacity));
            }

            Grid = grid;
            _defaultValue = defaultValue;
            _codec = FieldValueCodec<T>.Instance;
            _chunks = new FieldChunk2D<T>[initialChunkCapacity];
            _chunkIndexByKey = new Dictionary<long, int>(initialChunkCapacity);
        }

        public FieldGridSpec2D Grid { get; }
        public int ChannelCount => _codec.ChannelCount;
        public FieldChannelKind ChannelKind => _codec.ChannelKind;
        public int ChunkCount => _chunkCount;
        public int DirtyCount => _dirtyCount;
        public int NonDefaultCount => _nonDefaultCount;

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
            return true;
        }

        public void MarkDirty(FieldCell2D cell)
        {
            FieldChunk2D<T> chunk = GetOrCreateChunk(Grid.ChunkCoord(cell.X), Grid.ChunkCoord(cell.Y));
            MarkDirtyInChunk(chunk, Grid.LocalIndex(cell.X, cell.Y));
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
            _chunkIndexByKey.Clear();
            _chunkCount = 0;
            _dirtyCount = 0;
            _nonDefaultCount = 0;
        }

        private bool TryGetChunkIndex(int chunkX, int chunkY, out int chunkIndex)
        {
            return _chunkIndexByKey.TryGetValue(FieldGridSpec2D.PackChunkKey(chunkX, chunkY), out chunkIndex);
        }

        private FieldChunk2D<T> GetOrCreateChunk(int chunkX, int chunkY)
        {
            long key = FieldGridSpec2D.PackChunkKey(chunkX, chunkY);
            if (_chunkIndexByKey.TryGetValue(key, out int existing))
            {
                return _chunks[existing];
            }

            EnsureChunkCapacity(_chunkCount + 1);
            int index = _chunkCount++;
            FieldChunk2D<T> chunk = new(chunkX, chunkY, Grid.ChunkSizeCells, _defaultValue, _codec);
            _chunks[index] = chunk;
            _chunkIndexByKey.Add(key, index);
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
    }
}
