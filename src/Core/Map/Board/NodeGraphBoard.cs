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
        public BoardExtentSpec BoardExtent { get; }
        public WorldSizeSpec WorldSize { get; }
        public ISpatialCoordinateConverter CoordinateConverter { get; }
        public ISpatialPartitionWorld SpatialPartition => Partition;
        public ISpatialQueryService QueryService => QueryServiceInstance;
        public ILoadedChunks LoadedChunks => LoadedChunksSource;
        public WorldGridLoadedChunks LoadedChunksSource { get; }
        public ChunkedNodeGraphStore GraphStore { get; }
        public LoadedGraphRuntime GraphRuntime { get; }

        // The graph store/runtime is the board's purpose and stays eager; only the entity
        // partition/query pair defers (satellite boards carry no consumers until BoardRef
        // dispatch lands, #1567 slice 2).
        private ChunkedGridSpatialPartitionWorld? _partition;
        private SpatialQueryService? _queryService;

        private ChunkedGridSpatialPartitionWorld Partition =>
            _partition ??= new ChunkedGridSpatialPartitionWorld(chunkSizeCells: _chunkSizeCells);

        private SpatialQueryService QueryServiceInstance =>
            _queryService ??= new SpatialQueryService(
                new ChunkedGridSpatialPartitionBackend(Partition, WorldSize));

        private readonly int _chunkSizeCells;
        private bool _disposed;

        public NodeGraphBoard(BoardId id, string name, BoardConfig config)
        {
            Id = id;
            Name = name;

            BoardExtent = config.ResolveExtent();
            WorldSize = BoardExtent.ToWorldSizeSpec();
            CoordinateConverter = new SpatialCoordinateConverter(
                config.GridCellSizeCm,
                BoardExtent.TopologyOriginXCm,
                BoardExtent.TopologyOriginYCm);
            _chunkSizeCells = config.ChunkSizeCells;

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
            _partition?.Clear();
        }
    }
}
