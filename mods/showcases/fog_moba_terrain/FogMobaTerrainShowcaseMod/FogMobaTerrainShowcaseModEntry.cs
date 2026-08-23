using System.Threading.Tasks;
using FogMobaTerrainShowcaseMod.Runtime;
using FogMobaTerrainShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FogMobaTerrainShowcaseMod;

public sealed class FogMobaTerrainShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new FogMobaTerrainRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null || engine.GlobalContext.ContainsKey(FogMobaTerrainIds.InstalledKey))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[FogMobaTerrainIds.InstalledKey] = true;
            engine.GlobalContext[FogMobaTerrainIds.RuntimeServiceKey] = runtime;
            engine.RegisterSystem(new FogMobaTerrainSimulationSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new FogMobaTerrainPresentationSystem(engine, runtime));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        context.Log("[FogMobaTerrainShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
