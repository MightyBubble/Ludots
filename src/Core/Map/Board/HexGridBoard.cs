using System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.AOI;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// HexGrid-based Board. Supports terrain (VertexMap) and navigation.
    /// </summary>
    public sealed class HexGridBoard : ITerrainBoard, INavigableBoard
    {
        public BoardId Id { get; }
        public string Name { get; }
        public WorldSizeSpec WorldSize { get; }
        public ISpatialCoordinateConverter CoordinateConverter { get; }
        public ISpatialPartitionWorld SpatialPartition { get; }
        public ISpatialQueryService QueryService { get; }
        public ILoadedChunks LoadedChunks => HexGridAOI;
        public VertexMap VertexMap { get; set; }
        public Navigation.NavMesh.NavQueryServiceRegistry NavServices { get; set; }

        public HexGridAOI HexGridAOI { get; }
        public HexMetrics HexMetrics { get; }

        private bool _disposed;

        public HexGridBoard(BoardId id, string name, BoardConfig config)
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
            var queryService = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, WorldSize));

            var hexMetrics = new HexMetrics(config.HexEdgeLengthCm);
            HexMetrics = hexMetrics;
            queryService.SetHexMetrics(hexMetrics);
            queryService.SetCoordinateConverter(CoordinateConverter);
            QueryService = queryService;

            HexGridAOI = new HexGridAOI();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            VertexMap?.UnsubscribeFromLoadedChunks();
            HexGridAOI?.Reset();
            SpatialPartition?.Clear();
        }
    }
}
