using CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics2DPlaygroundV2Mod;

public sealed class CapabilityStandardPhysics2DPlaygroundV2ModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        CapabilityStandardPhysics2DPlaygroundV2ComponentAuthoring.Register(context.ModId);
        context.Log("[CapabilityStandardPhysics2DPlaygroundV2Mod] Loaded");

        var runtime = new CapabilityStandardPhysics2DPlaygroundV2Runtime(context);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
