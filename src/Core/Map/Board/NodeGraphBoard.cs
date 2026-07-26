using System;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Board backed by a chunked node graph (for strategic maps, city networks, etc.).
    /// </summary>
    public sealed class NodeGraphBoard : INodeGraphBoard
    {
        public BoardId Id { get; }
        public string Name { get; }
        public WorldSizeSpec WorldSize { get; }
        public ISpatialCoordinateConverter CoordinateConverter { get; }
        public ISpatialPartitionWorld SpatialPartition { get; }
        public ISpatialQueryService QueryService { get; }
        public ILoadedChunks LoadedChunks => LoadedChunksSource;
        public WorldGridLoadedChunks LoadedChunksSource { get; }
        public ChunkedNodeGraphStore GraphStore { get; }
        public LoadedGraphRuntime GraphRuntime { get; }

        private bool _disposed;

        public NodeGraphBoard(BoardId id, string name, BoardConfig config)
        {
            Id = id;
            Name = name;

            var worldExtent = new WorldExtentSpec(
                config.WidthInMacroTiles,
                config.HeightInMacroTiles,
                config.GridCellSizeCm);
            WorldSize = worldExtent.ToWorldSizeSpec();
            CoordinateConverter = new SpatialCoordinateConverter(WorldSize);

            var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: config.ChunkSizeCells);
            SpatialPartition = partition;
            QueryService = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, WorldSize));

            int streamingChunkSizeCm = config.ChunkSizeCells * config.GridCellSizeCm;
            LoadedChunksSource = new WorldGridLoadedChunks(streamingChunkSizeCm, config.LoadedChunkCapacity);
            GraphStore = new ChunkedNodeGraphStore();
            GraphStore.SubscribeToLoadedChunks(LoadedChunksSource);
            GraphRuntime = new LoadedGraphRuntime(
                GraphStore,
                LoadedChunksSource,
                preferredProjectionCellSizeCm: streamingChunkSizeCm);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GraphRuntime.Dispose();
            GraphStore.UnsubscribeFromLoadedChunks();
            LoadedChunksSource.Reset();
            SpatialPartition?.Clear();
        }
    }
}
