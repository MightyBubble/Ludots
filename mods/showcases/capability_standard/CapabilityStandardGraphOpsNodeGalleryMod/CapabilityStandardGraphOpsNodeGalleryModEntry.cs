using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphOpsNodeGalleryMod;

public sealed class CapabilityStandardGraphOpsNodeGalleryModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardGraphOpsNodeGallery.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphOpsNodeGalleryMod] Loaded (per-op GraphNodeOp gallery host)");
        var runtime = new GraphOpsNodeGalleryRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException(
                    "CapabilityStandardGraphOpsNodeGalleryMod GameStart requires GameEngine.");

            string? startupMapId = engine.MergedConfig?.StartupMapId;
            if (!GraphOpsNodeIds.TryParseOpFromMapId(startupMapId, out _))
            {
                return Task.CompletedTask;
            }

            runtime.BindFromStartupMapId(startupMapId);
            runtime.AttachEngine(engine);
            engine.SetService(MetricsKey, runtime.Metrics);
            runtime.BindStageVisuals(GraphOpsStageVisuals.FromEngine(engine));
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("Node gallery requires ScreenOverlayBuffer.");
            engine.RegisterSystem(new GraphOpsNodeGallerySimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new GraphOpsNodeGalleryPresentationSystem(runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ =>
        {
            if (runtime.IsBound)
            {
                runtime.EnsureWorld();
            }

            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
