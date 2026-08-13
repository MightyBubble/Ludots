using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsAttrMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphOpsAttrMod;

public sealed class CapabilityStandardGraphOpsAttrModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardGraphOpsAttr.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphOpsAttrMod] Loaded (attr/effect graph ops)");
        var runtime = new GraphOpsAttrRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException(
                    "CapabilityStandardGraphOpsAttrMod GameStart requires GameEngine.");
            runtime.AttachEngine(engine);
            runtime.Bind(engine.GetService(CoreServiceKeys.GraphProgramRegistry));
            runtime.BindStageVisuals(GraphOpsStageVisuals.FromEngine(engine));
            engine.SetService(MetricsKey, runtime.Metrics);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("Attr gallery requires ScreenOverlayBuffer.");
            engine.RegisterSystem(new GraphOpsAttrSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new GraphOpsAttrPresentationSystem(runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
