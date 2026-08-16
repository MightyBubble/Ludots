using System;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardLevelBlueprintTrialMod.Runtime;

internal sealed class LevelBlueprintTrialPresentationSystem : ISystem<float>
{
    private readonly LevelBlueprintTrialRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly GraphShowcaseConfig _config = new();

    public LevelBlueprintTrialPresentationSystem(LevelBlueprintTrialRuntime runtime, DebugDrawCommandBuffer debugDraw)
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
        int phase = _runtime.Director?.Phase ?? 0;
        GraphShowcaseStagePresenter.DrawPhasePips(_debugDraw, Math.Max(1, phase), 3);
        GraphShowcaseStagePresenter.DrawTriggerRing(_debugDraw, 0f, -8f, 1.8f, armed: phase == 0);
        GraphShowcaseStagePresenter.DrawGateBar(_debugDraw, y: 4f, halfWidth: 5f, open: _runtime.GateOpen);

        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _runtime.MarkerX, _runtime.MarkerY, 0.5f, GraphShowcaseStagePresenter.CasterColor, 0.16f);

        for (int i = 0; i < _runtime.MobSlotCount; i++)
        {
            if (!_runtime.MobAlive[i]) continue;
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw, _runtime.MobX[i], _runtime.MobY[i], 0.4f, GraphShowcaseStagePresenter.EnemyColor);
        }

        if (_config.ShowCrowdBand)
        {
            GraphShowcaseStagePresenter.DrawCrowdBand(_debugDraw, _config.CrowdBandCount);
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs, _config.ThinkBudgetMs);
    }
}
