using Ludots.Core.Config;
using Ludots.Core.Layers;

namespace MassNavigationMod.Runtime;

internal static class MassNavigationComponentAuthoring
{
    public static void Register()
    {
        LayerRegistry.Register(MassNavigationLayerNames.Agent);
        ComponentRegistry.Register<MassNavigationAgentTag>("MassNavigationAgentTag");
        ComponentRegistry.Register<MassNavigationControllable>("MassNavigationControllable");
        ComponentRegistry.Register<MassNavigationBlocker>("MassNavigationBlocker");
        ComponentRegistry.Register<MassNavigationHotspotMarker>("MassNavigationHotspotMarker");
        ComponentRegistry.Register<MassNavigationBlockerProfile>("MassNavigationBlockerProfile");
        ComponentRegistry.Register<MassNavigationAgentIndex>("MassNavigationAgentIndex");
        ComponentRegistry.Register<MassNavigationAgentProfile>("MassNavigationAgentProfile");
    }
}

