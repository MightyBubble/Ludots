using Ludots.Core.Spatial;
using Ludots.Core.Navigation.NavMesh.Config;

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

        /// <summary>Board width in 256-cell macro tiles.</summary>
        public int WidthInMacroTiles { get; set; } = SpatialScaleDefaults.DefaultWorldWidthMacroTiles;

        /// <summary>Board height in 256-cell macro tiles.</summary>
        public int HeightInMacroTiles { get; set; } = SpatialScaleDefaults.DefaultWorldHeightMacroTiles;

        /// <summary>Grid cell size in centimeters.</summary>
        public int GridCellSizeCm { get; set; } = SpatialScaleDefaults.CellCm;

        /// <summary>Hex edge length in centimeters. Applies to HexGrid boards.</summary>
        public int HexEdgeLengthCm { get; set; } = SpatialScaleDefaults.DefaultHexEdgeLengthCm;

        /// <summary>Spatial partition chunk size in cells per side. Must be a power of two.</summary>
        public int ChunkSizeCells { get; set; } = SpatialScaleDefaults.PartitionChunkCells;

        /// <summary>Maximum simultaneously loaded graph chunks. Required for NodeGraph boards.</summary>
        public int LoadedChunkCapacity { get; set; }

        /// <summary>Path to binary data file (.vtxm, .graph) — optional.</summary>
        public string DataFile { get; set; }

        public string VisualHeightmapAsset { get; set; }

        public string StructureCollisionAsset { get; set; } = string.Empty;

        public bool StructureAwareGrounding { get; set; }

        public bool StructureAwareNavigation { get; set; }

        /// <summary>Whether navigation is enabled for this board.</summary>
        public bool NavigationEnabled { get; set; }

        /// <summary>
        /// VisualHeightmap → LogicTerrain 投影的高度量化步长（cm）。0 = 引擎默认
        /// （SpatialScaleDefaults.CellCm）。起伏地图用细步长（如 25）可避免
        /// 粗量化把缓坡切成不可通行的陡崖，navmesh 高度语义需与烘焙
        /// heightScaleMeters（米/高度层）= 步长/100 保持一致。
        /// </summary>
        public int TerrainHeightStepCm { get; set; }

        /// <summary>Explicit nav tile grid for this board (authored with the bake).
        /// Required when the board participates in navmesh; runtime reads this declaration only.</summary>
        public NavTileGridConfig NavTileGrid { get; set; }

        /// <summary>
        /// Clone this config to prevent aliasing during merge operations.
        /// </summary>
        public BoardConfig Clone()
        {
            return new BoardConfig
            {
                Name = Name,
                SpatialType = SpatialType,
                WidthInMacroTiles = WidthInMacroTiles,
                HeightInMacroTiles = HeightInMacroTiles,
                GridCellSizeCm = GridCellSizeCm,
                HexEdgeLengthCm = HexEdgeLengthCm,
                ChunkSizeCells = ChunkSizeCells,
                LoadedChunkCapacity = LoadedChunkCapacity,
                DataFile = DataFile,
                VisualHeightmapAsset = VisualHeightmapAsset,
                StructureCollisionAsset = StructureCollisionAsset,
                StructureAwareGrounding = StructureAwareGrounding,
                StructureAwareNavigation = StructureAwareNavigation,
                NavigationEnabled = NavigationEnabled,
                TerrainHeightStepCm = TerrainHeightStepCm,
                NavTileGrid = NavTileGrid?.Clone()
            };
        }
    }
}
