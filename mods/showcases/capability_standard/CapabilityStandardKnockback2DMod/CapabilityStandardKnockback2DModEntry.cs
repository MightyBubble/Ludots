using CapabilityStandardKnockback2DMod.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardKnockback2DMod;

public sealed class CapabilityStandardKnockback2DModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardKnockback2DMod] Loaded");
        CapabilityStandardKnockback2DComponentAuthoring.Register(context.ModId);
        var runtime = new CapabilityStandardKnockback2DRuntime();
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
