using Ludots.Core.Config;

namespace MassNavigationMod.Runtime;

internal static class MassNavigationComponentAuthoring
{
    public static void Register()
    {
        ComponentRegistry.Register<MassNavigationAgentTag>("MassNavigationAgentTag");
        ComponentRegistry.Register<MassNavigationControllable>("MassNavigationControllable");
        ComponentRegistry.Register<MassNavigationBlocker>("MassNavigationBlocker");
        ComponentRegistry.Register<MassNavigationHotspotMarker>("MassNavigationHotspotMarker");
        ComponentRegistry.Register<MassNavigationBlockerProfile>("MassNavigationBlockerProfile");
        ComponentRegistry.Register<MassNavigationAgentIndex>("MassNavigationAgentIndex");
        ComponentRegistry.Register<MassNavigationAgentProfile>("MassNavigationAgentProfile");
    }
}

