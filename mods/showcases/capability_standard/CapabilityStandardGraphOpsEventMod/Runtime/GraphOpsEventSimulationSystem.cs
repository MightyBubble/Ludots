using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsEventMod.Runtime;

public sealed class GraphOpsEventSimulationSystem : BaseSystem<World, float>
{
    private readonly GraphOpsEventRuntime _runtime;

    public GraphOpsEventSimulationSystem(GameEngine engine, GraphOpsEventRuntime runtime) : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.Tick(dt);
}
