using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod;

public sealed class CapabilityStandardLiveSkillWorkbenchShowcaseModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardLiveSkillWorkbenchShowcase.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardLiveSkillWorkbenchShowcaseMod] Loaded — production hot-apply vignette");
        var runtime = new LiveSkillWorkbenchVignetteRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;

            runtime.Bind(engine);
            engine.SetService(MetricsKey, runtime.Metrics);

            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterSystem(new LiveSkillWorkbenchVignetteSimulationSystem(runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new LiveSkillWorkbenchVignettePresentationSystem(engine, runtime, debugDraw));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ =>
        {
            runtime.EnsureWorld();
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
