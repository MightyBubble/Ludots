using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardBehaviorTreeArenaMod.Runtime;

public sealed class BehaviorTreeArenaSimulationSystem : BaseSystem<World, float>
{
    private readonly BehaviorTreeArenaRuntime _runtime;

    public BehaviorTreeArenaSimulationSystem(GameEngine engine, BehaviorTreeArenaRuntime runtime)
        : base(engine.World)
    {
        _runtime = runtime;
    }

    public override void Update(in float dt) => _runtime.Tick(dt);
}
