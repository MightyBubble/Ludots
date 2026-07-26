namespace DynamicNavBakeShowcaseMod.UI;

internal readonly record struct DynamicNavBakeShowcasePanelState(
    string Title,
    string Status,
    bool ConstructionMode = false,
    bool NavMeshVisible = true)
{
    public static DynamicNavBakeShowcasePanelState Empty { get; } = new(
        Title: "Dynamic NavBake",
        Status: "等待地图就绪。",
        ConstructionMode: false,
        NavMeshVisible: true);
}
