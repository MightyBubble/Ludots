using TimeFlowShowcaseMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace TimeFlowShowcaseMod;

public sealed class TimeFlowShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[TimeFlowShowcaseMod] Loaded");

        TimeFlowShowcaseRuntime runtime = new();
        context.OnEvent(GameEvents.GameStart, new InstallTimeFlowShowcaseOnGameStartTrigger(runtime).ExecuteAsync);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
