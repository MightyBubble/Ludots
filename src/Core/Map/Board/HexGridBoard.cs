using System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.AOI;
using Ludots.Core.Navigation.Terrain;
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
        public BoardExtentSpec BoardExtent { get; }
        public WorldSizeSpec WorldSize { get; }
        public ISpatialCoordinateConverter CoordinateConverter { get; }
        public ISpatialPartitionWorld SpatialPartition => Partition;
        public ISpatialQueryService QueryService => QueryServiceInstance;
        public ILoadedChunks LoadedChunks => HexGridAOI;
        public VertexMap VertexMap { get; set; }
        public LogicTerrainField LogicTerrain { get; set; }
        public Navigation.NavMesh.NavQueryServiceRegistry NavServices { get; set; }

        public HexMetrics HexMetrics { get; }

        // HexGridAOI doubles as the loaded-chunks source and is cheap; the partition and
        // the hex-aware query service stay unallocated until first access (satellite boards
        // carry no consumers until BoardRef dispatch lands, #1567 slice 2).
        public HexGridAOI HexGridAOI { get; } = new();
        private ChunkedGridSpatialPartitionWorld? _partition;
        private SpatialQueryService? _queryService;

        private ChunkedGridSpatialPartitionWorld Partition =>
            _partition ??= new ChunkedGridSpatialPartitionWorld(chunkSizeCells: _chunkSizeCells);

        private SpatialQueryService QueryServiceInstance
        {
            get
            {
                if (_queryService == null)
                {
                    _queryService = new SpatialQueryService(
                        new ChunkedGridSpatialPartitionBackend(Partition, WorldSize));
                    _queryService.SetHexMetrics(HexMetrics);
                    _queryService.SetCoordinateConverter(CoordinateConverter);
                }
                return _queryService;
            }
        }

        private readonly int _chunkSizeCells;
        private bool _disposed;

        public HexGridBoard(BoardId id, string name, BoardConfig config)
        {
            Id = id;
            Name = name;

            BoardExtent = config.ResolveExtent();
            WorldSize = BoardExtent.ToWorldSizeSpec();
            CoordinateConverter = new SpatialCoordinateConverter(
                config.GridCellSizeCm,
                BoardExtent.TopologyOriginXCm,
                BoardExtent.TopologyOriginYCm);
            HexMetrics = new HexMetrics(config.HexEdgeLengthCm);
            _chunkSizeCells = config.ChunkSizeCells;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            VertexMap?.UnsubscribeFromLoadedChunks();
            HexGridAOI?.Reset();
            _partition?.Clear();
        }
    }
}
