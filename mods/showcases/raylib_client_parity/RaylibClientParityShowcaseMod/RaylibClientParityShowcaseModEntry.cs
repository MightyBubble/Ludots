using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RaylibClientParityShowcaseMod.Runtime;

namespace RaylibClientParityShowcaseMod;

public sealed class RaylibClientParityShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[RaylibClientParityShowcaseMod] Loaded");
        var runtime = new RaylibClientParityShowcaseRuntime();

        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            ctx.GetEngine()?.GlobalContext.TryAdd(RaylibClientParityShowcaseIds.InstalledKey, true);
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
