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
        private readonly HashSet<long> _directChunks;
        private readonly HashSet<long> _nextDirectChunks;
        private readonly Dictionary<long, int> _contributionCounts;
        private int _generation;

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
            _directChunks = loadedChunkCapacity > 0
                ? new HashSet<long>(loadedChunkCapacity)
                : new HashSet<long>();
            _nextDirectChunks = loadedChunkCapacity > 0
                ? new HashSet<long>(loadedChunkCapacity)
                : new HashSet<long>();
            _contributionCounts = loadedChunkCapacity > 0
                ? new Dictionary<long, int>(loadedChunkCapacity)
                : new Dictionary<long, int>();
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
                radiusCm = 0;
            }

            int minChunkX = MathUtil.FloorDiv(centerXcm - radiusCm, ChunkSizeCm);
            int maxChunkX = MathUtil.FloorDiv(centerXcm + radiusCm, ChunkSizeCm);
            int minChunkY = MathUtil.FloorDiv(centerYcm - radiusCm, ChunkSizeCm);
            int maxChunkY = MathUtil.FloorDiv(centerYcm + radiusCm, ChunkSizeCm);

            _nextDirectChunks.Clear();
            for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
            {
                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    _nextDirectChunks.Add(GraphChunkKey.Pack(chunkX, chunkY));
                }
            }

            foreach (long chunkKey in _directChunks)
            {
                if (!_nextDirectChunks.Contains(chunkKey))
                {
                    DeactivateIfUnreferenced(chunkKey, directWillRemain: false);
                }
            }

            foreach (long chunkKey in _nextDirectChunks)
            {
                if (!_directChunks.Contains(chunkKey))
                {
                    Activate(chunkKey);
                }
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
            if (_activeChunks.Add(chunkKey))
            {
                ChunkLoaded?.Invoke(chunkKey);
            }
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
            long[] snapshot = new long[_activeChunks.Count];
            _activeChunks.CopyTo(snapshot);
            _activeChunks.Clear();
            _directChunks.Clear();
            _nextDirectChunks.Clear();
            _contributionCounts.Clear();
            _generation = checked(_generation + 1);

            for (int i = 0; i < snapshot.Length; i++)
            {
                ChunkUnloaded?.Invoke(snapshot[i]);
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
