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
        public int LoadedChunkCapacity { get; }
        public IReadOnlyCollection<long> ActiveChunkKeys => _activeChunks;

        public event Action<long> ChunkLoaded;
        public event Action<long> ChunkUnloaded;

        public WorldGridLoadedChunks(int chunkSizeCm, int loadedChunkCapacity)
        {
            if (chunkSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizeCm));
            }

            if (loadedChunkCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(loadedChunkCapacity));
            }

            _activeChunks = new HashSet<long>(loadedChunkCapacity);
            _nextActiveChunks = new HashSet<long>(loadedChunkCapacity);
            _eventScratch = new List<long>(loadedChunkCapacity);
            ChunkSizeCm = chunkSizeCm;
            LoadedChunkCapacity = loadedChunkCapacity;
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
                if (_activeChunks.Contains(chunkKey))
                {
                    return;
                }

                EnsureCapacityFor(checked(_activeChunks.Count + 1));
                _activeChunks.Add(chunkKey);
                ChunkLoaded?.Invoke(chunkKey);
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
                throw new ArgumentOutOfRangeException(nameof(radiusCm));
            }

            int minChunkX = ResolveChunkCoordinate((long)centerXcm - radiusCm);
            int maxChunkX = ResolveChunkCoordinate((long)centerXcm + radiusCm);
            int minChunkY = ResolveChunkCoordinate((long)centerYcm - radiusCm);
            int maxChunkY = ResolveChunkCoordinate((long)centerYcm + radiusCm);
            long width = (long)maxChunkX - minChunkX + 1L;
            long height = (long)maxChunkY - minChunkY + 1L;
            long requiredChunkCount = checked(width * height);
            EnsureCapacityFor(requiredChunkCount);

            _nextActiveChunks.Clear();
            for (long chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
            {
                for (long chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    _nextActiveChunks.Add(GraphChunkKey.Pack((int)chunkX, (int)chunkY));
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

        private int ResolveChunkCoordinate(long worldCoordinateCm)
        {
            long quotient = worldCoordinateCm / ChunkSizeCm;
            long remainder = worldCoordinateCm % ChunkSizeCm;
            if (remainder != 0 && worldCoordinateCm < 0)
            {
                quotient--;
            }

            if (quotient < int.MinValue || quotient > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"World chunk coordinate {quotient} exceeds the GraphChunkKey coordinate range.");
            }

            return (int)quotient;
        }

        private void EnsureCapacityFor(long requiredChunkCount)
        {
            if (requiredChunkCount > LoadedChunkCapacity)
            {
                throw new InvalidOperationException(
                    $"Loaded chunk count {requiredChunkCount} exceeds configured capacity {LoadedChunkCapacity}.");
            }
        }
    }
}
