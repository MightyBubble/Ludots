using System.Text.Json.Serialization;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Where one point on the board sits in the Ludots world.
    /// Local centimeters are measured from the corner where cells start.
    /// World centimeters are Ludots host-world coordinates. The root board's world anchor is (0, 0).
    /// </summary>
    public sealed class BoardAnchor
    {
        public int LocalXCm { get; set; }
        public int LocalYCm { get; set; }
        public int WorldXCm { get; set; }
        public int WorldYCm { get; set; }

        public BoardAnchor Clone()
        {
            return new BoardAnchor
            {
                LocalXCm = LocalXCm,
                LocalYCm = LocalYCm,
                WorldXCm = WorldXCm,
                WorldYCm = WorldYCm,
            };
        }
    }

    /// <summary>Square-cell size for a board. Cell counts are derived from the centimeter rectangle.</summary>
    public sealed class BoardGridAuthoring
    {
        public int CellSizeCm { get; set; } = SpatialScaleDefaults.CellCm;

        public BoardGridAuthoring Clone() => new() { CellSizeCm = CellSizeCm };
    }

    /// <summary>Hex edge length. How many hexes fit is derived from the centimeter rectangle.</summary>
    public sealed class BoardHexAuthoring
    {
        public int EdgeLengthCm { get; set; } = SpatialScaleDefaults.DefaultHexEdgeLengthCm;

        public BoardHexAuthoring Clone() => new() { EdgeLengthCm = EdgeLengthCm };
    }

    /// <summary>
    /// Configuration for a single Board within a Map.
    /// </summary>
    public class BoardConfig
    {
        public string Name { get; set; } = "default";

        /// <summary>Spatial type: "Grid", "HexGrid", or "NodeGraph".</summary>
        public string SpatialType { get; set; } = "Grid";

        public int WidthCm { get; set; } = SpatialScaleDefaults.DefaultBoardWidthPages * SpatialScaleDefaults.TerrainPageCells * SpatialScaleDefaults.CellCm;

        public int HeightCm { get; set; } = SpatialScaleDefaults.DefaultBoardHeightPages * SpatialScaleDefaults.TerrainPageCells * SpatialScaleDefaults.CellCm;

        public BoardAnchor Anchor { get; set; } = new();

        public BoardGridAuthoring Grid { get; set; } = new();

        public BoardHexAuthoring Hex { get; set; }

        public string DataFile { get; set; }

        public string ContinuousHeightmapAsset { get; set; }

        public string StructureCollisionAsset { get; set; } = string.Empty;

        public bool StructureAwareGrounding { get; set; }

        public bool StructureAwareNavigation { get; set; }

        public int TerrainHeightStepCm { get; set; }

        public int? TerrainBlockedAtOrBelowHeightCm { get; set; }

        [JsonIgnore]
        public int ChunkSizeCells { get; set; } = SpatialScaleDefaults.PartitionChunkCells;

        [JsonIgnore]
        public int LoadedChunkCapacity { get; set; }

        [JsonIgnore]
        public int GridCellSizeCm => Grid != null && Grid.CellSizeCm > 0 ? Grid.CellSizeCm : 0;

        [JsonIgnore]
        public int WidthCells => GridCellSizeCm > 0 ? WidthCm / GridCellSizeCm : 0;

        [JsonIgnore]
        public int HeightCells => GridCellSizeCm > 0 ? HeightCm / GridCellSizeCm : 0;

        [JsonIgnore]
        public int HexEdgeLengthCm => Hex != null && Hex.EdgeLengthCm > 0
            ? Hex.EdgeLengthCm
            : SpatialScaleDefaults.DefaultHexEdgeLengthCm;

        [JsonIgnore]
        public int TopologyOriginXCm => (Anchor?.WorldXCm ?? 0) - (Anchor?.LocalXCm ?? 0);

        [JsonIgnore]
        public int TopologyOriginYCm => (Anchor?.WorldYCm ?? 0) - (Anchor?.LocalYCm ?? 0);

        public BoardExtentSpec ResolveExtent()
        {
            return new BoardExtentSpec(WidthCm, HeightCm, GridCellSizeCm, TopologyOriginXCm, TopologyOriginYCm);
        }

        public BoardConfig Clone()
        {
            return new BoardConfig
            {
                Name = Name,
                SpatialType = SpatialType,
                WidthCm = WidthCm,
                HeightCm = HeightCm,
                Anchor = Anchor?.Clone() ?? new BoardAnchor(),
                Grid = Grid?.Clone() ?? new BoardGridAuthoring(),
                Hex = Hex?.Clone(),
                ChunkSizeCells = ChunkSizeCells,
                LoadedChunkCapacity = LoadedChunkCapacity,
                DataFile = DataFile,
                ContinuousHeightmapAsset = ContinuousHeightmapAsset,
                StructureCollisionAsset = StructureCollisionAsset,
                StructureAwareGrounding = StructureAwareGrounding,
                StructureAwareNavigation = StructureAwareNavigation,
                TerrainHeightStepCm = TerrainHeightStepCm,
                TerrainBlockedAtOrBelowHeightCm = TerrainBlockedAtOrBelowHeightCm,
            };
        }
    }
}
