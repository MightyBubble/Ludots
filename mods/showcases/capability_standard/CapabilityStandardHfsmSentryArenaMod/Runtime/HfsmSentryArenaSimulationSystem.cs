using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardHfsmSentryArenaMod.Runtime;

public sealed class HfsmSentryArenaSimulationSystem : BaseSystem<World, float>
{
    private readonly HfsmSentryArenaRuntime _runtime;
    public HfsmSentryArenaSimulationSystem(GameEngine engine, HfsmSentryArenaRuntime runtime) : base(engine.World)
        => _runtime = runtime;
    public override void Update(in float dt) => _runtime.Tick(dt);
}
