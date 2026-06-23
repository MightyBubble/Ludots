using CapabilityStandardPhysics2DMod.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics2DMod;

public sealed class CapabilityStandardPhysics2DModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardPhysics2DMod] Loaded");
        CapabilityStandardPhysics2DComponentAuthoring.Register(context.ModId);
        var runtime = new CapabilityStandardPhysics2DRuntime();
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
