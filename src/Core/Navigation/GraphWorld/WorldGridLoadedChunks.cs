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
    public sealed class WorldGridLoadedChunks : ILoadedChunks, IWorldChunkKeyResolver
    {
        private readonly HashSet<long> _activeChunks;
        private readonly HashSet<long> _nextActiveChunks;
        private readonly List<long> _eventScratch;

        public int ChunkSizeCm { get; }
        public IReadOnlyCollection<long> ActiveChunkKeys => _activeChunks;

        public event Action<long> ChunkLoaded;
        public event Action<long> ChunkUnloaded;

        public WorldGridLoadedChunks(int chunkSizeCm, int loadedChunkCapacity = 0)
        {
            if (chunkSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizeCm));
            }

            if (loadedChunkCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(loadedChunkCapacity));
            }

            _activeChunks = loadedChunkCapacity > 0
                ? new HashSet<long>(loadedChunkCapacity)
                : new HashSet<long>();
            _nextActiveChunks = loadedChunkCapacity > 0
                ? new HashSet<long>(loadedChunkCapacity)
                : new HashSet<long>();
            _eventScratch = loadedChunkCapacity > 0
                ? new List<long>(loadedChunkCapacity)
                : new List<long>();
            ChunkSizeCm = chunkSizeCm;
        }

        public bool IsLoaded(long chunkKey)
        {
            return _activeChunks.Contains(chunkKey);
        }

        public long GetChunkKeyForWorldCm(float worldXCm, float worldYCm)
        {
            int chunkX = MathUtil.FloorDiv((int)MathF.Floor(worldXCm), ChunkSizeCm);
            int chunkY = MathUtil.FloorDiv((int)MathF.Floor(worldYCm), ChunkSizeCm);
            return GraphChunkKey.Pack(chunkX, chunkY);
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

            _eventScratch.Clear();
            foreach (long chunkKey in _activeChunks)
            {
                if (!_nextActiveChunks.Contains(chunkKey))
                {
                    _eventScratch.Add(chunkKey);
                }
            }
            _eventScratch.Sort();
            for (int i = 0; i < _eventScratch.Count; i++)
            {
                ChunkUnloaded?.Invoke(_eventScratch[i]);
            }

            _eventScratch.Clear();
            foreach (long chunkKey in _nextActiveChunks)
            {
                if (!_activeChunks.Contains(chunkKey))
                {
                    _eventScratch.Add(chunkKey);
                }
            }
            _eventScratch.Sort();
            for (int i = 0; i < _eventScratch.Count; i++)
            {
                ChunkLoaded?.Invoke(_eventScratch[i]);
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

            _eventScratch.Clear();
            foreach (long chunkKey in _activeChunks)
            {
                _eventScratch.Add(chunkKey);
            }
            _eventScratch.Sort();
            _activeChunks.Clear();

            for (int i = 0; i < _eventScratch.Count; i++)
            {
                ChunkUnloaded?.Invoke(_eventScratch[i]);
            }
        }
    }
}
