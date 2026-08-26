using System.Threading.Tasks;
using DeterministicReplayShowcaseMod.Runtime;
using DeterministicReplayShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace DeterministicReplayShowcaseMod;

public sealed class DeterministicReplayShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new DeterministicReplayShowcaseRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
                (engine.GlobalContext.TryGetValue(DeterministicReplayShowcaseIds.InstalledKey, out object? v) && v is true))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[DeterministicReplayShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[DeterministicReplayShowcaseIds.RuntimeKey] = runtime;
            engine.RegisterSystem(new DeterministicReplayShowcaseInputSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new DeterministicReplayShowcasePresentationSystem(engine, runtime));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload() { }
}
