using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using PersistenceOnlineReplayShowcaseMod.Runtime;
using PersistenceOnlineReplayShowcaseMod.Systems;

namespace PersistenceOnlineReplayShowcaseMod;

public sealed class PersistenceOnlineReplayShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtime = new PersistenceOnlineReplayRuntime(context);
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.GetEngine() is not GameEngine engine ||
                (engine.GlobalContext.TryGetValue(PersistenceOnlineReplayShowcaseIds.InstalledKey, out object? value) && value is true))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[PersistenceOnlineReplayShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[PersistenceOnlineReplayShowcaseIds.RuntimeKey] = runtime;
            var debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer) ?? new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterSystem(new PersistenceOnlineReplayInputSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new PersistenceOnlineReplayPresentationSystem(engine, runtime, debugDraw));
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
