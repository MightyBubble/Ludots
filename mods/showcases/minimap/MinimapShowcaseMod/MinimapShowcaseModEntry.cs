using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MinimapShowcaseMod.Triggers;

namespace MinimapShowcaseMod;

public sealed class MinimapShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[MinimapShowcaseMod] Loaded.");
        var trigger = new ConfigureMinimapShowcaseOnMapFocusTrigger();
        context.OnEvent(GameEvents.MapLoaded, trigger.ExecuteAsync);
        context.OnEvent(GameEvents.MapResumed, trigger.ExecuteAsync);
        context.OnEvent(GameEvents.GameStart, trigger.ExecuteAsync);
        context.OnEvent(GameEvents.MapUnloaded, trigger.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
