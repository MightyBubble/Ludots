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
        IReadOnlyCollection<long> ActiveChunkKeys { get; }

        bool IsLoaded(long chunkKey);

        event Action<long> ChunkLoaded;

        event Action<long> ChunkUnloaded;
    }

    public interface IWorldChunkKeyResolver
    {
        long GetChunkKeyForWorldCm(float worldXCm, float worldYCm);
    }
}
