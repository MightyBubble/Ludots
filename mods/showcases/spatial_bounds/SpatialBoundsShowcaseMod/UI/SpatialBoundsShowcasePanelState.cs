namespace SpatialBoundsShowcaseMod.UI
{
    internal readonly record struct SpatialBoundsShowcasePanelState(
        string Title,
        string Camera,
        string Hint)
    {
        public static readonly SpatialBoundsShowcasePanelState Empty = new(
            "Spatial Bounds",
            "Camera (0,0)  Dist 0",
            "Use Reset Camera to recenter the showcase.");
    }
}
