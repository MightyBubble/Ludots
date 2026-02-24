using System;
using System.Collections.Generic;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Single source of truth for which chunks are currently loaded/active.
    /// Consumers (ChunkedNodeGraphStore, SpatialQueryService, VertexMap, etc.)
    /// subscribe to ChunkLoaded/ChunkUnloaded events to synchronize their state.
    /// </summary>
    public interface ILoadedChunks
    {
        /// <summary>
        /// Currently active chunk keys.
        /// </summary>
        IReadOnlyCollection<long> ActiveChunkKeys { get; }

        /// <summary>
        /// Check if a specific chunk is currently loaded.
        /// </summary>
        bool IsLoaded(long chunkKey);

        /// <summary>
        /// Fired when a chunk becomes active/loaded.
        /// </summary>
        event Action<long> ChunkLoaded;

        /// <summary>
        /// Fired when a chunk is deactivated/unloaded.
        /// </summary>
        event Action<long> ChunkUnloaded;
    }
}
