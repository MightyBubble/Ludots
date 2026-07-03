using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using TeamResearchShowcaseMod.Runtime;
using TeamResearchShowcaseMod.Systems;

namespace TeamResearchShowcaseMod;

public sealed class TeamResearchShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new TeamResearchRuntime(context);

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(TeamResearchIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[TeamResearchIds.InstalledKey] = true;
            engine.GlobalContext[TeamResearchIds.RuntimeServiceKey] = runtime;
            engine.RegisterPresentationSystem(new TeamResearchPresentationSystem(engine, runtime));
            engine.RegisterSystem(new TeamResearchSimulationSystem(engine, runtime), SystemGroup.InputCollection);
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
