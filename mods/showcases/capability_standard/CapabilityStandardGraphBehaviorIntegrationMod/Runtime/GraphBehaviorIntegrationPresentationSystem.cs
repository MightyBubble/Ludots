using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphBehaviorIntegrationMod.Runtime;

internal sealed class GraphBehaviorIntegrationPresentationSystem : ISystem<float>
{
    private readonly GraphBehaviorIntegrationRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

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
        _debugDraw.Clear();
        if (_runtime.Hfsm == null || _runtime.PosX.Length == 0) return;
        if (_runtime.Level != null)
        {
            GraphShowcaseDebugPresenter.DrawPhaseRings(_debugDraw, _runtime.Level.Phase);
        }

        GraphShowcaseDebugPresenter.DrawAgentDotsAtPositions(
            _debugDraw,
            _runtime.PosX.Length,
            _runtime.PosX,
            _runtime.PosY,
            i => (byte)_runtime.Hfsm.GetLeafState(i),
            maxDots: 800);
        GraphShowcaseDebugPresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs);
    }
}
