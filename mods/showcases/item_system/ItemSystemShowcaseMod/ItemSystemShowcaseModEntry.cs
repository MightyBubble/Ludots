using System.Threading.Tasks;
using ItemSystemShowcaseMod.Runtime;
using ItemSystemShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ItemSystemShowcaseMod;

public sealed class ItemSystemShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new ItemSystemShowcaseRuntime();

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            var engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(ItemSystemShowcaseIds.InstalledKey, out var installed) &&
                installed is bool isInstalled &&
                isInstalled)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[ItemSystemShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[ItemSystemShowcaseIds.RuntimeKey] = runtime;
            engine.RegisterPresentationSystem(new ItemSystemShowcasePresentationSystem(engine, runtime));
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
