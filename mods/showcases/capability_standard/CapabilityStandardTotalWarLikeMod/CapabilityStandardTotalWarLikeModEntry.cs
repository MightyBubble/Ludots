using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CapabilityStandardTotalWarLikeMod.Runtime;

namespace CapabilityStandardTotalWarLikeMod;

public sealed class CapabilityStandardTotalWarLikeModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardTotalWarLikeMod] Loaded");
        CapabilityStandardTotalWarLikeComponentAuthoring.Register(context.ModId);
        var runtime = new CapabilityStandardTotalWarLikeRuntime();
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
