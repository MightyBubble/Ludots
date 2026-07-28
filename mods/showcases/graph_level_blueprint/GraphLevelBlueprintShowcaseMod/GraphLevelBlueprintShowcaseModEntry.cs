using GraphAiShowcaseCommon;
using Ludots.Core.Modding;

namespace GraphLevelBlueprintShowcaseMod;

public sealed class GraphLevelBlueprintShowcaseModEntry : IMod
{
    private const string MapId = "graph_level_blueprint_showcase";
    private const string RuntimeKey = "GraphAiShowcase.LevelBlueprint.Runtime";

    public void OnLoad(IModContext context)
    {
        GraphAiShowcaseBootstrap.Register(
            context,
            "GraphLevelBlueprintShowcaseMod",
            MapId,
            RuntimeKey,
            "GraphLevelBlueprintShowcaseMod");
    }

    public void OnUnload()
    {
    }
}
