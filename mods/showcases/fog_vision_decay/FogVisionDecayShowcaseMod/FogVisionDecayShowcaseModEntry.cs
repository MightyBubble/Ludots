using System.Threading.Tasks;
using FogVisionDecayShowcaseMod.Runtime;
using FogVisionDecayShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FogVisionDecayShowcaseMod;

public sealed class FogVisionDecayShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new FogVisionDecayShowcaseRuntime();

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(FogVisionDecayIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[FogVisionDecayIds.InstalledKey] = true;
            engine.GlobalContext[FogVisionDecayIds.RuntimeServiceKey] = runtime;
            engine.RegisterSystem(new FogVisionDecaySimulationSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new FogVisionDecayPresentationSystem(engine, runtime));
            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
