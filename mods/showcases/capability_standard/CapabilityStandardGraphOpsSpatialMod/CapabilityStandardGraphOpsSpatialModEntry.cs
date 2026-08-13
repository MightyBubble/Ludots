using System.Threading.Tasks;
using CapabilityStandardGraphOpsSpatialMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphOpsSpatialMod;

public sealed class CapabilityStandardGraphOpsSpatialModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardGraphOpsSpatial.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphOpsSpatialMod] Loaded (Spatial query showcase)");
        var runtime = new GraphOpsSpatialRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException(
                    "CapabilityStandardGraphOpsSpatialMod GameStart requires GameEngine.");
            GraphProgramRegistry programs = GraphOpsSpatialCatalogBootstrap.Load(out GraphFunctionCatalog catalog);
            runtime.AttachEngine(engine);
            runtime.Bind(programs, catalog);
            runtime.BindStageVisuals(GraphOpsStageVisuals.FromEngine(engine));
            engine.SetService(MetricsKey, runtime.Metrics);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("GraphOps Spatial showcase requires ScreenOverlayBuffer.");
            engine.RegisterSystem(new GraphOpsSpatialSimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new GraphOpsSpatialPresentationSystem(runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
    }

    public void OnUnload() { }
}
