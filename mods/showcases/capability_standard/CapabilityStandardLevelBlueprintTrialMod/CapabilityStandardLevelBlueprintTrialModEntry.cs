using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardLevelBlueprintTrialMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardLevelBlueprintTrialMod;

public sealed class CapabilityStandardLevelBlueprintTrialModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardLevelBlueprintTrial.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardLevelBlueprintTrialMod] Loaded (Level-only showcase)");
        var runtime = new LevelBlueprintTrialRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            runtime.Bind(
                engine.GetService(CoreServiceKeys.GraphProgramRegistry),
                engine.GetService(CoreServiceKeys.GraphActionCatalog));
            engine.SetService(MetricsKey, runtime.Metrics);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterSystem(new LevelBlueprintTrialSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new LevelBlueprintTrialPresentationSystem(runtime, debugDraw));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
