using CapabilityStandardTransportNetworkMod.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardTransportNetworkMod;

public sealed class CapabilityStandardTransportNetworkModEntry : IMod
{
    private readonly CapabilityStandardTransportNetworkRuntime _runtime = new();

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardTransportNetworkMod] Loaded");
        context.OnEvent(GameEvents.MapLoaded, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, _runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
        _runtime.Dispose();
    }
}
