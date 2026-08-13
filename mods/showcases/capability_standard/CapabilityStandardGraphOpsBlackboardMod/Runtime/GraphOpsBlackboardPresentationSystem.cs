using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphOpsBlackboardMod.Runtime;

internal sealed class GraphOpsBlackboardPresentationSystem : ISystem<float>
{
  private readonly GraphOpsBlackboardRuntime _runtime;
  private readonly DebugDrawCommandBuffer _debugDraw;
  private readonly ScreenOverlayBuffer _overlay;

  public GraphOpsBlackboardPresentationSystem(
    GraphOpsBlackboardRuntime runtime,
    DebugDrawCommandBuffer debugDraw,
    ScreenOverlayBuffer overlay)
  {
    _runtime = runtime;
    _debugDraw = debugDraw;
    _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
  }

  public void Initialize() { }
  public void BeforeUpdate(in float t) { }
  public void AfterUpdate(in float t) { }
  public void Dispose() { }

  public void Update(in float dt)
  {
    GraphShowcaseStagePresenter.Clear(_debugDraw);
    GraphShowcaseStagePresenter.DrawAggroLine(
      _debugDraw, _runtime.ClerkX, _runtime.ClerkY, _runtime.ContextX, _runtime.ContextY);
    GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, "黑板记事", _runtime.Metrics.Detail);
  }
}
