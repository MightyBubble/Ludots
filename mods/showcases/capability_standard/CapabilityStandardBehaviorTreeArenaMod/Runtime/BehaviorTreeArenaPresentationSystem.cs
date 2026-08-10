using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardBehaviorTreeArenaMod.Runtime;

internal sealed class BehaviorTreeArenaPresentationSystem : ISystem<float>
{
    private readonly BehaviorTreeArenaRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

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
        _debugDraw.Clear();
        BehaviorTreeWorld? world = _runtime.World;
        if (world == null || _runtime.PosX.Length == 0)
        {
            return;
        }

        GraphShowcaseDebugPresenter.DrawAgentDotsAtPositions(
            _debugDraw,
            world.Count,
            _runtime.PosX,
            _runtime.PosY,
            i => (byte)world.Statuses[i]);
    }
}
