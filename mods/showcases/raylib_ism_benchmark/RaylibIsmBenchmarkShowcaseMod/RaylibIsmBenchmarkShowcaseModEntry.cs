using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RaylibIsmBenchmarkShowcaseMod.Runtime;

namespace RaylibIsmBenchmarkShowcaseMod;

public sealed class RaylibIsmBenchmarkShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[RaylibIsmBenchmarkShowcaseMod] Loaded");
        var runtime = new RaylibIsmBenchmarkShowcaseRuntime();

        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            ctx.GetEngine()?.GlobalContext.TryAdd(RaylibIsmBenchmarkShowcaseIds.InstalledKey, true);
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
