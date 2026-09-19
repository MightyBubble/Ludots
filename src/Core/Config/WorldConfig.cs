using Ludots.Core.Spatial;

namespace Ludots.Core.Config
{
    /// <summary>
    /// World declaration: the single authority for world size. Map JSON carries the
    /// authored world; the engine boot config carries the pre-map-load default.
    /// Macro-tile counts are derived from these centimeter values, never authored.
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
            WidthCm = SpatialScaleDefaults.DefaultWorldWidthMacroTiles * SpatialScaleDefaults.MacroTileCells * SpatialScaleDefaults.CellCm,
            HeightCm = SpatialScaleDefaults.DefaultWorldHeightMacroTiles * SpatialScaleDefaults.MacroTileCells * SpatialScaleDefaults.CellCm,
            CellSizeCm = SpatialScaleDefaults.CellCm
        };
    }
}
