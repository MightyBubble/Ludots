using System.Threading.Tasks;
using CapabilityStandardGraphOpsFloatMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphOpsFloatMod;

public sealed class CapabilityStandardGraphOpsFloatModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardGraphOpsFloat.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphOpsFloatMod] Loaded (float graph-op damage pipeline)");
        var runtime = new GraphOpsFloatRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException(
                    "CapabilityStandardGraphOpsFloatMod GameStart requires GameEngine.");
            engine.SetService(MetricsKey, runtime.Metrics);
            runtime.AttachEngine(engine);
            runtime.BindStageVisuals(GraphOpsStageVisuals.FromEngine(engine));
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("Float gallery requires ScreenOverlayBuffer.");
            engine.RegisterSystem(new GraphOpsFloatSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new GraphOpsFloatPresentationSystem(runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
