using System;
using System.Collections.Generic;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation2D.FlowField
{
    public sealed class CrowdFlowChunkStreaming : IDisposable
    {
        private readonly ILoadedChunks _loadedChunks;
        private readonly IReadOnlyList<CrowdFlow2D> _flows;

        public CrowdFlowChunkStreaming(ILoadedChunks loadedChunks, IReadOnlyList<CrowdFlow2D> flows)
        {
            _loadedChunks = loadedChunks ?? throw new ArgumentNullException(nameof(loadedChunks));
            _flows = flows ?? throw new ArgumentNullException(nameof(flows));

            _loadedChunks.ChunkLoaded += OnChunkLoaded;
            _loadedChunks.ChunkUnloaded += OnChunkUnloaded;

            foreach (long key in _loadedChunks.ActiveChunkKeys)
            {
                for (int i = 0; i < _flows.Count; i++)
                {
                    _flows[i].OnTileLoaded(key);
                }
            }
        }

        private void OnChunkLoaded(long chunkKey)
        {
            for (int i = 0; i < _flows.Count; i++)
            {
                _flows[i].OnTileLoaded(chunkKey);
            }
        }

        private void OnChunkUnloaded(long chunkKey)
        {
            for (int i = 0; i < _flows.Count; i++)
            {
                _flows[i].OnTileUnloaded(chunkKey);
            }
        }

        public void Dispose()
        {
            _loadedChunks.ChunkLoaded -= OnChunkLoaded;
            _loadedChunks.ChunkUnloaded -= OnChunkUnloaded;
        }
    }
}
