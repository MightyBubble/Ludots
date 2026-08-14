using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RtsMultiplayerFrontlineMod.Runtime;

namespace RtsMultiplayerFrontlineMod;

public sealed class RtsMultiplayerFrontlineModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        FrontlineComponentAuthoring.Register(context.ModId);
        var runtime = new FrontlineRuntime(context);
        context.OnEvent(GameEvents.GameStart, runtime.HandleGameStartAsync);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        context.Log("[RtsMultiplayerFrontlineMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
