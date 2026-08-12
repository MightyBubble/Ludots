using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsRelMod.Runtime;

public sealed class GraphOpsRelSimulationSystem : BaseSystem<World, float>
{
    private readonly GraphOpsRelRuntime _runtime;
    public GraphOpsRelSimulationSystem(GameEngine engine, GraphOpsRelRuntime runtime) : base(engine.World)
        => _runtime = runtime;
    public override void Update(in float dt) => _runtime.Tick(dt);
}
