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

            int gridCellSizeCm = config.GridCellSizeCm;
            int worldWidthCm = config.WidthInTiles * 256 * gridCellSizeCm;
            int worldHeightCm = config.HeightInTiles * 256 * gridCellSizeCm;
            WorldSize = new WorldSizeSpec(
                new Mathematics.WorldAabbCm(-worldWidthCm / 2, -worldHeightCm / 2, worldWidthCm, worldHeightCm),
                gridCellSizeCm);
            CoordinateConverter = new SpatialCoordinateConverter(WorldSize);

            var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: config.ChunkSizeCells);
            SpatialPartition = partition;
            QueryService = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, WorldSize));

            LoadedChunksSource = new WorldGridLoadedChunks(config.ChunkSizeCells * gridCellSizeCm);
            GraphStore = new ChunkedNodeGraphStore();
            GraphStore.SubscribeToLoadedChunks(LoadedChunksSource);
            GraphRuntime = new LoadedGraphRuntime(
                GraphStore,
                LoadedChunksSource,
                preferredProjectionCellSizeCm: config.ChunkSizeCells * gridCellSizeCm);
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
