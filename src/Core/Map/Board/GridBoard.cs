using System;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Simple grid-based Board. The default board type for most maps.
    /// </summary>
    public sealed class GridBoard : ITerrainBoard, INavigableBoard
    {
        public BoardId Id { get; }
        public string Name { get; }
        public BoardExtentSpec BoardExtent { get; }
        public WorldSizeSpec WorldSize { get; }
        public ISpatialCoordinateConverter CoordinateConverter { get; }
        public ISpatialPartitionWorld SpatialPartition => Partition;
        public ISpatialQueryService QueryService => QueryServiceInstance;
        public ILoadedChunks LoadedChunks => LoadedChunksSource;
        public VertexMap VertexMap { get; set; }
        public LogicTerrainField LogicTerrain { get; set; }
        public NavQueryServiceRegistry NavServices { get; set; }
        public int GridCellSizeCm { get; }
        public int ChunkSizeCells { get; }

        // Satellite boards carry no consumers until BoardRef dispatch lands (#1567 slice 2):
        // partition/streaming/query state stays unallocated until first access.
        public WorldGridLoadedChunks LoadedChunksSource => _loadedChunks ??= new WorldGridLoadedChunks(
            ChunkSizeCells * GridCellSizeCm, _loadedChunkCapacity);
        private ChunkedGridSpatialPartitionWorld? _partition;
        private SpatialQueryService? _queryService;
        private WorldGridLoadedChunks? _loadedChunks;
        private readonly int _loadedChunkCapacity;

        private ChunkedGridSpatialPartitionWorld Partition =>
            _partition ??= new ChunkedGridSpatialPartitionWorld(chunkSizeCells: ChunkSizeCells);

        private SpatialQueryService QueryServiceInstance =>
            _queryService ??= new SpatialQueryService(
                new ChunkedGridSpatialPartitionBackend(Partition, WorldSize));

        private bool _disposed;

        public GridBoard(BoardId id, string name, BoardConfig config)
        {
            Id = id;
            Name = name;

            BoardExtent = config.ResolveExtent();
            WorldSize = BoardExtent.ToWorldSizeSpec();
            CoordinateConverter = new SpatialCoordinateConverter(
                config.GridCellSizeCm,
                BoardExtent.TopologyOriginXCm,
                BoardExtent.TopologyOriginYCm);
            GridCellSizeCm = config.GridCellSizeCm;
            ChunkSizeCells = config.ChunkSizeCells;
            _loadedChunkCapacity = config.LoadedChunkCapacity;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _partition?.Clear();
            _loadedChunks?.Reset();
        }
    }
}
