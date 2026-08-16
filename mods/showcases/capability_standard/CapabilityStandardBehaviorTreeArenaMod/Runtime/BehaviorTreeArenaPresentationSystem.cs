using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardBehaviorTreeArenaMod.Runtime;

internal sealed class BehaviorTreeArenaPresentationSystem : ISystem<float>
{
    private readonly BehaviorTreeArenaRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly GraphShowcaseConfig _config = new();

    public BehaviorTreeArenaPresentationSystem(BehaviorTreeArenaRuntime runtime, DebugDrawCommandBuffer debugDraw)
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
        GraphShowcaseStagePresenter.DrawPolyline(_debugDraw, BehaviorTreeArenaRuntime.PatrolPath, GraphShowcaseStagePresenter.PathColor);

        for (int e = 0; e < _runtime.EnemyCount; e++)
        {
            if (!_runtime.EnemyAlive[e]) continue;
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw, _runtime.EnemyX[e], _runtime.EnemyY[e], 0.55f, GraphShowcaseStagePresenter.EnemyColor, 0.18f);
        }

        for (int i = 0; i < _runtime.GuardCount; i++)
        {
            var color = _runtime.Intent[i] switch
            {
                1 => GraphShowcaseStagePresenter.SentryAlert,
                2 => GraphShowcaseStagePresenter.SentryCombat,
                _ => GraphShowcaseStagePresenter.GuardColor
            };
            if (_runtime.Flash[i] > 0) color = DebugDrawColor.White;
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.GuardX[i], _runtime.GuardY[i], 0.45f, color);
            int t = _runtime.TargetIndex[i];
            if (_runtime.Intent[i] >= 1 && t >= 0 && _runtime.EnemyAlive[t])
            {
                GraphShowcaseStagePresenter.DrawAggroLine(
                    _debugDraw, _runtime.GuardX[i], _runtime.GuardY[i], _runtime.EnemyX[t], _runtime.EnemyY[t]);
            }
        }

        if (_config.ShowCrowdBand)
        {
            GraphShowcaseStagePresenter.DrawCrowdBand(_debugDraw, _config.CrowdBandCount);
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
    }
}
