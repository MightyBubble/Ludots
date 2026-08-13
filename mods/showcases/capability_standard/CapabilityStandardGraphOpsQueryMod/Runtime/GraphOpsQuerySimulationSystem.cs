using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsQueryMod.Runtime;

public sealed class GraphOpsQuerySimulationSystem : BaseSystem<World, float>
{
    private readonly GraphOpsQueryRuntime _runtime;

    public GraphOpsQuerySimulationSystem(GameEngine engine, GraphOpsQueryRuntime runtime) : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.Tick(dt);
}
