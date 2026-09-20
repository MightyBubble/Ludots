namespace UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

public readonly record struct UiPlayerAggregateGraphMvpSnapshot(
    string Title,
    string Copy,
    string Controls,
    string Status,
    string GraphId,
    string OreSummaryKey,
    string CrystalSummaryKey,
    float OreTotal,
    float CrystalTotal,
    bool BuildingShutDown,
    string ShutDownBuildingName);
