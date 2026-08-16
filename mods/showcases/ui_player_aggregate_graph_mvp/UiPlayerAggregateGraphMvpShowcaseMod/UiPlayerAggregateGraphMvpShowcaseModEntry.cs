using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiPlayerAggregateGraphMvpShowcaseMod.Runtime;
using UiPlayerAggregateGraphMvpShowcaseMod.Systems;
using Ludots.Platform.Abstractions;

namespace UiPlayerAggregateGraphMvpShowcaseMod;

public sealed class UiPlayerAggregateGraphMvpShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        AttributeRegistry.Register("Showcase.Resource.Ore");
        AttributeRegistry.Register("Showcase.Resource.Crystal");

        var runtime = new UiPlayerAggregateGraphMvpRuntime();

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(UiPlayerAggregateGraphMvpIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[UiPlayerAggregateGraphMvpIds.InstalledKey] = true;
            engine.GlobalContext[UiPlayerAggregateGraphMvpIds.RuntimeServiceKey] = runtime;
            var debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer) ?? new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterSystem(new UiPlayerAggregateGraphMvpSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new UiPlayerAggregateGraphMvpPresentationSystem(engine, runtime, debugDraw));
            context.Log("[UiPlayerAggregateGraphMvpShowcaseMod] production systems registered.");
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
