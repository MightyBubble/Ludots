using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsRelMod.Runtime;

internal sealed class GraphOpsRelPresentationSystem : ISystem<float>
{
    private readonly GraphOpsRelRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly GraphShowcaseConfig _config = new();

    public GraphOpsRelPresentationSystem(GraphOpsRelRuntime runtime, DebugDrawCommandBuffer debugDraw)
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
            _debugDraw, _runtime.PlayerX, _runtime.PlayerY, 0.7f, GraphShowcaseStagePresenter.CasterColor, 0.2f);

        for (int i = 0; i < _runtime.FriendSlotCount; i++)
        {
            float ang = -0.9f + i * 0.55f;
            float x = MathF.Sin(ang) * 5.5f;
            float y = 3.5f + MathF.Cos(ang) * 0.6f;
            var color = string.Equals(_runtime.TopFriendLabel, $"好友{i + 1}", StringComparison.Ordinal)
                ? DebugDrawColor.Cyan
                : GraphShowcaseStagePresenter.EnemyColor;
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, x, y, 0.42f, color);
            GraphShowcaseStagePresenter.DrawAggroLine(_debugDraw, _runtime.PlayerX, _runtime.PlayerY, x, y);
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
    }
}
