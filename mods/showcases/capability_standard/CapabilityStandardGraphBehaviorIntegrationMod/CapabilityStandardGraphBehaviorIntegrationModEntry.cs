using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphBehaviorIntegrationMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphBehaviorIntegrationMod;

public sealed class CapabilityStandardGraphBehaviorIntegrationModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardGraphBehaviorIntegration.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphBehaviorIntegrationMod] Loaded (integration-only demo)");
        var runtime = new GraphBehaviorIntegrationRuntime();
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
            engine.RegisterSystem(new GraphBehaviorIntegrationSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new GraphBehaviorIntegrationPresentationSystem(runtime, debugDraw));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
