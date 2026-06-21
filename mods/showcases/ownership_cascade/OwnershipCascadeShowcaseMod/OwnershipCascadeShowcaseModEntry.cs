using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using OwnershipCascadeShowcaseMod.Runtime;
using OwnershipCascadeShowcaseMod.Systems;

namespace OwnershipCascadeShowcaseMod;

public sealed class OwnershipCascadeShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new OwnershipCascadeRuntime(context);

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(OwnershipCascadeIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[OwnershipCascadeIds.InstalledKey] = true;
            engine.GlobalContext[OwnershipCascadeIds.RuntimeServiceKey] = runtime;
            engine.RegisterPresentationSystem(new OwnershipCascadePresentationSystem(engine, runtime));
            engine.RegisterSystem(new OwnershipCascadeSimulationSystem(engine, runtime), SystemGroup.InputCollection);
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
