using Ludots.Core.Map;

namespace UiPlayerAggregateGraphMvpShowcaseMod;

public static class UiPlayerAggregateGraphMvpIds
{
    public const string MapId = "ui_player_aggregate_graph_mvp";
    public static readonly MapId ShowcaseMap = new(MapId);
    public const string RuntimeServiceKey = "UiPlayerAggregateGraphMvpShowcase.Runtime";
    public const string InstalledKey = "UiPlayerAggregateGraphMvpShowcase.Installed";
    public const string ShutDownBuildingActionId = "UiPlayerAggregateGraphMvp.ShutDownBuilding";
    public const string PanelRootElementId = "ui-player-aggregate-graph-mvp-panel";
    public const string ShutDownButtonElementId = "ui-player-aggregate-graph-mvp-shutdown";

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, StringComparison.Ordinal);
    }
}
