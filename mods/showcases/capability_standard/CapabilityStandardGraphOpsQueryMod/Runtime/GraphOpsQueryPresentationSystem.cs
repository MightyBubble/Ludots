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
    private readonly GraphShowcaseConfig _config = new();

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
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _runtime.CasterX, _runtime.CasterY, 0.7f, GraphShowcaseStagePresenter.CasterColor, 0.2f);

        for (int i = 0; i < _runtime.UnitCount; i++)
        {
            DebugDrawColor color = ResolveColor(i);
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.UnitX[i], _runtime.UnitY[i], 0.42f, color);
        }

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

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, "筛人聚合", _runtime.Metrics.Detail);
    }

    private DebugDrawColor ResolveColor(int index)
    {
        if (index == _runtime.StrongestIndex)
        {
            return DebugDrawColor.Cyan;
        }

        if (index == _runtime.WeakestIndex)
        {
            return DebugDrawColor.Yellow;
        }

        if (_runtime.UnitDead[index] != 0)
        {
            return DebugDrawColor.Gray;
        }

        if (_runtime.UnitInRange[index] != 0)
        {
            return GraphShowcaseStagePresenter.EnemyColor;
        }

        return _runtime.UnitEnemy[index] != 0
            ? DebugDrawColor.Blue
            : GraphShowcaseStagePresenter.GuardColor;
    }
}
