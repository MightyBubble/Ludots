using System.Threading.Tasks;
using CapabilityStandardGraphOpsBlackboardMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphOpsBlackboardMod;

public sealed class CapabilityStandardGraphOpsBlackboardModEntry : IMod
{
  public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
    new("CapabilityStandardGraphOpsBlackboard.Metrics");

  public void OnLoad(IModContext context)
  {
    context.Log("[CapabilityStandardGraphOpsBlackboardMod] Loaded (blackboard/config/lifecycle graph ops)");
    var runtime = new GraphOpsBlackboardRuntime();
    context.OnEvent(GameEvents.GameStart, ctx =>
    {
      GameEngine engine = ctx.GetEngine()
        ?? throw new InvalidOperationException(
          "CapabilityStandardGraphOpsBlackboardMod GameStart requires GameEngine.");
      engine.SetService(MetricsKey, runtime.Metrics);
      runtime.AttachEngine(engine);
      runtime.BindStageVisuals(GraphOpsStageVisuals.FromEngine(engine));
      var debugDraw = new DebugDrawCommandBuffer();
      engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
      ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
        ?? throw new InvalidOperationException("ScreenOverlayBuffer is required for blackboard player caption.");
      engine.RegisterSystem(new GraphOpsBlackboardSimulationSystem(engine, runtime), SystemGroup.PostMovement);
      engine.RegisterPresentationSystem(new GraphOpsBlackboardPresentationSystem(runtime, debugDraw, overlay));
      return Task.CompletedTask;
    });
    context.OnEvent(GameEvents.MapLoaded, _ => { runtime.EnsureWorld(); return Task.CompletedTask; });
  }

  public void OnUnload() { }
}
