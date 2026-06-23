using CapabilityStandardNavSink2DMod.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardNavSink2DMod;

public sealed class CapabilityStandardNavSink2DModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardNavSink2DMod] Loaded");
        var runtime = new CapabilityStandardNavSink2DRuntime();
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
