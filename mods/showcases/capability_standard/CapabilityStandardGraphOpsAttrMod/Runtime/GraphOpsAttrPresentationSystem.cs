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
    private readonly ScreenOverlayBuffer? _overlay;
    private readonly GraphShowcaseConfig _config = new();

    public GraphOpsAttrPresentationSystem(
        GraphOpsAttrRuntime runtime,
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
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, -2.5f, 0f, 0.7f, GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, 2.5f, 0f, 0.55f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawAggroLine(_debugDraw, -2.5f, 0f, 2.5f, 0f);

        float healthRatio = Math.Clamp(_runtime.TargetHealth / GraphOpsAttrRuntime.OpeningHealth, 0f, 1f);
        _debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new System.Numerics.Vector2(2.5f, -1.4f),
            HalfWidth = 1.2f,
            HalfHeight = 0.12f,
            Thickness = 0.08f,
            Color = DebugDrawColor.Gray
        });
        if (healthRatio > 0f)
        {
            _debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new System.Numerics.Vector2(2.5f - 1.2f + healthRatio * 1.2f, -1.4f),
                HalfWidth = healthRatio * 1.2f,
                HalfHeight = 0.1f,
                Thickness = 0.06f,
                Color = healthRatio > 0.35f ? DebugDrawColor.Green : DebugDrawColor.Red
            });
        }

        if (_config.ShowCrowdBand)
        {
            GraphShowcaseStagePresenter.DrawCrowdBand(_debugDraw, _config.CrowdBandCount);
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
        if (_overlay != null)
        {
            GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, PlayerTitle, _runtime.Metrics.Detail);
        }
    }
}
