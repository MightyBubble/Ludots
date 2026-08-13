using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphOpsQueryMod.Runtime;

internal sealed class GraphOpsQueryPresentationSystem : ISystem<float>
{
    private readonly GraphOpsQueryRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public GraphOpsQueryPresentationSystem(
        GraphOpsQueryRuntime runtime,
        DebugDrawCommandBuffer debugDraw,
        ScreenOverlayBuffer overlay)
    {
        _runtime = runtime;
        _debugDraw = debugDraw;
        _overlay = overlay;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        GraphShowcaseStagePresenter.Clear(_debugDraw);
        int strongest = _runtime.StrongestIndex;
        if (strongest >= 0 && strongest < _runtime.UnitCount)
        {
            GraphShowcaseStagePresenter.DrawAggroLine(
                _debugDraw,
                _runtime.CasterX,
                _runtime.CasterY,
                _runtime.UnitX[strongest],
                _runtime.UnitY[strongest]);
        }

        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, "筛人聚合", _runtime.Metrics.Detail);
    }
}
