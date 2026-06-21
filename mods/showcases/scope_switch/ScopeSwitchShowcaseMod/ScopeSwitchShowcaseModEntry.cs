using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ScopeSwitchShowcaseMod.Runtime;
using ScopeSwitchShowcaseMod.Systems;

namespace ScopeSwitchShowcaseMod;

public sealed class ScopeSwitchShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new ScopeSwitchRuntime(context);

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            var engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(ScopeSwitchIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[ScopeSwitchIds.InstalledKey] = true;
            engine.GlobalContext[ScopeSwitchIds.RuntimeServiceKey] = runtime;
            engine.RegisterPresentationSystem(new ScopeSwitchPresentationSystem(engine, runtime));
            engine.RegisterSystem(new ScopeSwitchSimulationSystem(engine, runtime), SystemGroup.InputCollection);
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
