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

        /// <summary>Board width in topology cells (#1567: authored directly; the root board anchors the host world.</summary>
        public int WidthCells { get; set; } = SpatialScaleDefaults.DefaultBoardWidthPages * SpatialScaleDefaults.TerrainPageCells;

        /// <summary>Board height in topology cells (#1567: authored directly; the root board anchors the host world.</summary>
        public int HeightCells { get; set; } = SpatialScaleDefaults.DefaultBoardHeightPages * SpatialScaleDefaults.TerrainPageCells;

        /// <summary>Board AABB min-corner anchor in world coordinates, X axis; null = centered on the world (#1567 slice 2). Same anchor semantics as NavTileGridConfig.OriginXcm.</summary>
        public int? OriginXCm { get; set; }

        /// <summary>Board AABB min-corner anchor in world coordinates, Y axis; null = centered on the world. Both axes must be authored together.</summary>
        public int? OriginYcm { get; set; }

        /// <summary>Grid cell size in centimeters.</summary>
        public int GridCellSizeCm { get; set; } = SpatialScaleDefaults.CellCm;

        /// <summary>Hex edge length in centimeters. Applies to HexGrid boards.</summary>
        public int HexEdgeLengthCm { get; set; } = SpatialScaleDefaults.DefaultHexEdgeLengthCm;

        /// <summary>Board width in hexes; HexGrid-only authoring, takes precedence over
        /// WidthCells when authored on both axes (#1567 slice 2 hex metric). Non-square with
        /// HeightHexes is fine; the world AABB is the conservative hex footprint.</summary>
        public int? WidthHexes { get; set; }

        /// <summary>Board height in hexes; must be authored together with WidthHexes.</summary>
        public int? HeightHexes { get; set; }

        /// <summary>Spatial partition chunk size in cells per side. Runtime only: populated from
        /// the map's Tuning (#1567); JSON authoring lives on map Tuning.PartitionChunkCells.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int ChunkSizeCells { get; set; } = SpatialScaleDefaults.PartitionChunkCells;

        /// <summary>Maximum simultaneously loaded chunks. Runtime only: populated from the map's
        /// Tuning (#1567); JSON authoring lives on map Tuning.LoadedChunkCapacity.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int LoadedChunkCapacity { get; set;}

        /// <summary>Path to binary data file (.hex, .graph) — optional.</summary>
        public string DataFile { get; set; }

        public string ContinuousHeightmapAsset { get; set; }

        public string StructureCollisionAsset { get; set; } = string.Empty;

        public bool StructureAwareGrounding { get; set; }

        public bool StructureAwareNavigation { get; set; }

        /// <summary>
        /// ContinuousHeightmap → LogicTerrain 投影的高度量化步长（cm）。0 = 引擎默认
        /// （SpatialScaleDefaults.CellCm）。起伏地图用细步长（如 25）可避免
        /// 粗量化把缓坡切成不可通行的陡崖，navmesh 高度语义需与烘焙
        /// heightScaleMeters（米/高度层）= 步长/100 保持一致。
        /// </summary>
        public int TerrainHeightStepCm { get; set; }

        /// <summary>
        /// Marks continuous-heightmap projection output as ramp surface rather than discrete-step
        /// floor. Set this for relief maps whose slopes are meant to be judged by agent
        /// <c>maxSlopeDeg</c>: without it, a slope spanning more than one
        /// <see cref="TerrainHeightStepCm"/> step is treated as a cliff and the slope limit never
        /// applies.
        /// </summary>
        public bool TerrainProjectAsRamp { get; set; }

        public int? TerrainBlockedAtOrBelowHeightCm { get; set; }


        /// <summary>
        /// The board's effective world extent — hex footprint when WidthHexes/HeightHexes
        /// are authored (HexGrid-only, take precedence), cell grid otherwise. Single
        /// source for placement validation, board construction, and world/nav derivation.
        /// </summary>
        public Ludots.Core.Spatial.BoardExtentSpec ResolveExtent()
        {
            if (WidthHexes is int widthHexes && HeightHexes is int heightHexes)
            {
                var metrics = new Ludots.Core.Map.Hex.HexMetrics(HexEdgeLengthCm);
                (int widthCm, int heightCm) = metrics.FootprintWorldCm(widthHexes, heightHexes);
                return Ludots.Core.Spatial.BoardExtentSpec.FromConservativeCm(
                    widthCm, heightCm, GridCellSizeCm, OriginXCm, OriginYcm);
            }

            return new Ludots.Core.Spatial.BoardExtentSpec(
                WidthCells, HeightCells, GridCellSizeCm, OriginXCm, OriginYcm);
        }

        /// <summary>
        /// Clone this config to prevent aliasing during merge operations.
        /// </summary>
        public BoardConfig Clone()
        {
            return new BoardConfig
            {
                Name = Name,
                SpatialType = SpatialType,
                WidthCells = WidthCells,
                HeightCells = HeightCells,
                OriginXCm = OriginXCm,
                OriginYcm = OriginYcm,
                GridCellSizeCm = GridCellSizeCm,
                HexEdgeLengthCm = HexEdgeLengthCm,
                WidthHexes = WidthHexes,
                HeightHexes = HeightHexes,
                ChunkSizeCells = ChunkSizeCells,
                LoadedChunkCapacity = LoadedChunkCapacity,
                DataFile = DataFile,
                ContinuousHeightmapAsset = ContinuousHeightmapAsset,
                StructureCollisionAsset = StructureCollisionAsset,
                StructureAwareGrounding = StructureAwareGrounding,
                TerrainHeightStepCm = TerrainHeightStepCm,
                TerrainProjectAsRamp = TerrainProjectAsRamp,
                TerrainBlockedAtOrBelowHeightCm = TerrainBlockedAtOrBelowHeightCm
            };
        }
    }
}
