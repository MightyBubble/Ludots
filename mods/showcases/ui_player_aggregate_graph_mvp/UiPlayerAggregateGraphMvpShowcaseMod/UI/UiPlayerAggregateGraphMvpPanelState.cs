using UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

namespace UiPlayerAggregateGraphMvpShowcaseMod.UI;

internal readonly record struct UiPlayerAggregateGraphMvpPanelState(
    string Title,
    string Copy,
    string Controls,
    string Status,
    string GraphId,
    UiPlayerAggregatePanelBinding OreBinding,
    UiPlayerAggregatePanelBinding CrystalBinding,
    float OreTotal,
    float CrystalTotal,
    bool BuildingShutDown,
    string ShutDownBuildingName,
    UiPlayerAggregatePanelStyle PanelStyle);
