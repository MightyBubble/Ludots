using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsEventMod.Runtime;

internal sealed class GraphOpsEventPresentationSystem : ISystem<float>
{
    private readonly GraphOpsEventRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

    public GraphOpsEventPresentationSystem(GraphOpsEventRuntime runtime, DebugDrawCommandBuffer debugDraw)
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
            _debugDraw, _runtime.PlayerRepX, _runtime.PlayerRepY, 0.55f, DebugDrawColor.Cyan);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _runtime.ControllerX, _runtime.ControllerY, 0.45f, DebugDrawColor.Yellow);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _runtime.UnitX, _runtime.UnitY, 0.4f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _runtime.SnapMarkerX, _runtime.SnapMarkerY, 0.25f, DebugDrawColor.White);
        GraphShowcaseStagePresenter.DrawAggroLine(
            _debugDraw, _runtime.ControllerX, _runtime.ControllerY, _runtime.UnitX, _runtime.UnitY);
        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs);
    }
}
