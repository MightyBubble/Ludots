using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphSemantics.GAS
{
    public static class GraphAttributeSinks
    {
        public static void RegisterBuiltins(AttributeSinkRegistry sinks, GraphEdgeCostOverlay edgeOverlay)
        {
            sinks.Register(GraphSinkNames.EdgeCostOverlay, new GraphEdgeCostOverlaySink(edgeOverlay));
        }
    }
}

