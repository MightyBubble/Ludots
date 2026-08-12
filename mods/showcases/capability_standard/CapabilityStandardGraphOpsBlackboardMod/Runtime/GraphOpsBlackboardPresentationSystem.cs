using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsBlackboardMod.Runtime;

internal sealed class GraphOpsBlackboardPresentationSystem : ISystem<float>
{
  private readonly GraphOpsBlackboardRuntime _runtime;
  private readonly DebugDrawCommandBuffer _debugDraw;
  private readonly GraphShowcaseConfig _config = new();

  public GraphOpsBlackboardPresentationSystem(GraphOpsBlackboardRuntime runtime, DebugDrawCommandBuffer debugDraw)
  {
    _runtime = runtime;
    _debugDraw = debugDraw;
  }

  public void Initialize() { }
  public void BeforeUpdate(in float t) { }
  public void AfterUpdate(in float t) { }
  public void Dispose() { }

  public void Update(in float dt)
  {
    GraphShowcaseStagePresenter.Clear(_debugDraw);
    GraphShowcaseStagePresenter.DrawActor(
      _debugDraw, _runtime.ClerkX, _runtime.ClerkY, 0.65f, GraphShowcaseStagePresenter.CasterColor, 0.18f);
    GraphShowcaseStagePresenter.DrawActor(
      _debugDraw, _runtime.ContextX, _runtime.ContextY, 0.45f, DebugDrawColor.Cyan);
    GraphShowcaseStagePresenter.DrawAggroLine(
      _debugDraw, _runtime.ClerkX, _runtime.ClerkY, _runtime.ContextX, _runtime.ContextY);
    GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
  }
}
