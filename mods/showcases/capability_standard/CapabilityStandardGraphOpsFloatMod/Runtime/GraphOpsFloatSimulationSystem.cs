using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsFloatMod.Runtime;

public sealed class GraphOpsFloatSimulationSystem : BaseSystem<World, float>
{
    private readonly GraphOpsFloatRuntime _runtime;

    public GraphOpsFloatSimulationSystem(GameEngine engine, GraphOpsFloatRuntime runtime) : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.Tick(dt);
}
