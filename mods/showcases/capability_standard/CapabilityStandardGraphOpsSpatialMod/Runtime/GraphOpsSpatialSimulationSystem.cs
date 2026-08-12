using Arch.System;
using Arch.Core;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsSpatialMod.Runtime;

public sealed class GraphOpsSpatialSimulationSystem : BaseSystem<World, float>
{
    private readonly GraphOpsSpatialRuntime _runtime;

    public GraphOpsSpatialSimulationSystem(GameEngine engine, GraphOpsSpatialRuntime runtime) : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.Tick(dt);
}
