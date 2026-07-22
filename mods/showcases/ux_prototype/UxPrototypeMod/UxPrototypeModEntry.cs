using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UxPrototypeMod.Runtime;
using UxPrototypeMod.Triggers;

namespace UxPrototypeMod;

public sealed class UxPrototypeModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[UxPrototypeMod] Loaded");

        var runtime = new UxPrototypeRuntime();
        context.OnEvent(GameEvents.GameStart, new InstallUxPrototypeOnGameStartTrigger(context, runtime).ExecuteAsync);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
