using GraphAiShowcaseCommon;
using Ludots.Core.Modding;

namespace GraphComplexBtShowcaseMod;

public sealed class GraphComplexBtShowcaseModEntry : IMod
{
    private const string MapId = "graph_complex_bt_showcase";
    private const string RuntimeKey = "GraphAiShowcase.ComplexBt.Runtime";

    public void OnLoad(IModContext context)
    {
        GraphAiShowcaseBootstrap.Register(
            context,
            "GraphComplexBtShowcaseMod",
            MapId,
            RuntimeKey,
            "GraphComplexBtShowcaseMod");
    }

    public void OnUnload()
    {
    }
}
