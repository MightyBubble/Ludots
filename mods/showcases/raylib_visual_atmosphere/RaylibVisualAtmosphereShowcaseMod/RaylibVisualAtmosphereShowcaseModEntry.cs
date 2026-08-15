using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RaylibVisualAtmosphereShowcaseMod.Runtime;

namespace RaylibVisualAtmosphereShowcaseMod;

public sealed class RaylibVisualAtmosphereShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[RaylibVisualAtmosphereShowcaseMod] Loaded");
        IslandTerrainGenerator.EnsureGenerated(context);
        IslandTerrainControlMapGenerator.EnsureGenerated(context);

        var runtime = new RaylibVisualAtmosphereShowcaseRuntime();
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            ctx.GetEngine()?.GlobalContext.TryAdd(RaylibVisualAtmosphereShowcaseIds.InstalledKey, true);
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
