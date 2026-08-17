using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardHfsmSentryArenaMod.Runtime;

internal sealed class HfsmSentryArenaPresentationSystem : ISystem<float>
{
    private readonly HfsmSentryArenaRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly GraphShowcaseConfig _config = new();

    public HfsmSentryArenaPresentationSystem(HfsmSentryArenaRuntime runtime, DebugDrawCommandBuffer debugDraw)
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
        // Gate line
        _debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new System.Numerics.Vector2(-6.5f, -6.5f),
            B = new System.Numerics.Vector2(-6.5f, 6.5f),
            Thickness = 0.14f,
            Color = GraphShowcaseStagePresenter.PathColor
        });

        if (_runtime.IntruderAlive)
        {
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw, _runtime.IntruderX, _runtime.IntruderY, 0.55f, GraphShowcaseStagePresenter.EnemyColor, 0.18f);
        }

        var world = _runtime.World;
        if (world != null)
        {
            for (int i = 0; i < _runtime.SentryCount; i++)
            {
                int leaf = world.GetLeafState(i);
                DebugDrawColor color = leaf switch
                {
                    1 => GraphShowcaseStagePresenter.SentryIdle,
                    3 => GraphShowcaseStagePresenter.SentryAlert,
                    4 => GraphShowcaseStagePresenter.SentryCombat,
                    5 => GraphShowcaseStagePresenter.SentryRetreat,
                    _ => DebugDrawColor.Gray
                };
                GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.SentryX[i], _runtime.SentryY[i], 0.48f, color);
                if (_runtime.IntruderAlive && (leaf == 3 || leaf == 4))
                {
                    GraphShowcaseStagePresenter.DrawAggroLine(
                        _debugDraw, _runtime.SentryX[i], _runtime.SentryY[i], _runtime.IntruderX, _runtime.IntruderY);
                }
            }
        }

        if (_config.ShowCrowdBand)
        {
            GraphShowcaseStagePresenter.DrawCrowdBand(_debugDraw, _config.CrowdBandCount);
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
    }
}
