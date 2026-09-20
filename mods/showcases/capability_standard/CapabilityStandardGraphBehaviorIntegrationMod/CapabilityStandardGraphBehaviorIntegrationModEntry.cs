using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphBehaviorIntegrationMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphBehaviorIntegrationMod;

public sealed class CapabilityStandardGraphBehaviorIntegrationModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardGraphBehaviorIntegration.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphBehaviorIntegrationMod] Loaded (integration-only demo)");
        var runtime = new GraphBehaviorIntegrationRuntime();
        var panel = new GraphShowcasePanelController(
            runtime.BuildControlState,
            runtime.TogglePaused,
            runtime.Step,
            runtime.ToggleL2,
            runtime.ToggleStimulus,
            runtime.IncreaseSensorRadius,
            runtime.DecreaseSensorRadius,
            runtime.IncreaseThinkPeriod,
            runtime.DecreaseThinkPeriod,
            runtime.ResetScenario);
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            runtime.Bind(
                engine.GetService(CoreServiceKeys.GraphProgramRegistry),
                engine.GetService(CoreServiceKeys.GraphActionCatalog),
                engine.AiRuntime.Behavior);
            engine.SetService(MetricsKey, runtime.Metrics);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterSystem(new GraphBehaviorIntegrationSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new GraphBehaviorIntegrationPresentationSystem(engine, runtime, debugDraw, panel));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
        context.OnEvent(GameEvents.MapUnloaded, _ =>
        {
            panel.Clear();
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
