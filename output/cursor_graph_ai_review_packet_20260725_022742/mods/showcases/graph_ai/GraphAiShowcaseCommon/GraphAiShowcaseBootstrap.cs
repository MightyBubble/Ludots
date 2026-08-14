using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GraphAiShowcaseCommon;

public static class GraphAiShowcaseBootstrap
{
    public static void Register(
        IModContext context,
        string modId,
        string expectedMapId,
        string runtimeKey,
        string logName)
    {
        var runtime = new GraphAiShowcaseRuntime(modId, expectedMapId, runtimeKey);
        string installedKey = runtimeKey + ".installed";

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(installedKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[installedKey] = true;
            engine.GlobalContext[runtimeKey] = runtime;
            engine.RegisterSystem(new GraphAiShowcaseSimulationSystem(runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new GraphAiShowcasePresentationSystem(engine, runtime));
            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        context.Log($"[{logName}] Loaded");
    }
}
