using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphOpsSpatialMod.Runtime;

internal sealed class GraphOpsSpatialPresentationSystem : ISystem<float>
{
    private readonly GraphOpsSpatialRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public GraphOpsSpatialPresentationSystem(
        GraphOpsSpatialRuntime runtime,
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
        int hit = _runtime.LastHitIndex;
        if (hit >= 0 && hit < _runtime.TargetCount)
        {
            GraphShowcaseStagePresenter.DrawAggroLine(
                _debugDraw,
                _runtime.CasterX,
                _runtime.CasterY,
                _runtime.TargetX[hit],
                _runtime.TargetY[hit]);
        }

        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, GraphOpsSpatialRuntime.CaptionTitle, _runtime.Metrics.Detail);
    }
}
