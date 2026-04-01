using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.GraphWorld
{
    /// <summary>
    /// Map-scoped loaded chunk set for world-space grid chunks keyed by <see cref="GraphChunkKey"/>.
    /// Supports explicit chunk toggles and AOI-style square updates without per-update allocations.
    /// </summary>
    public sealed class WorldGridLoadedChunks : ILoadedChunks
    {
        private readonly HashSet<long> _activeChunks = new HashSet<long>();
        private readonly HashSet<long> _nextActiveChunks = new HashSet<long>();

        public int ChunkSizeCm { get; }
        public IReadOnlyCollection<long> ActiveChunkKeys => _activeChunks;

        public event Action<long> ChunkLoaded;
        public event Action<long> ChunkUnloaded;

        public WorldGridLoadedChunks(int chunkSizeCm)
        {
            if (chunkSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizeCm));
            }

            ChunkSizeCm = chunkSizeCm;
        }

        public bool IsLoaded(long chunkKey)
        {
            return _activeChunks.Contains(chunkKey);
        }

        public void SetLoaded(long chunkKey, bool loaded)
        {
            if (loaded)
            {
                if (_activeChunks.Add(chunkKey))
                {
                    ChunkLoaded?.Invoke(chunkKey);
                }

                return;
            }

            if (_activeChunks.Remove(chunkKey))
            {
                ChunkUnloaded?.Invoke(chunkKey);
            }
        }

        public void Update(int centerXcm, int centerYcm, int radiusCm)
        {
            if (radiusCm < 0)
            {
                radiusCm = 0;
            }

            int minChunkX = MathUtil.FloorDiv(centerXcm - radiusCm, ChunkSizeCm);
            int maxChunkX = MathUtil.FloorDiv(centerXcm + radiusCm, ChunkSizeCm);
            int minChunkY = MathUtil.FloorDiv(centerYcm - radiusCm, ChunkSizeCm);
            int maxChunkY = MathUtil.FloorDiv(centerYcm + radiusCm, ChunkSizeCm);

            _nextActiveChunks.Clear();
            for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
            {
                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    _nextActiveChunks.Add(GraphChunkKey.Pack(chunkX, chunkY));
                }
            }

            foreach (long chunkKey in _activeChunks)
            {
                if (!_nextActiveChunks.Contains(chunkKey))
                {
                    ChunkUnloaded?.Invoke(chunkKey);
                }
            }

            foreach (long chunkKey in _nextActiveChunks)
            {
                if (!_activeChunks.Contains(chunkKey))
                {
                    ChunkLoaded?.Invoke(chunkKey);
                }
            }

            _activeChunks.Clear();
            foreach (long chunkKey in _nextActiveChunks)
            {
                _activeChunks.Add(chunkKey);
            }
        }

        public void Reset()
        {
            if (_activeChunks.Count == 0)
            {
                return;
            }

            long[] snapshot = new long[_activeChunks.Count];
            _activeChunks.CopyTo(snapshot);
            _activeChunks.Clear();

            for (int i = 0; i < snapshot.Length; i++)
            {
                ChunkUnloaded?.Invoke(snapshot[i]);
            }
        }
    }
}
