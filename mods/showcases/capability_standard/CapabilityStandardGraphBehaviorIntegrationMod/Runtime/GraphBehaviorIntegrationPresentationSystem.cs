using System;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphBehaviorIntegrationMod.Runtime;

internal sealed class GraphBehaviorIntegrationPresentationSystem : ISystem<float>
{
    private readonly GraphBehaviorIntegrationRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly GraphShowcaseConfig _config = new();

    public GraphBehaviorIntegrationPresentationSystem(
        GraphBehaviorIntegrationRuntime runtime,
        DebugDrawCommandBuffer debugDraw)
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
        GraphShowcaseStagePresenter.DrawPolyline(_debugDraw, GraphBehaviorIntegrationRuntime.LeftPatrol, GraphShowcaseStagePresenter.PathColor);
        GraphShowcaseStagePresenter.DrawTriggerRing(_debugDraw, 0f, -8f, 1.6f, armed: (_runtime.Level?.Phase ?? 0) == 0);
        GraphShowcaseStagePresenter.DrawGateBar(_debugDraw, y: 5f, halfWidth: 3.5f, open: (_runtime.Level?.Phase ?? 0) >= 2);
        GraphShowcaseStagePresenter.DrawPhasePips(_debugDraw, Math.Max(1, _runtime.Level?.Phase ?? 0), 3);

        _debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new System.Numerics.Vector2(5.5f, -5.5f),
            B = new System.Numerics.Vector2(5.5f, 5.5f),
            Thickness = 0.12f,
            Color = GraphShowcaseStagePresenter.PathColor
        });

        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _runtime.MarkerX, _runtime.MarkerY, 0.45f, GraphShowcaseStagePresenter.CasterColor);

        if (_runtime.EnemyAlive)
        {
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw, _runtime.EnemyX, _runtime.EnemyY, 0.55f, GraphShowcaseStagePresenter.EnemyColor, 0.18f);
        }

        for (int i = 0; i < _runtime.GuardCount; i++)
        {
            var color = _runtime.Intent[i] switch
            {
                1 => GraphShowcaseStagePresenter.SentryAlert,
                2 => GraphShowcaseStagePresenter.SentryCombat,
                _ => GraphShowcaseStagePresenter.GuardColor
            };
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.GuardX[i], _runtime.GuardY[i], 0.4f, color);
            if (_runtime.Intent[i] >= 1 && _runtime.EnemyAlive)
            {
                GraphShowcaseStagePresenter.DrawAggroLine(
                    _debugDraw, _runtime.GuardX[i], _runtime.GuardY[i], _runtime.EnemyX, _runtime.EnemyY);
            }
        }

        if (_runtime.Hfsm != null)
        {
            for (int i = 0; i < _runtime.SentryCount; i++)
            {
                int leaf = _runtime.Hfsm.GetLeafState(i);
                DebugDrawColor color = leaf switch
                {
                    1 => GraphShowcaseStagePresenter.SentryIdle,
                    3 => GraphShowcaseStagePresenter.SentryAlert,
                    4 => GraphShowcaseStagePresenter.SentryCombat,
                    5 => GraphShowcaseStagePresenter.SentryRetreat,
                    _ => DebugDrawColor.Gray
                };
                GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.SentryX[i], _runtime.SentryY[i], 0.4f, color);
            }
        }

        if (_config.ShowCrowdBand)
        {
            GraphShowcaseStagePresenter.DrawCrowdBand(_debugDraw, _config.CrowdBandCount);
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
    }
}
