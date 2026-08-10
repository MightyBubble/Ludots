using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;

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
        if (world == null) return;
        GraphShowcaseDebugPresenter.DrawAgentDots(
            _debugDraw,
            world.Count,
            i => (byte)world.Statuses[i]);
    }
}
