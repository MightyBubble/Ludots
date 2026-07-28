using GraphAiShowcaseCommon;
using Ludots.Core.Modding;

namespace GraphStressFieldShowcaseMod;

public sealed class GraphStressFieldShowcaseModEntry : IMod
{
    private const string MapId = "graph_stress_field_showcase";
    private const string RuntimeKey = "GraphAiShowcase.StressField.Runtime";

    public void OnLoad(IModContext context)
    {
        GraphAiShowcaseBootstrap.Register(
            context,
            "GraphStressFieldShowcaseMod",
            MapId,
            RuntimeKey,
            "GraphStressFieldShowcaseMod");
    }

    public void OnUnload()
    {
    }
}
