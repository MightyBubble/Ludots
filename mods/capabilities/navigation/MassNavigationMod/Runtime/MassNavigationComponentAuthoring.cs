using Ludots.Core.Config;
using Ludots.Core.Layers;

namespace MassNavigationMod.Runtime;

internal static class MassNavigationComponentAuthoring
{
    public static void Register(string modId)
    {
        LayerRegistry.Register(MassNavigationLayerNames.Agent);
        ComponentRegistry.Register<MassNavigationAgentTag>("MassNavigationAgentTag", modId);
        ComponentRegistry.Register<MassNavigationControllable>("MassNavigationControllable", modId);
        ComponentRegistry.Register<MassNavigationBlocker>("MassNavigationBlocker", modId);
        ComponentRegistry.Register<MassNavigationHotspotMarker>("MassNavigationHotspotMarker", modId);
        ComponentRegistry.Register<MassNavigationBlockerProfile>("MassNavigationBlockerProfile", modId);
        ComponentRegistry.Register<MassNavigationAgentIndex>("MassNavigationAgentIndex", modId);
        ComponentRegistry.Register<MassNavigationAgentProfile>("MassNavigationAgentProfile", modId);
    }
}

