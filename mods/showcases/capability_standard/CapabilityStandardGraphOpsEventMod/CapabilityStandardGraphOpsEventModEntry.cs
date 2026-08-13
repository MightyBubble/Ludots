using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsEventMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphOpsEventMod;

public sealed class CapabilityStandardGraphOpsEventModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardGraphOpsEvent.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphOpsEventMod] Loaded (event/control/snap/dispatch graph ops)");
        var runtime = new GraphOpsEventRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException(
                    "CapabilityStandardGraphOpsEventMod GameStart requires GameEngine.");
            GraphProgramRegistry programs = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
                ?? throw new InvalidOperationException("GraphProgramRegistry is required for event player graphs.");
            TargetDispatchPresetRegistry presets = engine.GetService(CoreServiceKeys.TargetDispatchPresetRegistry)
                ?? throw new InvalidOperationException("TargetDispatchPresetRegistry is required for event fan-out.");
            EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is required for event snap collections.");
            runtime.Bind(programs, presets, collections);
            runtime.AttachEngine(engine);
            runtime.BindStageVisuals(GraphOpsStageVisuals.FromEngine(engine));
            engine.SetService(MetricsKey, runtime.Metrics);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer is required for event player caption.");
            engine.RegisterSystem(new GraphOpsEventSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new GraphOpsEventPresentationSystem(runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
