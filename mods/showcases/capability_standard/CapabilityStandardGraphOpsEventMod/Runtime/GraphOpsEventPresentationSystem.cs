using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphOpsEventMod.Runtime;

internal sealed class GraphOpsEventPresentationSystem : ISystem<float>
{
    private readonly GraphOpsEventRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public GraphOpsEventPresentationSystem(
        GraphOpsEventRuntime runtime,
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
        GraphShowcaseStagePresenter.DrawTriggerRing(
            _debugDraw, _runtime.SnapMarkerX, _runtime.SnapMarkerY, 2.0f, _runtime.SnapCollectionOk);
        GraphShowcaseStagePresenter.DrawAggroLine(
            _debugDraw, _runtime.ControllerX, _runtime.ControllerY, _runtime.UnitX, _runtime.UnitY);
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, "发事件 / 落点吸附", _runtime.Metrics.Detail);
    }
}
