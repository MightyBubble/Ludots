using GraphAiShowcaseCommon;
using Ludots.Core.Modding;

namespace GraphStanceFsmShowcaseMod;

public sealed class GraphStanceFsmShowcaseModEntry : IMod
{
    private const string MapId = "graph_stance_fsm_showcase";
    private const string RuntimeKey = "GraphAiShowcase.StanceFsm.Runtime";

    public void OnLoad(IModContext context)
    {
        GraphAiShowcaseBootstrap.Register(
            context,
            "GraphStanceFsmShowcaseMod",
            MapId,
            RuntimeKey,
            "GraphStanceFsmShowcaseMod");
    }

    public void OnUnload()
    {
    }
}
