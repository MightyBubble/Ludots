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

        public int LoadedChunkCount => _chunks.Count;

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

            long key = GraphChunkKey.Pack(chunk.ChunkX, chunk.ChunkY);
            _chunks[key] = chunk;
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
                InvalidateThreadCache(key);
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
                InvalidateThreadCache(chunkKey);
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

        private sealed class ThreadCache
        {
            public ChunkedVisualHeightmapStore? Owner;

            public long Key = long.MinValue;

            public ChunkedVisualHeightmapChunk? Chunk;
        }
    }
}
