using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsFloatMod.Runtime;

internal sealed class GraphOpsFloatPresentationSystem : ISystem<float>
{
    private readonly GraphOpsFloatRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly GraphShowcaseConfig _config = new();

    public GraphOpsFloatPresentationSystem(GraphOpsFloatRuntime runtime, DebugDrawCommandBuffer debugDraw)
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
            _debugDraw, _runtime.CasterX, _runtime.CasterY, 0.7f, GraphShowcaseStagePresenter.CasterColor, 0.2f);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _runtime.TargetX, _runtime.TargetY, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawAggroLine(
            _debugDraw,
            _runtime.CasterX,
            _runtime.CasterY,
            _runtime.TargetX,
            _runtime.TargetY);
        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
    }
}
