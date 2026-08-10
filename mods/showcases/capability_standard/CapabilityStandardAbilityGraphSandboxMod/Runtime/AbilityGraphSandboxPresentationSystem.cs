using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardAbilityGraphSandboxMod.Runtime;

internal sealed class AbilityGraphSandboxPresentationSystem : ISystem<float>
{
    private readonly AbilityGraphSandboxRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

    public AbilityGraphSandboxPresentationSystem(AbilityGraphSandboxRuntime runtime, DebugDrawCommandBuffer debugDraw)
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
        if (_runtime.TargetCount == 0) return;
        GraphShowcaseDebugPresenter.DrawAgentDotsAtPositions(
            _debugDraw,
            _runtime.TargetCount,
            _runtime.PosX,
            _runtime.PosY,
            i => _runtime.Flash[i] > 0 ? (byte)2 : (byte)4);
        GraphShowcaseDebugPresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs);
    }
}
