using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.GraphWorld
{
    /// <summary>
    /// Board-owned loaded chunk set for world-space grid chunks keyed by <see cref="GraphChunkKey"/>.
    /// Contributor leases publish independent windows; the reader-visible set is their union.
    /// </summary>
    public sealed class WorldGridLoadedChunks : ILoadedChunks, IWorldChunkKeyResolver
    {
        private readonly HashSet<long> _activeChunks;
        private readonly Dictionary<long, int> _contributionCounts;
        private readonly Dictionary<string, WorldGridLoadedChunkContributor> _contributors;
        private readonly WorldGridLoadedChunkContributor _directContributor;

        public int ChunkSizeCm { get; }
        public IReadOnlyCollection<long> ActiveChunkKeys => _activeChunks;
        public int ContributorCount => _contributors.Count;

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
            _contributionCounts = loadedChunkCapacity > 0
                ? new Dictionary<long, int>(loadedChunkCapacity)
                : new Dictionary<long, int>();
            _contributors = new Dictionary<string, WorldGridLoadedChunkContributor>(StringComparer.Ordinal);
            _directContributor = new WorldGridLoadedChunkContributor(this, "$direct", loadedChunkCapacity);
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
            _directContributor.SetLoaded(chunkKey, loaded);
        }

        public void Update(int centerXcm, int centerYcm, int radiusCm)
        {
            _directContributor.UpdateWindow(centerXcm, centerYcm, radiusCm);
        }

        public WorldGridLoadedChunkContributor AcquireContributor(string key, int chunkCapacity = 0)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Loaded-chunk contributor key must be non-empty.", nameof(key));
            }

            if (chunkCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCapacity));
            }

            if (_contributors.ContainsKey(key))
            {
                throw new InvalidOperationException($"Loaded-chunk contributor '{key}' is already acquired.");
            }

            var contributor = new WorldGridLoadedChunkContributor(this, key, chunkCapacity);
            _contributors.Add(key, contributor);
            return contributor;
        }

        public void Reset()
        {
            _directContributor.ClearCore();
            if (_contributors.Count > 0)
            {
                var contributors = new WorldGridLoadedChunkContributor[_contributors.Count];
                _contributors.Values.CopyTo(contributors, 0);
                for (int i = 0; i < contributors.Length; i++)
                {
                    contributors[i].ClearCore();
                }
            }
        }

        internal void AddContribution(long chunkKey)
        {
            if (_contributionCounts.TryGetValue(chunkKey, out int count))
            {
                _contributionCounts[chunkKey] = checked(count + 1);
                return;
            }

            _contributionCounts.Add(chunkKey, 1);
            _activeChunks.Add(chunkKey);
            ChunkLoaded?.Invoke(chunkKey);
        }

        internal void RemoveContribution(long chunkKey)
        {
            if (!_contributionCounts.TryGetValue(chunkKey, out int count))
            {
                throw new InvalidOperationException($"Loaded chunk {chunkKey} has no contribution to remove.");
            }

            if (count > 1)
            {
                _contributionCounts[chunkKey] = count - 1;
                return;
            }

            _contributionCounts.Remove(chunkKey);
            _activeChunks.Remove(chunkKey);
            ChunkUnloaded?.Invoke(chunkKey);
        }

        internal void ReleaseContributor(WorldGridLoadedChunkContributor contributor)
        {
            if (!_contributors.TryGetValue(contributor.Key, out WorldGridLoadedChunkContributor? current) ||
                !ReferenceEquals(current, contributor))
            {
                return;
            }

            _contributors.Remove(contributor.Key);
            contributor.ClearCore();
        }
    }

    public sealed class WorldGridLoadedChunkContributor : IDisposable
    {
        private readonly WorldGridLoadedChunks _owner;
        private readonly HashSet<long> _activeChunks;
        private readonly HashSet<long> _nextActiveChunks;
        private bool _disposed;

        internal WorldGridLoadedChunkContributor(WorldGridLoadedChunks owner, string key, int chunkCapacity)
        {
            _owner = owner;
            Key = key;
            _activeChunks = chunkCapacity > 0
                ? new HashSet<long>(chunkCapacity)
                : new HashSet<long>();
            _nextActiveChunks = chunkCapacity > 0
                ? new HashSet<long>(chunkCapacity)
                : new HashSet<long>();
        }

        public string Key { get; }
        public int ChunkSizeCm => _owner.ChunkSizeCm;
        public IReadOnlyCollection<long> ActiveChunkKeys => _activeChunks;

        public void SetLoaded(long chunkKey, bool loaded)
        {
            ThrowIfDisposed();
            if (loaded)
            {
                if (_activeChunks.Add(chunkKey))
                {
                    _owner.AddContribution(chunkKey);
                }

                return;
            }

            if (_activeChunks.Remove(chunkKey))
            {
                _owner.RemoveContribution(chunkKey);
            }
        }

        public void UpdateWindow(int centerXcm, int centerYcm, int radiusCm)
        {
            ThrowIfDisposed();
            int clampedRadiusCm = Math.Max(0, radiusCm);
            int minChunkX = MathUtil.FloorDiv(centerXcm - clampedRadiusCm, _owner.ChunkSizeCm);
            int maxChunkX = MathUtil.FloorDiv(centerXcm + clampedRadiusCm, _owner.ChunkSizeCm);
            int minChunkY = MathUtil.FloorDiv(centerYcm - clampedRadiusCm, _owner.ChunkSizeCm);
            int maxChunkY = MathUtil.FloorDiv(centerYcm + clampedRadiusCm, _owner.ChunkSizeCm);

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
                    _owner.RemoveContribution(chunkKey);
                }
            }

            foreach (long chunkKey in _nextActiveChunks)
            {
                if (!_activeChunks.Contains(chunkKey))
                {
                    _owner.AddContribution(chunkKey);
                }
            }

            _activeChunks.Clear();
            foreach (long chunkKey in _nextActiveChunks)
            {
                _activeChunks.Add(chunkKey);
            }
        }

        public void Clear()
        {
            ThrowIfDisposed();
            ClearCore();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.ReleaseContributor(this);
        }

        internal void ClearCore()
        {
            if (_activeChunks.Count == 0)
            {
                return;
            }

            _nextActiveChunks.Clear();
            foreach (long chunkKey in _activeChunks)
            {
                _nextActiveChunks.Add(chunkKey);
            }

            _activeChunks.Clear();
            foreach (long chunkKey in _nextActiveChunks)
            {
                _owner.RemoveContribution(chunkKey);
            }

            _nextActiveChunks.Clear();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
