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
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SpatialPartition?.Clear();
        }
    }
}
