using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Map-authored declaration for the visual terrain height truth used by presentation.
    /// </summary>
    public sealed class ContinuousHeightmapBindingConfig
    {
        /// <summary>
        /// Binary visual heightmap asset path. Resolved through the existing VFS map asset search path.
        /// </summary>
        public string Asset { get; set; } = string.Empty;

        /// <summary>
        /// Optional board name this binding belongs to. Empty means the map-level default.
        /// </summary>
        public string BoardName { get; set; } = string.Empty;

        /// <summary>
        /// Layer index used by consumers that request the default visual terrain layer.
        /// </summary>
        public int DefaultLayerIndex { get; set; } = -1;

        /// <summary>
        /// When &gt; 0, remaps the asset's world AABB to this width (cm), preserving aspect ratio and center.
        /// Authored samples stay the same; only horizontal playable meters change (e.g. continental → 64km board).
        /// </summary>
        public int WorldWidthCm { get; set; }

        /// <summary>
        /// Presentation-only render profile for adapters. This never changes sampling truth.
        /// </summary>
        public ContinuousHeightmapRenderProfile RenderProfile { get; set; } = ContinuousHeightmapRenderProfile.CreateDefault();

        public ContinuousHeightmapBindingConfig Clone()
        {
            return new ContinuousHeightmapBindingConfig
            {
                Asset = Asset,
                BoardName = BoardName,
                DefaultLayerIndex = DefaultLayerIndex,
                WorldWidthCm = WorldWidthCm,
                RenderProfile = (RenderProfile ?? ContinuousHeightmapRenderProfile.CreateDefault()).NormalizeAndValidate(),
            };
        }
    }
}
