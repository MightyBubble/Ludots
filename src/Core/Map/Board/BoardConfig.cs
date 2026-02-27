namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Configuration for a single Board within a Map.
    /// Replaces the old MapSpatialConfig with per-board granularity.
    /// </summary>
    public class BoardConfig
    {
        /// <summary>Board name within the map (e.g., "default", "strategic", "battle").</summary>
        public string Name { get; set; } = "default";

        /// <summary>Spatial type: "Grid", "HexGrid", or "NodeGraph".</summary>
        public string SpatialType { get; set; } = "Grid";

        /// <summary>Board width in tiles.</summary>
        public int WidthInTiles { get; set; } = 64;

        /// <summary>Board height in tiles.</summary>
        public int HeightInTiles { get; set; } = 64;

        /// <summary>Grid cell size in centimeters.</summary>
        public int GridCellSizeCm { get; set; } = 100;

        /// <summary>Hex edge length in centimeters. Applies to HexGrid boards.</summary>
        public int HexEdgeLengthCm { get; set; } = 400;

        /// <summary>Spatial partition chunk size in cells per side. Must be a power of two.</summary>
        public int ChunkSizeCells { get; set; } = 64;

        /// <summary>Path to binary data file (.vtxm, .graph) — optional.</summary>
        public string DataFile { get; set; }

        /// <summary>Whether navigation is enabled for this board.</summary>
        public bool NavigationEnabled { get; set; }

        /// <summary>
        /// Clone this config to prevent aliasing during merge operations.
        /// </summary>
        public BoardConfig Clone()
        {
            return new BoardConfig
            {
                Name = Name,
                SpatialType = SpatialType,
                WidthInTiles = WidthInTiles,
                HeightInTiles = HeightInTiles,
                GridCellSizeCm = GridCellSizeCm,
                HexEdgeLengthCm = HexEdgeLengthCm,
                ChunkSizeCells = ChunkSizeCells,
                DataFile = DataFile,
                NavigationEnabled = NavigationEnabled
            };
        }
    }
}
