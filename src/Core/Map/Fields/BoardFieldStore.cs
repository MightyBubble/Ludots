using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Fields
{
    public sealed class BoardFieldStore<T> : IBoardField<T> where T : struct
    {
        private readonly Dictionary<long, BoardFieldChunk<T>> _chunks;
        private readonly IBoardFieldChunkCodec<T> _codec;
        private ILoadedChunks? _loadedChunks;

        public BoardFieldStore(
            int widthCells,
            int heightCells,
            int cellSizeCm,
            T defaultValue,
            IBoardFieldChunkCodec<T> codec,
            int chunkSizeCells = SpatialScaleDefaults.TerrainChunkCells)
        {
            if (widthCells <= 0) throw new ArgumentOutOfRangeException(nameof(widthCells));
            if (heightCells <= 0) throw new ArgumentOutOfRangeException(nameof(heightCells));
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            if (chunkSizeCells <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSizeCells));

            WidthCells = widthCells;
            HeightCells = heightCells;
            CellSizeCm = cellSizeCm;
            ChunkSizeCells = chunkSizeCells;
            DefaultValue = defaultValue;
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _chunks = new Dictionary<long, BoardFieldChunk<T>>();
        }

        public int WidthCells { get; }

        public int HeightCells { get; }

        public int CellSizeCm { get; }

        public int ChunkSizeCells { get; }

        public int WidthChunks => (WidthCells + ChunkSizeCells - 1) / ChunkSizeCells;

        public int HeightChunks => (HeightCells + ChunkSizeCells - 1) / ChunkSizeCells;

        public T DefaultValue { get; }

        public int ResidentChunkCount => _chunks.Count;

        public T GetCell(int col, int row)
        {
            ThrowIfOutOfBounds(col, row);
            int chunkX = col / ChunkSizeCells;
            int chunkY = row / ChunkSizeCells;
            if (!_chunks.TryGetValue(ChunkKey(chunkX, chunkY), out BoardFieldChunk<T>? chunk))
            {
                return DefaultValue;
            }

            return chunk.GetCell(GetLocalIndex(col, row, chunkX, chunkY));
        }

        public bool TryGetCell(int col, int row, out T value)
        {
            if (!IsInBounds(col, row))
            {
                value = default;
                return false;
            }

            int chunkX = col / ChunkSizeCells;
            int chunkY = row / ChunkSizeCells;
            if (!_chunks.TryGetValue(ChunkKey(chunkX, chunkY), out BoardFieldChunk<T>? chunk))
            {
                value = DefaultValue;
                return true;
            }

            value = chunk.GetCell(GetLocalIndex(col, row, chunkX, chunkY));
            return true;
        }

        public void SetCell(int col, int row, T value)
        {
            ThrowIfOutOfBounds(col, row);
            int chunkX = col / ChunkSizeCells;
            int chunkY = row / ChunkSizeCells;
            long key = ChunkKey(chunkX, chunkY);
            if (!_chunks.TryGetValue(key, out BoardFieldChunk<T>? chunk))
            {
                if (EqualityComparer<T>.Default.Equals(value, DefaultValue))
                {
                    return;
                }

                chunk = CreateChunk();
                _chunks.Add(key, chunk);
            }

            chunk.SetCell(GetLocalIndex(col, row, chunkX, chunkY), value);
            chunk.Dirty = true;
        }

        public T SampleWorldCm(float worldXCm, float worldYCm)
        {
            WorldToCell(worldXCm, worldYCm, out int col, out int row);
            return GetCell(col, row);
        }

        public bool TrySampleWorldCm(float worldXCm, float worldYCm, out T value)
        {
            WorldToCell(worldXCm, worldYCm, out int col, out int row);
            return TryGetCell(col, row, out value);
        }

        public bool IsChunkResident(int chunkX, int chunkY)
        {
            if (!IsChunkInBounds(chunkX, chunkY)) return false;
            return _chunks.ContainsKey(ChunkKey(chunkX, chunkY));
        }

        public bool IsChunkDirty(int chunkX, int chunkY)
        {
            if (!IsChunkInBounds(chunkX, chunkY)) return false;
            return _chunks.TryGetValue(ChunkKey(chunkX, chunkY), out BoardFieldChunk<T>? chunk) && chunk.Dirty;
        }

        public void ClearChunkDirty(int chunkX, int chunkY)
        {
            if (!IsChunkInBounds(chunkX, chunkY)) throw new ArgumentOutOfRangeException();
            if (_chunks.TryGetValue(ChunkKey(chunkX, chunkY), out BoardFieldChunk<T>? chunk))
            {
                chunk.Dirty = false;
            }
        }

        public void ClearDirty()
        {
            foreach (KeyValuePair<long, BoardFieldChunk<T>> pair in _chunks)
            {
                pair.Value.Dirty = false;
            }
        }

        public bool RemoveChunk(int chunkX, int chunkY)
        {
            if (!IsChunkInBounds(chunkX, chunkY)) return false;
            return _chunks.Remove(ChunkKey(chunkX, chunkY));
        }

        public void SubscribeToLoadedChunks(ILoadedChunks source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            UnsubscribeFromLoadedChunks();
            _loadedChunks = source;
            source.ChunkUnloaded += OnChunkUnloaded;
        }

        public void UnsubscribeFromLoadedChunks()
        {
            if (_loadedChunks == null) return;
            _loadedChunks.ChunkUnloaded -= OnChunkUnloaded;
            _loadedChunks = null;
        }

        public static long ChunkKey(int chunkX, int chunkY)
            => ((long)chunkY << 32) | (uint)chunkX;

        private BoardFieldChunk<T> GetOrCreateChunk(int chunkX, int chunkY)
        {
            long key = ChunkKey(chunkX, chunkY);
            if (_chunks.TryGetValue(key, out BoardFieldChunk<T>? chunk))
            {
                return chunk;
            }

            chunk = CreateChunk();
            _chunks.Add(key, chunk);
            return chunk;
        }

        private BoardFieldChunk<T> CreateChunk()
        {
            int cellCount = checked(ChunkSizeCells * ChunkSizeCells);
            BoardFieldChunk<T> chunk = _codec.CreateChunk(cellCount, DefaultValue);
            if (chunk.CellCount != cellCount)
            {
                throw new InvalidOperationException(
                    $"Board field codec created a chunk with {chunk.CellCount} cells; expected {cellCount}.");
            }

            return chunk;
        }

        private bool IsInBounds(int col, int row)
            => (uint)col < (uint)WidthCells && (uint)row < (uint)HeightCells;

        private bool IsChunkInBounds(int chunkX, int chunkY)
            => (uint)chunkX < (uint)WidthChunks && (uint)chunkY < (uint)HeightChunks;

        private void ThrowIfOutOfBounds(int col, int row)
        {
            if (!IsInBounds(col, row))
            {
                throw new ArgumentOutOfRangeException($"Cell ({col},{row}) is outside board field bounds {WidthCells}x{HeightCells}.");
            }
        }

        private int GetLocalIndex(int col, int row, int chunkX, int chunkY)
        {
            int localX = col - chunkX * ChunkSizeCells;
            int localY = row - chunkY * ChunkSizeCells;
            return localY * ChunkSizeCells + localX;
        }

        private void WorldToCell(float worldXCm, float worldYCm, out int col, out int row)
        {
            col = (int)MathF.Floor(worldXCm / CellSizeCm);
            row = (int)MathF.Floor(worldYCm / CellSizeCm);
        }

        private void OnChunkUnloaded(long chunkKey)
        {
            _chunks.Remove(chunkKey);
        }
    }
}
