using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsScriptMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphOpsScriptMod;

public sealed class CapabilityStandardGraphOpsScriptModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardGraphOpsScript.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphOpsScriptMod] Loaded (Script control graph ops)");
        var runtime = new GraphOpsScriptRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException(
                    "CapabilityStandardGraphOpsScriptMod GameStart requires GameEngine.");
            runtime.AttachEngine(engine);
            runtime.Bind(
                engine.GetService(CoreServiceKeys.GraphProgramRegistry),
                engine.GetService(CoreServiceKeys.GraphActionCatalog),
                engine.GetService(CoreServiceKeys.GraphFunctionCatalog));
            runtime.BindStageVisuals(GraphOpsStageVisuals.FromEngine(engine));
            engine.SetService(MetricsKey, runtime.Metrics);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("GraphOps Script showcase requires ScreenOverlayBuffer.");
            engine.RegisterSystem(new GraphOpsScriptSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new GraphOpsScriptPresentationSystem(runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
