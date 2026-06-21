using System.Threading.Tasks;
using GoldMarketShowcaseMod.Runtime;
using GoldMarketShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GoldMarketShowcaseMod;

public sealed class GoldMarketShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new GoldMarketRuntime(context);

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(GoldMarketIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[GoldMarketIds.InstalledKey] = true;
            engine.GlobalContext[GoldMarketIds.RuntimeServiceKey] = runtime;
            engine.RegisterPresentationSystem(new GoldMarketPresentationSystem(engine, runtime));
            engine.RegisterSystem(new GoldMarketSimulationSystem(engine, runtime), SystemGroup.InputCollection);
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
