using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.GraphWorld
{
    /// <summary>
    /// Map-scoped loaded chunk set for world-space grid chunks keyed by <see cref="GraphChunkKey"/>.
    /// Supports explicit chunk toggles, contributor-owned chunks, and AOI-style square updates.
    /// </summary>
    public sealed class WorldGridLoadedChunks : ILoadedChunks, IWorldChunkKeyResolver
    {
        private readonly HashSet<long> _activeChunks;
        private readonly HashSet<long> _directChunks;
        private readonly HashSet<long> _nextDirectChunks;
        private readonly Dictionary<long, int> _contributionCounts;
        private readonly List<long> _eventScratch;
        private int _generation;

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
            _directChunks = new HashSet<long>(loadedChunkCapacity);
            _nextDirectChunks = new HashSet<long>(loadedChunkCapacity);
            _contributionCounts = new Dictionary<long, int>(loadedChunkCapacity);
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
                if (_directChunks.Add(chunkKey))
                {
                    Activate(chunkKey);
                }

                return;
            }

            if (_directChunks.Remove(chunkKey))
            {
                DeactivateIfUnreferenced(chunkKey);
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

            _nextDirectChunks.Clear();
            for (long chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
            {
                for (long chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    _nextDirectChunks.Add(GraphChunkKey.Pack((int)chunkX, (int)chunkY));
                }
            }

            _eventScratch.Clear();
            foreach (long chunkKey in _directChunks)
            {
                if (!_nextDirectChunks.Contains(chunkKey))
                {
                    _eventScratch.Add(chunkKey);
                }
            }
            _eventScratch.Sort();
            for (int i = 0; i < _eventScratch.Count; i++)
            {
                DeactivateIfUnreferenced(_eventScratch[i], directWillRemain: false);
            }

            _eventScratch.Clear();
            foreach (long chunkKey in _nextDirectChunks)
            {
                if (!_directChunks.Contains(chunkKey))
                {
                    _eventScratch.Add(chunkKey);
                }
            }
            _eventScratch.Sort();
            for (int i = 0; i < _eventScratch.Count; i++)
            {
                Activate(_eventScratch[i]);
            }

            _directChunks.Clear();
            foreach (long chunkKey in _nextDirectChunks)
            {
                _directChunks.Add(chunkKey);
            }
        }

        public WorldGridLoadedChunkContributor AcquireContributor(string key, int capacity)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Loaded-chunk contributor key is required.", nameof(key));
            }

            return new WorldGridLoadedChunkContributor(this, key, capacity);
        }

        internal void SetContributionLoaded(long chunkKey, bool loaded)
        {
            if (loaded)
            {
                _contributionCounts.TryGetValue(chunkKey, out int count);
                _contributionCounts[chunkKey] = checked(count + 1);
                if (count == 0)
                {
                    Activate(chunkKey);
                }

                return;
            }

            if (!_contributionCounts.TryGetValue(chunkKey, out int current) || current <= 0)
            {
                throw new InvalidOperationException($"Loaded-chunk contribution underflow for key {chunkKey}.");
            }

            if (current == 1)
            {
                _contributionCounts.Remove(chunkKey);
                DeactivateIfUnreferenced(chunkKey);
            }
            else
            {
                _contributionCounts[chunkKey] = current - 1;
            }
        }

        private void Activate(long chunkKey)
        {
            if (_activeChunks.Contains(chunkKey))
            {
                return;
            }

            EnsureCapacityFor(checked(_activeChunks.Count + 1));
            _activeChunks.Add(chunkKey);
            ChunkLoaded?.Invoke(chunkKey);
        }

        private void DeactivateIfUnreferenced(long chunkKey, bool? directWillRemain = null)
        {
            bool direct = directWillRemain ?? _directChunks.Contains(chunkKey);
            if (!direct && !_contributionCounts.ContainsKey(chunkKey) && _activeChunks.Remove(chunkKey))
            {
                ChunkUnloaded?.Invoke(chunkKey);
            }
        }

        public void Reset()
        {
            _eventScratch.Clear();
            foreach (long chunkKey in _activeChunks)
            {
                _eventScratch.Add(chunkKey);
            }
            _eventScratch.Sort();

            _activeChunks.Clear();
            _directChunks.Clear();
            _nextDirectChunks.Clear();
            _contributionCounts.Clear();
            _generation = checked(_generation + 1);

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

        internal int Generation => _generation;
    }

    public sealed class WorldGridLoadedChunkContributor : IDisposable
    {
        private readonly WorldGridLoadedChunks _owner;
        private readonly HashSet<long> _activeChunks;
        private int _generation;
        private bool _disposed;

        internal WorldGridLoadedChunkContributor(WorldGridLoadedChunks owner, string key, int capacity)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Key = key;
            _activeChunks = new HashSet<long>(capacity);
            Capacity = capacity;
            _generation = owner.Generation;
        }

        public string Key { get; }
        public int Capacity { get; }
        public IReadOnlyCollection<long> ActiveChunkKeys
        {
            get
            {
                SynchronizeGeneration();
                return _activeChunks;
            }
        }

        public void SetLoaded(long chunkKey, bool loaded)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            SynchronizeGeneration();
            if (loaded)
            {
                if (_activeChunks.Contains(chunkKey))
                {
                    return;
                }

                if (_activeChunks.Count >= Capacity)
                {
                    throw new InvalidOperationException(
                        $"Loaded-chunk contributor '{Key}' exceeded capacity {Capacity}.");
                }

                _activeChunks.Add(chunkKey);
                _owner.SetContributionLoaded(chunkKey, true);
                return;
            }

            if (_activeChunks.Remove(chunkKey))
            {
                _owner.SetContributionLoaded(chunkKey, false);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            SynchronizeGeneration();

            foreach (long chunkKey in _activeChunks)
            {
                _owner.SetContributionLoaded(chunkKey, false);
            }

            _activeChunks.Clear();
            _disposed = true;
        }

        private void SynchronizeGeneration()
        {
            if (_generation == _owner.Generation)
            {
                return;
            }

            _activeChunks.Clear();
            _generation = _owner.Generation;
        }
    }
}
