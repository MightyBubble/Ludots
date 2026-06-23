using System.Threading.Tasks;
using DiplomacyTradeGateShowcaseMod.Runtime;
using DiplomacyTradeGateShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace DiplomacyTradeGateShowcaseMod;

public sealed class DiplomacyTradeGateShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new DiplomacyTradeGateRuntime(context);

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(DiplomacyTradeGateIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[DiplomacyTradeGateIds.InstalledKey] = true;
            engine.GlobalContext[DiplomacyTradeGateIds.RuntimeServiceKey] = runtime;
            engine.RegisterPresentationSystem(new DiplomacyTradeGatePresentationSystem(engine, runtime));
            engine.RegisterSystem(new DiplomacyTradeGateSimulationSystem(engine, runtime), SystemGroup.InputCollection);
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
