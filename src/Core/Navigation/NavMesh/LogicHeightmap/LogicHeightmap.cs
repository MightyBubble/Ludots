using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh.LogicHeightmap
{
    public sealed class LogicHeightmap
    {
        private readonly Dictionary<long, LogicHeightmapChunk> _chunks = new();

        public int WidthInChunks { get; private set; }

        public int HeightInChunks { get; private set; }

        public LogicHeightmapGridKind GridKind { get; private set; }

        public int CellSizeXCm { get; private set; }

        public int CellSizeZCm { get; private set; }

        public void Initialize(
            int widthInChunks,
            int heightInChunks,
            LogicHeightmapGridKind gridKind,
            int cellSizeXCm,
            int cellSizeZCm)
        {
            if (widthInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(widthInChunks));
            if (heightInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(heightInChunks));
            if (cellSizeXCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeXCm));
            if (cellSizeZCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeZCm));

            WidthInChunks = widthInChunks;
            HeightInChunks = heightInChunks;
            GridKind = gridKind;
            CellSizeXCm = cellSizeXCm;
            CellSizeZCm = cellSizeZCm;
            _chunks.Clear();
        }

        public int WidthSamples => WidthInChunks * LogicHeightmapChunk.ChunkSize;

        public int HeightSamples => HeightInChunks * LogicHeightmapChunk.ChunkSize;

        public int ChunkCount => _chunks.Count;

        public IEnumerable<(int ChunkX, int ChunkY, LogicHeightmapChunk Chunk)> Chunks
        {
            get
            {
                foreach (var kv in _chunks)
                {
                    int cx = (int)(kv.Key >> 32);
                    int cy = (int)kv.Key;
                    yield return (cx, cy, kv.Value);
                }
            }
        }

        public bool IsValidChunk(int chunkX, int chunkY)
        {
            return chunkX >= 0 && chunkX < WidthInChunks && chunkY >= 0 && chunkY < HeightInChunks;
        }

        public LogicHeightmapChunk? GetChunk(int sampleX, int sampleY, bool createIfMissing = false)
        {
            int chunkX = sampleX >> LogicHeightmapChunk.ChunkSizeShift;
            int chunkY = sampleY >> LogicHeightmapChunk.ChunkSizeShift;
            if (!IsValidChunk(chunkX, chunkY)) return null;

            long key = GetChunkKey(chunkX, chunkY);
            if (_chunks.TryGetValue(key, out var chunk))
            {
                return chunk;
            }

            if (!createIfMissing) return null;

            chunk = new LogicHeightmapChunk();
            _chunks[key] = chunk;
            return chunk;
        }

        public int GetHeightCm(int sampleX, int sampleY)
        {
            var chunk = GetChunk(sampleX, sampleY);
            if (chunk == null) return 0;
            return chunk.GetHeightCm(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask);
        }

        public void SetHeightCm(int sampleX, int sampleY, int heightCm)
        {
            var chunk = GetChunk(sampleX, sampleY, createIfMissing: true) ?? throw new ArgumentOutOfRangeException();
            chunk.SetHeightCm(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask, heightCm);
        }

        public int GetWaterHeightCm(int sampleX, int sampleY)
        {
            var chunk = GetChunk(sampleX, sampleY);
            if (chunk == null) return 0;
            return chunk.GetWaterHeightCm(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask);
        }

        public void SetWaterHeightCm(int sampleX, int sampleY, int heightCm)
        {
            var chunk = GetChunk(sampleX, sampleY, createIfMissing: true) ?? throw new ArgumentOutOfRangeException();
            chunk.SetWaterHeightCm(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask, heightCm);
        }

        public byte GetAreaId(int sampleX, int sampleY)
        {
            var chunk = GetChunk(sampleX, sampleY);
            if (chunk == null) return 0;
            return chunk.GetAreaId(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask);
        }

        public void SetAreaId(int sampleX, int sampleY, byte areaId)
        {
            var chunk = GetChunk(sampleX, sampleY, createIfMissing: true) ?? throw new ArgumentOutOfRangeException();
            chunk.SetAreaId(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask, areaId);
        }

        public bool IsBlocked(int sampleX, int sampleY)
        {
            var chunk = GetChunk(sampleX, sampleY);
            if (chunk == null) return false;
            return chunk.IsBlocked(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask);
        }

        public void SetBlocked(int sampleX, int sampleY, bool blocked)
        {
            var chunk = GetChunk(sampleX, sampleY, createIfMissing: true) ?? throw new ArgumentOutOfRangeException();
            chunk.SetBlocked(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask, blocked);
        }

        public bool IsRamp(int sampleX, int sampleY)
        {
            var chunk = GetChunk(sampleX, sampleY);
            if (chunk == null) return false;
            return chunk.IsRamp(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask);
        }

        public void SetRamp(int sampleX, int sampleY, bool isRamp)
        {
            var chunk = GetChunk(sampleX, sampleY, createIfMissing: true) ?? throw new ArgumentOutOfRangeException();
            chunk.SetRamp(sampleX & LogicHeightmapChunk.ChunkSizeMask, sampleY & LogicHeightmapChunk.ChunkSizeMask, isRamp);
        }

        internal void SetChunk(int chunkX, int chunkY, LogicHeightmapChunk chunk)
        {
            if (!IsValidChunk(chunkX, chunkY)) throw new ArgumentOutOfRangeException();
            _chunks[GetChunkKey(chunkX, chunkY)] = chunk ?? throw new ArgumentNullException(nameof(chunk));
        }

        private static long GetChunkKey(int chunkX, int chunkY)
        {
            return ((long)chunkX << 32) | (uint)chunkY;
        }
    }
}
