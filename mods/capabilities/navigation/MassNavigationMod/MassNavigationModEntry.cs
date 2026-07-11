using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace MassNavigationMod;

public sealed class MassNavigationModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new MassNavigationRuntime();
        context.OnEvent(GameEvents.MapLoaded, eventContext => HandleMapFocusedAsync(runtime, eventContext));
        context.OnEvent(GameEvents.MapResumed, eventContext => HandleMapFocusedAsync(runtime, eventContext));
        context.OnEvent(GameEvents.MapSuspended, eventContext => HandleMapSuspendedAsync(runtime, eventContext));
        context.OnEvent(GameEvents.MapUnloaded, eventContext => HandleMapUnloadedAsync(runtime, eventContext));
        context.Log("[MassNavigationMod] Loaded MassNavigation runtime and assets.");
    }

    public void OnUnload()
    {
    }

    private static Task HandleMapFocusedAsync(
        MassNavigationRuntime runtime,
        ScriptContext context)
    {
        GameEngine engine = RequireEngine(context);
        MapId mapId = RequireMapId(context);
        if (runtime.HandleMapFocused(engine, mapId, out MassNavigationMapRuntimeState? mapState))
        {
            MassNavigationSceneOwner sceneOwner;
            if (mapState!.SceneController == null)
            {
                sceneOwner = new MassNavigationSceneOwner(mapState.MapId, mapState.Profile.SceneAuthoring);
                runtime.AttachSceneController(mapId, sceneOwner);
            }
            else
            {
                sceneOwner = mapState.SceneController as MassNavigationSceneOwner
                    ?? throw new InvalidOperationException(
                        $"MassNavigation map '{mapId.Value}' owns an incompatible scene controller.");
            }

            sceneOwner.Activate(engine, mapState.Simulation);
        }

        return Task.CompletedTask;
    }

    private static Task HandleMapSuspendedAsync(
        MassNavigationRuntime runtime,
        ScriptContext context)
    {
        GameEngine engine = RequireEngine(context);
        MapId mapId = RequireMapId(context);
        DeactivateScene(runtime, engine, mapId);
        runtime.HandleMapSuspended(engine, mapId);
        return Task.CompletedTask;
    }

    private static Task HandleMapUnloadedAsync(
        MassNavigationRuntime runtime,
        ScriptContext context)
    {
        GameEngine engine = RequireEngine(context);
        MapId mapId = RequireMapId(context);
        DeactivateScene(runtime, engine, mapId);
        runtime.HandleMapUnloaded(engine, mapId);
        return Task.CompletedTask;
    }

    private static void DeactivateScene(MassNavigationRuntime runtime, GameEngine engine, MapId mapId)
    {
        if (runtime.TryGetMapState(mapId, out MassNavigationMapRuntimeState? mapState) &&
            mapState!.SceneController is MassNavigationSceneOwner sceneOwner)
        {
            sceneOwner.Deactivate(engine);
        }
    }

    private static GameEngine RequireEngine(ScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetEngine()
            ?? throw new InvalidOperationException("MassNavigationMod map lifecycle requires GameEngine in ScriptContext.");
    }

    private static MapId RequireMapId(ScriptContext context)
    {
        if (!context.TryGet(CoreServiceKeys.MapId, out MapId mapId))
        {
            throw new InvalidOperationException("MassNavigationMod map lifecycle requires MapId in ScriptContext.");
        }

        return mapId;
    }
}
