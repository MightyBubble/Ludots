using System;
using Ludots.Core.Navigation.GraphWorld;

namespace Ludots.Core.TransportNetwork
{
    public sealed class TransportNetworkChunkGraphSource : IDisposable
    {
        private readonly ChunkedNodeGraphStore _store;
        private readonly WorldGridLoadedChunks _loadedChunks;
        private readonly TransportNetworkBakedAsset _baked;
        private bool _disposed;

        public TransportNetworkChunkGraphSource(
            ChunkedNodeGraphStore store,
            WorldGridLoadedChunks loadedChunks,
            TransportNetworkBakedAsset baked)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _loadedChunks = loadedChunks ?? throw new ArgumentNullException(nameof(loadedChunks));
            _baked = baked ?? throw new ArgumentNullException(nameof(baked));
            _loadedChunks.ChunkLoaded += OnChunkLoaded;
        }

        public void LoadActiveChunks()
        {
            foreach (long chunkKey in _loadedChunks.ActiveChunkKeys)
            {
                OnChunkLoaded(chunkKey);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _loadedChunks.ChunkLoaded -= OnChunkLoaded;
        }

        private void OnChunkLoaded(long chunkKey)
        {
            if (_baked.TryGetGraphChunk(chunkKey, out GraphChunkData chunk))
            {
                _store.AddOrReplace(chunkKey, chunk);
            }
        }
    }
}
