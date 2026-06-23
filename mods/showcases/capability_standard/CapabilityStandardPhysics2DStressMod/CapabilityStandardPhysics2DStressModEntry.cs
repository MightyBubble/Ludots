using CapabilityStandardPhysics2DStressMod.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics2DStressMod;

public sealed class CapabilityStandardPhysics2DStressModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardPhysics2DStressMod] Loaded");
        CapabilityStandardPhysics2DStressComponentAuthoring.Register(context.ModId);
        var runtime = new CapabilityStandardPhysics2DStressRuntime();
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
