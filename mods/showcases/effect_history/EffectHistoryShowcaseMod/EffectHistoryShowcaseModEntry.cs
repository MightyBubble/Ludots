using System.Threading.Tasks;
using EffectHistoryShowcaseMod.Runtime;
using EffectHistoryShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace EffectHistoryShowcaseMod;

public sealed class EffectHistoryShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new EffectHistoryShowcaseRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null || (engine.GlobalContext.TryGetValue(EffectHistoryShowcaseIds.InstalledKey, out object? value) && value is true))
                return Task.CompletedTask;

            engine.GlobalContext[EffectHistoryShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[EffectHistoryShowcaseIds.RuntimeKey] = runtime;
            engine.RegisterSystem(new EffectHistoryShowcaseSimulationSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new EffectHistoryShowcasePresentationSystem(engine, runtime));
            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapLoadedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapLoadedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
