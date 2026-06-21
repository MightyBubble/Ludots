using System;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Simple grid-based Board. The default board type for most maps.
    /// </summary>
    public sealed class GridBoard : IBoard
    {
        public BoardId Id { get; }
        public string Name { get; }
        public WorldSizeSpec WorldSize { get; }
        public ISpatialCoordinateConverter CoordinateConverter { get; }
        public ISpatialPartitionWorld SpatialPartition { get; }
        public ISpatialQueryService QueryService { get; }
        public ILoadedChunks LoadedChunks => null;

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

            var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: config.ChunkSizeCells);
            SpatialPartition = partition;
            QueryService = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, WorldSize));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SpatialPartition?.Clear();
        }
    }
}
