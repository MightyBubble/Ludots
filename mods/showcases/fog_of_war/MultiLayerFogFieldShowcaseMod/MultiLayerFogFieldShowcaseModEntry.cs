using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using WarFogShowcase.Shared;

namespace MultiLayerFogFieldShowcaseMod;

public sealed class MultiLayerFogFieldShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new FogShowcaseRuntime(FogShowcaseScenario.Create(
            FogShowcaseKind.MultiLayer,
            "MultiLayerFogFieldShowcaseMod"));

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(runtime.Scenario.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[runtime.Scenario.InstalledKey] = true;
            engine.GlobalContext[runtime.Scenario.RuntimeServiceKey] = runtime;
            engine.RegisterSystem(new FogShowcaseSimulationSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new FogShowcasePresentationSystem(engine, runtime));
            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        context.Log("[MultiLayerFogFieldShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
