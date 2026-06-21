using System.Threading.Tasks;
using FourXAssociationShowcaseMod.Runtime;
using FourXAssociationShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FourXAssociationShowcaseMod;

public sealed class FourXAssociationShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new FourXAssociationRuntime(context);

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(FourXAssociationIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[FourXAssociationIds.InstalledKey] = true;
            engine.GlobalContext[FourXAssociationIds.RuntimeServiceKey] = runtime;
            engine.RegisterPresentationSystem(new FourXAssociationPresentationSystem(engine, runtime));
            engine.RegisterSystem(new FourXAssociationSimulationSystem(engine, runtime), SystemGroup.InputCollection);
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
