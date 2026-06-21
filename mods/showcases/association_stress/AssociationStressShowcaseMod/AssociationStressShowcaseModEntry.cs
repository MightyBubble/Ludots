using System.Threading.Tasks;
using AssociationStressShowcaseMod.Runtime;
using AssociationStressShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace AssociationStressShowcaseMod;

public sealed class AssociationStressShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new AssociationStressShowcaseRuntime(context);

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            var engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(AssociationStressIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[AssociationStressIds.InstalledKey] = true;
            engine.GlobalContext[AssociationStressIds.RuntimeServiceKey] = runtime;
            engine.RegisterPresentationSystem(new AssociationStressPresentationSystem(engine, runtime));
            engine.RegisterSystem(new AssociationStressSimulationSystem(engine, runtime), SystemGroup.InputCollection);
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
