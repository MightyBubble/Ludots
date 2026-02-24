using System;
using System.Collections.Generic;
using Ludots.Core.Map.Hex;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.AOI
{
    public class HexGridAOI : ILoadedChunks
    {
        private readonly HashSet<long> _activeChunks = new HashSet<long>();
        private readonly List<IAOIListener> _listeners = new List<IAOIListener>();
        
        // Temporary set for update logic to avoid allocs
        private readonly HashSet<long> _newActiveChunks = new HashSet<long>();

        // ILoadedChunks implementation
        public IReadOnlyCollection<long> ActiveChunkKeys => _activeChunks;

        public bool IsLoaded(long chunkKey) => _activeChunks.Contains(chunkKey);

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

            foreach (long key in snapshot)
            {
                NotifyExit(key);
            }
        }

        public void Update(IAOISource source)
        {
            _newActiveChunks.Clear();

            // Convert world position (cm) to Hex
            var centerHex = HexCoordinates.FromWorldPositionCm(
                new System.Numerics.Vector3(source.CenterXcm, 0f, source.CenterZcm));
            
            // Calculate chunk range based on radius (all in centimeters)
            int chunkWorldSizeCm = 64 * HexCoordinates.EdgeLengthCm; 
            int radiusInChunks = (int)Math.Ceiling((float)source.RadiusCm / chunkWorldSizeCm) + 1;

            (int cx, int cy) = centerHex.ToOffsetCoordinates(); // Approx center chunk
            int centerChunkX = cx >> 6;
            int centerChunkY = cy >> 6;

            // Simple square loop around center chunk
            for (int x = centerChunkX - radiusInChunks; x <= centerChunkX + radiusInChunks; x++)
            {
                for (int y = centerChunkY - radiusInChunks; y <= centerChunkY + radiusInChunks; y++)
                {
                    // Precise distance check could be added here if needed
                    long key = HexCoordinates.GetChunkKey(x, y);
                    _newActiveChunks.Add(key);
                }
            }

            // Detect Exits
            foreach (long key in _activeChunks)
            {
                if (!_newActiveChunks.Contains(key))
                {
                    NotifyExit(key);
                }
            }

            // Detect Enters
            foreach (long key in _newActiveChunks)
            {
                if (!_activeChunks.Contains(key))
                {
                    NotifyEnter(key);
                }
            }

            // Swap sets
            _activeChunks.Clear();
            foreach (var key in _newActiveChunks)
            {
                _activeChunks.Add(key);
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
    }
}
