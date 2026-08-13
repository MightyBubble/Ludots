using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphOpsFloatMod.Runtime;

internal sealed class GraphOpsFloatPresentationSystem : ISystem<float>
{
    public const string PlayerTitle = "浮点伤害演算图节点";

    private readonly GraphOpsFloatRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public GraphOpsFloatPresentationSystem(
        GraphOpsFloatRuntime runtime,
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
            _debugDraw,
            _runtime.CasterX,
            _runtime.CasterY,
            _runtime.TargetX,
            _runtime.TargetY);
        GraphShowcaseStagePresenter.DrawTriggerRing(
            _debugDraw,
            _runtime.CasterX,
            _runtime.CasterY,
            GraphOpsFloatRuntime.MaxRange * 0.2f,
            _runtime.LastRangeValid);
        GraphShowcaseStagePresenter.DrawGateBar(_debugDraw, 3.2f, 4f, open: _runtime.LastPermit);
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, PlayerTitle, _runtime.Metrics.Detail);
    }
}
