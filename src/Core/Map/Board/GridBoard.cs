using System;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.NavMesh;
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
        public WorldSizeSpec WorldSize { get; }
        public ISpatialCoordinateConverter CoordinateConverter { get; }
        public ISpatialPartitionWorld SpatialPartition { get; }
        public ISpatialQueryService QueryService { get; }
        public ILoadedChunks LoadedChunks => LoadedChunksSource;
        public WorldGridLoadedChunks LoadedChunksSource { get; }
        public VertexMap VertexMap { get; set; }
        public LogicTerrainField LogicTerrain { get; set; }
        public NavQueryServiceRegistry NavServices { get; set; }
        public int GridCellSizeCm { get; }
        public int ChunkSizeCells { get; }

        private bool _disposed;

        public GridBoard(BoardId id, string name, BoardConfig config)
        {
            Id = id;
            Name = name;

            var worldExtent = new WorldExtentSpec(
                config.WidthInMacroTiles,
                config.HeightInMacroTiles,
                config.GridCellSizeCm);
            WorldSize = worldExtent.ToWorldSizeSpec();
            CoordinateConverter = new SpatialCoordinateConverter(WorldSize);
            GridCellSizeCm = config.GridCellSizeCm;
            ChunkSizeCells = config.ChunkSizeCells;

            var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: config.ChunkSizeCells);
            SpatialPartition = partition;
            QueryService = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, WorldSize));
            LoadedChunksSource = new WorldGridLoadedChunks(config.ChunkSizeCells * config.GridCellSizeCm);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            LoadedChunksSource.Reset();
            SpatialPartition?.Clear();
        }
    }
}
