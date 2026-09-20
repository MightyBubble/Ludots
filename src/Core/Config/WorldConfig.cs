using Ludots.Core.Spatial;

namespace Ludots.Core.Config
{
    /// <summary>
    /// Engine-boot world declaration: the pre-map-load placeholder replaced on map
    /// load by the root board's frame. Macro-tile counts are derived from these
    /// centimeter values, never authored.
    /// </summary>
    public class WorldConfig
    {
        public int WidthCm { get; set; }

        public int HeightCm { get; set; }

        public int CellSizeCm { get; set; } = SpatialScaleDefaults.CellCm;

        public WorldConfig Clone() => new()
        {
            WidthCm = WidthCm,
            HeightCm = HeightCm,
            CellSizeCm = CellSizeCm
        };

        public static WorldConfig CreateEngineBootDefault() => new()
        {
            WidthCm = SpatialScaleDefaults.DefaultBoardWidthPages * SpatialScaleDefaults.TerrainPageCells * SpatialScaleDefaults.CellCm,
            HeightCm = SpatialScaleDefaults.DefaultBoardHeightPages * SpatialScaleDefaults.TerrainPageCells * SpatialScaleDefaults.CellCm,
            CellSizeCm = SpatialScaleDefaults.CellCm
        };
    }
}
