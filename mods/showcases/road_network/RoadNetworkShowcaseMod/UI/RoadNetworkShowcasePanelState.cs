namespace RoadNetworkShowcaseMod.UI
{
    internal readonly record struct RoadNetworkShowcasePanelState(
        string Title,
        string Status,
        string Actor,
        string Profile,
        string Chunks,
        string Hint)
    {
        public static readonly RoadNetworkShowcasePanelState Empty = new(
            "Road Network Showcase",
            "Road command ready. Right-click near a road or fort.",
            "Actor <none>",
            "Route profile <none>",
            "Chunks 0 | Nodes 0",
            "RMB route | Shift queue | Home reset camera");
    }
}
