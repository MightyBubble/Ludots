using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardHfsmSentryArenaMod.Runtime;

internal sealed class HfsmSentryArenaPresentationSystem : ISystem<float>
{
    private readonly HfsmSentryArenaRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

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
        _debugDraw.Clear();
        HfsmWorld? world = _runtime.World;
        if (world == null || _runtime.PosX.Length == 0) return;
        GraphShowcaseDebugPresenter.DrawAgentDotsAtPositions(
            _debugDraw,
            world.Count,
            _runtime.PosX,
            _runtime.PosY,
            i => (byte)world.GetLeafState(i));
        GraphShowcaseDebugPresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs);
    }
}
