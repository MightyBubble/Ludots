using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using SaveLoadShowcaseMod.Runtime;
using SaveLoadShowcaseMod.Systems;

namespace SaveLoadShowcaseMod;

public sealed class SaveLoadShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtimeHolder = new SaveLoadShowcaseRuntime?[] { null };
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
                (engine.GlobalContext.TryGetValue(SaveLoadShowcaseIds.InstalledKey, out object? value) && value is true))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[SaveLoadShowcaseIds.InstalledKey] = true;
            var runtime = runtimeHolder[0] ??= new SaveLoadShowcaseRuntime(engine);
            engine.GlobalContext[SaveLoadShowcaseIds.RuntimeKey] = runtime;
            var debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer) ?? new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterPresentationSystem(new SaveLoadShowcasePresentationSystem(engine, runtime, debugDraw));
            context.Log("[SaveLoadShowcaseMod] Save/load showcase installed.");
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
