using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphOpsRelMod.Runtime;

internal sealed class GraphOpsRelPresentationSystem : ISystem<float>
{
    private readonly GraphOpsRelRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly GraphShowcaseConfig _config = new();

    public GraphOpsRelPresentationSystem(
        GraphOpsRelRuntime runtime,
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
            _debugDraw, _runtime.PlayerX, _runtime.PlayerY, 0.7f, GraphShowcaseStagePresenter.CasterColor, 0.2f);

        for (int i = 0; i < _runtime.FriendSlotCount; i++)
        {
            float ang = -0.9f + i * 0.55f;
            float x = MathF.Sin(ang) * 5.5f;
            float y = 3.5f + MathF.Cos(ang) * 0.6f;
            string label = $"好友{i + 1}";
            DebugDrawColor color;
            if (string.Equals(_runtime.TopFriendLabel, label, StringComparison.Ordinal))
            {
                color = DebugDrawColor.Cyan;
            }
            else if (string.Equals(_runtime.WeakFriendLabel, label, StringComparison.Ordinal))
            {
                color = DebugDrawColor.Yellow;
            }
            else if (!_runtime.IsFriendLinked(i))
            {
                color = DebugDrawColor.Gray;
            }
            else
            {
                color = GraphShowcaseStagePresenter.EnemyColor;
            }

            GraphShowcaseStagePresenter.DrawActor(_debugDraw, x, y, 0.42f, color);
            if (_runtime.IsFriendLinked(i))
            {
                GraphShowcaseStagePresenter.DrawAggroLine(_debugDraw, _runtime.PlayerX, _runtime.PlayerY, x, y);
            }
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, _runtime.Phase, _runtime.Metrics.Detail);
    }
}
