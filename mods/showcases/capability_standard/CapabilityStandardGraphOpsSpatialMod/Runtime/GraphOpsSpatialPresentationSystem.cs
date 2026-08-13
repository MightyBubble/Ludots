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
    private readonly GraphShowcaseConfig _config = new();

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
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _runtime.CasterX, _runtime.CasterY, 0.7f, GraphShowcaseStagePresenter.CasterColor, 0.2f);

        for (int i = 0; i < _runtime.TargetCount; i++)
        {
            var color = _runtime.Flash[i] > 0 ? DebugDrawColor.White : GraphShowcaseStagePresenter.EnemyColor;
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.TargetX[i], _runtime.TargetY[i], 0.45f, color);
        }

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

        if (_config.ShowCrowdBand)
        {
            GraphShowcaseStagePresenter.DrawCrowdBand(_debugDraw, _config.CrowdBandCount);
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, GraphOpsSpatialRuntime.CaptionTitle, _runtime.Metrics.Detail);
    }
}
