namespace SplineSurfaceUatMod.UI
{
    internal readonly record struct SplineSurfaceUatPanelState(
        string Title,
        string Status,
        string Camera,
        string Surfaces,
        string Hint)
    {
        public static readonly SplineSurfaceUatPanelState Empty = new(
            "Spline Surface UAT",
            "Reset camera to pull road, river, lake, and raw procedural mesh back into view.",
            "Camera (0,0)",
            "Road | River | Lake | Raw Mesh",
            "Use Reset Camera for the overview, then the focus buttons to inspect each procedural surface family.");
    }
}
