using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using SaveLoadShowcaseMod.Runtime;
using SaveLoadShowcaseMod.Systems;

namespace SaveLoadShowcaseMod;

public sealed class SaveLoadShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new SaveLoadShowcaseRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
                (engine.GlobalContext.TryGetValue(SaveLoadShowcaseIds.InstalledKey, out object? v) && v is true))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[SaveLoadShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[SaveLoadShowcaseIds.RuntimeKey] = runtime;
            engine.RegisterSystem(new SaveLoadShowcaseInputSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new SaveLoadShowcasePresentationSystem(engine, runtime));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload() { }
}
