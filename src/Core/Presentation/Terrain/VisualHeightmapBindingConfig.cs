namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Map-authored declaration for the visual terrain height truth used by presentation.
    /// </summary>
    public sealed class VisualHeightmapBindingConfig
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

        public VisualHeightmapBindingConfig Clone()
        {
            return new VisualHeightmapBindingConfig
            {
                Asset = Asset,
                BoardName = BoardName,
                DefaultLayerIndex = DefaultLayerIndex,
            };
        }
    }
}
