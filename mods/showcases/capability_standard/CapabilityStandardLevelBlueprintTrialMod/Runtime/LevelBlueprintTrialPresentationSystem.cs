using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardLevelBlueprintTrialMod.Runtime;

internal sealed class LevelBlueprintTrialPresentationSystem : ISystem<float>
{
    private readonly LevelBlueprintTrialRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

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
        _debugDraw.Clear();
        if (_runtime.Director == null || _runtime.PosX.Length == 0) return;
        GraphShowcaseDebugPresenter.DrawPhaseRings(_debugDraw, _runtime.Director.Phase);
        GraphShowcaseDebugPresenter.DrawAgentDotsAtPositions(
            _debugDraw,
            _runtime.VisibleUnits,
            _runtime.PosX,
            _runtime.PosY,
            _ => (byte)(1 + _runtime.Director.Phase));
        GraphShowcaseDebugPresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs);
    }
}
