using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MassNavigationTotalWarEntryMod.Runtime;

namespace MassNavigationTotalWarEntryMod;

public sealed class MassNavigationTotalWarEntryModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[MassNavigationTotalWarEntryMod] Loaded");
        TotalWarShowcaseComponentAuthoring.Register(context.ModId);
        var runtime = new TotalWarShowcaseRuntime();
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
