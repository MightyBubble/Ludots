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
        var replication = new FrontlineReplicationLifecycle(runtime);
        context.OnEvent(GameEvents.GameStart, runtime.HandleGameStartAsync);
        context.OnEvent(GameEvents.GameStart, replication.HandleGameStartAsync);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapLoaded, replication.HandleMapLoadedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, replication.HandleMapResumedAsync);
        context.OnEvent(GameEvents.MapUnloaded, replication.HandleMapUnloadedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        context.Log("[RtsMultiplayerFrontlineMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
