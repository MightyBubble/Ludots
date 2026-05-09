using Ludots.Core.Config;

namespace MassNavWebParityMod.Runtime;

internal static class MassNavWebParityComponentAuthoring
{
    public static void Register()
    {
        ComponentRegistry.Register<MassNavAgentTag>("MassNavAgentTag");
        ComponentRegistry.Register<MassNavControllable>("MassNavControllable");
        ComponentRegistry.Register<MassNavBlocker>("MassNavBlocker");
        ComponentRegistry.Register<MassNavHotspotMarker>("MassNavHotspotMarker");
        ComponentRegistry.Register<MassNavBlockerProfile>("MassNavBlockerProfile");
        ComponentRegistry.Register<MassNavAgentIndex>("MassNavAgentIndex");
        ComponentRegistry.Register<MassNavAgentProfile>("MassNavAgentProfile");
    }
}
