namespace RoadNetworkShowcaseMod.UI
{
    internal readonly record struct RoadNetworkShowcaseActorPanelState(
        string Header,
        string Queue,
        string Query,
        string Plan,
        string Pick,
        string Execute,
        string Check,
        string Path);
}
