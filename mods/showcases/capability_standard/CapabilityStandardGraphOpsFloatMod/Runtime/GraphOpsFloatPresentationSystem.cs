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
    private readonly ScreenOverlayBuffer? _overlay;
    private readonly GraphShowcaseConfig _config = new();

    public GraphOpsFloatPresentationSystem(
        GraphOpsFloatRuntime runtime,
        DebugDrawCommandBuffer debugDraw,
        ScreenOverlayBuffer? overlay)
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
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _runtime.TargetX, _runtime.TargetY, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
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

        float healthRatio = Math.Clamp(_runtime.TargetHealth / GraphOpsFloatRuntime.OpeningHealth, 0f, 1f);
        _debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new System.Numerics.Vector2(_runtime.TargetX, _runtime.TargetY - 1.4f),
            HalfWidth = 1.2f,
            HalfHeight = 0.12f,
            Thickness = 0.08f,
            Color = DebugDrawColor.Gray
        });
        if (healthRatio > 0f)
        {
            _debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new System.Numerics.Vector2(_runtime.TargetX - 1.2f + healthRatio * 1.2f, _runtime.TargetY - 1.4f),
                HalfWidth = healthRatio * 1.2f,
                HalfHeight = 0.1f,
                Thickness = 0.06f,
                Color = _runtime.LastApplied ? DebugDrawColor.Red : DebugDrawColor.Green
            });
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
        if (_overlay != null)
        {
            GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, PlayerTitle, _runtime.Metrics.Detail);
        }
    }
}
