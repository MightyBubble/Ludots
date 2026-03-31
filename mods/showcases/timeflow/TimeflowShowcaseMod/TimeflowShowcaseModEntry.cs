using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using TimeflowShowcaseMod.Runtime;
using TimeflowShowcaseMod.Triggers;

namespace TimeflowShowcaseMod;

public sealed class TimeflowShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[TimeflowShowcaseMod] Loaded");

        var runtime = new TimeflowShowcaseRuntime();
        context.OnEvent(GameEvents.GameStart, new InstallTimeflowShowcaseOnGameStartTrigger(context, runtime).ExecuteAsync);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
