namespace RoadNetworkShowcaseMod.UI
{
    internal readonly record struct RoadNetworkShowcasePanelState(
        string Title,
        string Status,
        string CommandSource,
        string Input,
        string Chunks,
        string Hint,
        RoadNetworkShowcaseActorPanelState[] Actors)
    {
        public static readonly RoadNetworkShowcasePanelState Empty = new(
            "Road Network Showcase",
            "Road command ready. Right-click near a road or fort.",
            "Command source 0 | Primary <none> | Owner <none>",
            "Input ground=<none>\nInput order=<none>",
            "Chunks 0 | Nodes 0",
            "Legend: Query=command source/order, Plan=active order plus nav plan, Pick=route waypoint, Move=intent sink to nav, Check=arrival and timeout state.",
            System.Array.Empty<RoadNetworkShowcaseActorPanelState>());
    }
}
