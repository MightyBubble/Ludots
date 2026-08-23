using System;
using System.Collections.Generic;
using Ludots.Core.Map.Hex;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.AOI
{
    public class HexGridAOI : ILoadedChunkWindowSource, IWorldChunkKeyResolver
    {
        private readonly HashSet<long> _activeChunks = new HashSet<long>();
        private readonly HashSet<long> _directChunks = new HashSet<long>();
        private readonly HashSet<long> _nextDirectChunks = new HashSet<long>();
        private readonly Dictionary<long, int> _contributionCounts = new Dictionary<long, int>();
        private readonly List<long> _eventScratch = new List<long>();
        private readonly List<IAOIListener> _listeners = new List<IAOIListener>();
        private readonly HexMetrics _hexMetrics;
        private int _generation;

        public HexGridAOI()
            : this(HexMetrics.Default)
        {
        }

        public HexGridAOI(HexMetrics hexMetrics)
        {
            _hexMetrics = hexMetrics ?? throw new ArgumentNullException(nameof(hexMetrics));
        }

        // ILoadedChunks implementation
        public IReadOnlyCollection<long> ActiveChunkKeys => _activeChunks;

        public bool IsLoaded(long chunkKey) => _activeChunks.Contains(chunkKey);

        public long GetChunkKeyForWorldCm(float worldXCm, float worldYCm)
        {
            HexCoordinates hex = _hexMetrics.WorldCmToHex(worldXCm, worldYCm);
            (int col, int row) = hex.ToOffsetCoordinates();
            return HexCoordinates.GetChunkKey(col >> 6, row >> 6);
        }

        public event Action<long> ChunkLoaded;
        public event Action<long> ChunkUnloaded;

        public void AddListener(IAOIListener listener)
        {
            _listeners.Add(listener);
        }

        public void RemoveListener(IAOIListener listener)
        {
            _listeners.Remove(listener);
        }

        /// <summary>
        /// Force-clear all active chunks, firing ChunkUnloaded for each.
        /// Used by MapSession.Cleanup when switching maps.
        /// Snapshot to array before iterating to prevent InvalidOperationException
        /// if an event subscriber modifies _activeChunks re-entrantly.
        /// </summary>
        public void Reset()
        {
            if (_activeChunks.Count == 0) return;

            // Snapshot keys — event handlers may call back into this instance
            var snapshot = new long[_activeChunks.Count];
            _activeChunks.CopyTo(snapshot);
            _activeChunks.Clear();
            _directChunks.Clear();
            _nextDirectChunks.Clear();
            _contributionCounts.Clear();
            _generation = checked(_generation + 1);

            foreach (long key in snapshot)
            {
                NotifyExit(key);
            }
        }

        public void Update(IAOISource source)
        {
            _nextDirectChunks.Clear();
            CollectWindowChunkKeys(source.CenterXcm, source.CenterZcm, source.RadiusCm, _nextDirectChunks);

            _eventScratch.Clear();
            foreach (long key in _directChunks)
            {
                if (!_nextDirectChunks.Contains(key))
                {
                    _eventScratch.Add(key);
                }
            }

            _eventScratch.Sort();
            for (int i = 0; i < _eventScratch.Count; i++)
            {
                long key = _eventScratch[i];
                _directChunks.Remove(key);
                DeactivateIfUnreferenced(key);
            }

            _eventScratch.Clear();
            foreach (long key in _nextDirectChunks)
            {
                if (!_directChunks.Contains(key))
                {
                    _eventScratch.Add(key);
                }
            }

            _eventScratch.Sort();
            for (int i = 0; i < _eventScratch.Count; i++)
            {
                long key = _eventScratch[i];
                _directChunks.Add(key);
                Activate(key);
            }
        }

        public void CollectWindowChunkKeys(int centerXcm, int centerYcm, int radiusCm, ICollection<long> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (radiusCm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusCm));
            }

            HexCoordinates centerHex = _hexMetrics.WorldCmToHex(centerXcm, centerYcm);
            int chunkWorldSizeCm = SpatialScaleDefaults.TerrainChunkCells * _hexMetrics.EdgeLengthCm;
            int radiusInChunks = (int)Math.Ceiling((float)radiusCm / chunkWorldSizeCm) + 1;

            (int cx, int cy) = centerHex.ToOffsetCoordinates();
            int centerChunkX = cx >> VertexChunk.ChunkSizeShift;
            int centerChunkY = cy >> VertexChunk.ChunkSizeShift;

            for (int x = centerChunkX - radiusInChunks; x <= centerChunkX + radiusInChunks; x++)
            {
                for (int y = centerChunkY - radiusInChunks; y <= centerChunkY + radiusInChunks; y++)
                {
                    destination.Add(HexCoordinates.GetChunkKey(x, y));
                }
            }
        }

        public HexGridLoadedChunkContributor AcquireContributor(string key, int capacity)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Loaded-chunk contributor key is required.", nameof(key));
            }

            return new HexGridLoadedChunkContributor(this, key, capacity);
        }

        ILoadedChunkContributor ILoadedChunkWindowSource.AcquireContributor(string key, int capacity)
        {
            return AcquireContributor(key, capacity);
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

        private void Activate(long key)
        {
            if (_activeChunks.Add(key))
            {
                NotifyEnter(key);
            }
        }

        private void DeactivateIfUnreferenced(long key)
        {
            if (!_directChunks.Contains(key) && !_contributionCounts.ContainsKey(key) && _activeChunks.Remove(key))
            {
                NotifyExit(key);
            }
        }

        private void NotifyEnter(long key)
        {
            foreach (var listener in _listeners) listener.OnChunkEnter(key);
            ChunkLoaded?.Invoke(key);
        }

        private void NotifyExit(long key)
        {
            foreach (var listener in _listeners) listener.OnChunkExit(key);
            ChunkUnloaded?.Invoke(key);
        }

        internal int Generation => _generation;
    }

    public sealed class HexGridLoadedChunkContributor : ILoadedChunkContributor
    {
        private readonly HexGridAOI _owner;
        private readonly HashSet<long> _activeChunks;
        private int _generation;
        private bool _disposed;

        internal HexGridLoadedChunkContributor(HexGridAOI owner, string key, int capacity)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Key = key;
            Capacity = capacity;
            _activeChunks = new HashSet<long>(capacity);
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
