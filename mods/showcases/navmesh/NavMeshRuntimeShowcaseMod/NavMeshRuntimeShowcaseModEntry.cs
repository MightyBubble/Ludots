using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Scripting;
using NavMeshRuntimeShowcaseMod.Systems;

namespace NavMeshRuntimeShowcaseMod;

public sealed class NavMeshRuntimeShowcaseModEntry : IMod
{
    private const string ObstacleCycleSystemInstalledKey = "NavMeshRuntimeShowcase.ObstacleCycleSystemInstalled";

    public void OnLoad(IModContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        context.Log("[NavMeshRuntimeShowcaseMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ConfigureShowcaseAsync);
        context.OnEvent(GameEvents.MapLoaded, ConfigureShowcaseAsync);
        context.OnEvent(GameEvents.MapResumed, ConfigureShowcaseAsync);
    }

    public void OnUnload()
    {
    }

    private static Task ConfigureShowcaseAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        EnableNavMeshVisualization(engine);
        EnsureObstacleCycleSystem(engine);
        return Task.CompletedTask;
    }

    private static void EnableNavMeshVisualization(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.NavMeshPresentationState, out NavMeshPresentationState state))
        {
            throw new InvalidOperationException(
                "NavMeshRuntimeShowcaseMod requires NavMeshPresentationState; Core presentation composition did not register it.");
        }

        if (!engine.TryGetService(CoreServiceKeys.RenderDebugState, out RenderDebugState renderDebug))
        {
            throw new InvalidOperationException(
                "NavMeshRuntimeShowcaseMod requires RenderDebugState to toggle navmesh drawing.");
        }

        state.Configure(
            enabled: true,
            layer: 0,
            profile: 0,
            style: new NavMeshPresentationStyle(
                fillColor: new NavMeshPresentationColor(0.16f, 0.75f, 1.0f, 0.35f),
                edgeColor: new NavMeshPresentationColor(0.08f, 0.35f, 0.63f, 0.92f),
                heightOffsetMeters: 0.05f,
                drawFill: true,
                drawEdges: true));
        renderDebug.DrawNavMesh = true;
    }

    private static void EnsureObstacleCycleSystem(GameEngine engine)
    {
        if (engine.GlobalContext.ContainsKey(ObstacleCycleSystemInstalledKey))
        {
            return;
        }

        engine.RegisterSystem(new NavMeshShowcaseObstacleCycleSystem(engine), SystemGroup.InputCollection);
        engine.GlobalContext[ObstacleCycleSystemInstalledKey] = true;
    }
}
