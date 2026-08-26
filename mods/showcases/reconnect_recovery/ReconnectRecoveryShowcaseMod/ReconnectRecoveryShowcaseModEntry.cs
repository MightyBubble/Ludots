using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ReconnectRecoveryShowcaseMod.Runtime;
using ReconnectRecoveryShowcaseMod.Systems;

namespace ReconnectRecoveryShowcaseMod;

public sealed class ReconnectRecoveryShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new ReconnectRecoveryShowcaseRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
                (engine.GlobalContext.TryGetValue(ReconnectRecoveryShowcaseIds.InstalledKey, out object? v) && v is true))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[ReconnectRecoveryShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[ReconnectRecoveryShowcaseIds.RuntimeKey] = runtime;
            engine.RegisterSystem(new ReconnectRecoveryShowcaseInputSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new ReconnectRecoveryShowcasePresentationSystem(engine, runtime));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload() { }
}
