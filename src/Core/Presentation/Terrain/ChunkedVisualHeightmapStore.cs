using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Spatial;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Sparse loaded chunk store for visual terrain height data.
    /// Can subscribe to <see cref="ILoadedChunks"/> to release chunk payloads when streaming unloads them.
    /// </summary>
    public sealed class ChunkedVisualHeightmapStore
    {
        private readonly Dictionary<long, ChunkedVisualHeightmapChunk> _chunks = new Dictionary<long, ChunkedVisualHeightmapChunk>();
        private ILoadedChunks? _loadedChunks;
        [ThreadStatic] private static ThreadCache? s_threadCache;

        public ChunkedVisualHeightmapStore(ChunkedVisualHeightmapDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public ChunkedVisualHeightmapDescriptor Descriptor { get; }

        public int Revision { get; private set; }

        public int LoadedChunkCount => _chunks.Count;

        public int MaxMipLevel { get; private set; }

        public void SubscribeToLoadedChunks(ILoadedChunks source)
        {
            UnsubscribeFromLoadedChunks();
            _loadedChunks = source ?? throw new ArgumentNullException(nameof(source));
            _loadedChunks.ChunkUnloaded += OnChunkUnloaded;
        }

        public void UnsubscribeFromLoadedChunks()
        {
            if (_loadedChunks == null)
            {
                return;
            }

            _loadedChunks.ChunkUnloaded -= OnChunkUnloaded;
            _loadedChunks = null;
        }

        public void SetChunk(ChunkedVisualHeightmapChunk chunk)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
            ValidateChunkCoordinates(chunk.ChunkX, chunk.ChunkY);
            if (chunk.SampleCount != Descriptor.SamplesPerChunk)
            {
                throw new ArgumentException("Chunk sample payload does not match descriptor layout.", nameof(chunk));
            }

            bool expectsRaw = Descriptor.StorageLayout == VisualHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled;
            if (chunk.UsesRawUInt16Samples != expectsRaw)
            {
                throw new ArgumentException("Chunk sample payload does not match the descriptor storage encoding.", nameof(chunk));
            }

            ValidateChunkMips(chunk, expectsRaw);

            long key = GraphChunkKey.Pack(chunk.ChunkX, chunk.ChunkY);
            bool replacesCurrentMax =
                _chunks.TryGetValue(key, out ChunkedVisualHeightmapChunk? previous) &&
                previous.MipLevels.Length == MaxMipLevel &&
                chunk.MipLevels.Length < MaxMipLevel;
            _chunks[key] = chunk;
            if (replacesCurrentMax)
            {
                RecomputeMaxMipLevel();
            }
            else if (chunk.MipLevels.Length > MaxMipLevel)
            {
                MaxMipLevel = chunk.MipLevels.Length;
            }

            Revision++;
            ThreadCache cache = GetThreadCache();
            if (ReferenceEquals(cache.Owner, this) && cache.Key == key)
            {
                cache.Chunk = chunk;
            }
        }

        public bool RemoveChunk(int chunkX, int chunkY)
        {
            ValidateChunkCoordinates(chunkX, chunkY);
            long key = GraphChunkKey.Pack(chunkX, chunkY);
            bool removed = _chunks.Remove(key);
            if (removed)
            {
                Revision++;
                InvalidateThreadCache(key);
                RecomputeMaxMipLevel();
            }

            return removed;
        }

        public bool TryGetChunk(int chunkX, int chunkY, out ChunkedVisualHeightmapChunk chunk)
        {
            ValidateChunkCoordinates(chunkX, chunkY);
            long key = GraphChunkKey.Pack(chunkX, chunkY);
            ThreadCache cache = GetThreadCache();
            if (ReferenceEquals(cache.Owner, this) && cache.Key == key && cache.Chunk != null)
            {
                chunk = cache.Chunk;
                return true;
            }

            if (_chunks.TryGetValue(key, out chunk!))
            {
                cache.Owner = this;
                cache.Key = key;
                cache.Chunk = chunk;
                return true;
            }

            chunk = null!;
            return false;
        }

        public bool TryGetChunk(long chunkKey, out ChunkedVisualHeightmapChunk chunk)
        {
            ThreadCache cache = GetThreadCache();
            if (ReferenceEquals(cache.Owner, this) && cache.Key == chunkKey && cache.Chunk != null)
            {
                chunk = cache.Chunk;
                return true;
            }

            if (_chunks.TryGetValue(chunkKey, out chunk!))
            {
                cache.Owner = this;
                cache.Key = chunkKey;
                cache.Chunk = chunk;
                return true;
            }

            chunk = null!;
            return false;
        }

        private void OnChunkUnloaded(long chunkKey)
        {
            if (_chunks.Remove(chunkKey))
            {
                Revision++;
                InvalidateThreadCache(chunkKey);
                RecomputeMaxMipLevel();
            }
        }

        private static ThreadCache GetThreadCache()
        {
            return s_threadCache ??= new ThreadCache();
        }

        private void InvalidateThreadCache(long chunkKey)
        {
            ThreadCache cache = GetThreadCache();
            if (ReferenceEquals(cache.Owner, this) && cache.Key == chunkKey)
            {
                cache.Key = long.MinValue;
                cache.Chunk = null;
            }
        }

        private void ValidateChunkCoordinates(int chunkX, int chunkY)
        {
            if ((uint)chunkX >= (uint)Descriptor.ChunkColumns)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkX));
            }

            if ((uint)chunkY >= (uint)Descriptor.ChunkRows)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkY));
            }
        }

        private void ValidateChunkMips(ChunkedVisualHeightmapChunk chunk, bool expectsRaw)
        {
            for (int i = 0; i < chunk.MipLevels.Length; i++)
            {
                ChunkedVisualHeightmapChunkMipLevel mip = chunk.MipLevels[i];
                if (mip.UsesRawUInt16Samples != expectsRaw)
                {
                    throw new ArgumentException("Chunk mip payload does not match the descriptor storage encoding.", nameof(chunk));
                }

                int expectedSamples = checked(mip.SamplesPerLayerPerChunk * Descriptor.Layers.Length);
                int actualSamples = mip.UsesRawUInt16Samples ? mip.HeightSamplesRaw.Length : mip.HeightSamplesCm.Length;
                if (actualSamples != expectedSamples)
                {
                    throw new ArgumentException("Chunk mip sample payload does not match descriptor layer count.", nameof(chunk));
                }
            }
        }

        private void RecomputeMaxMipLevel()
        {
            int max = 0;
            foreach (ChunkedVisualHeightmapChunk chunk in _chunks.Values)
            {
                if (chunk.MipLevels.Length > max)
                {
                    max = chunk.MipLevels.Length;
                }
            }

            MaxMipLevel = max;
        }

        private sealed class ThreadCache
        {
            public ChunkedVisualHeightmapStore? Owner;

            public long Key = long.MinValue;

            public ChunkedVisualHeightmapChunk? Chunk;
        }
    }
}
