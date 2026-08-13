using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphOpsAttrMod.Runtime;

internal sealed class GraphOpsAttrPresentationSystem : ISystem<float>
{
    public const string PlayerTitle = "属性/效果模板图节点";

    private readonly GraphOpsAttrRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public GraphOpsAttrPresentationSystem(
        GraphOpsAttrRuntime runtime,
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
            GraphOpsAttrRuntime.CasterX,
            GraphOpsAttrRuntime.CasterY,
            GraphOpsAttrRuntime.TargetX,
            GraphOpsAttrRuntime.TargetY);
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, PlayerTitle, _runtime.Metrics.Detail);
    }
}
