using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsAttrMod.Runtime;

public sealed class GraphOpsAttrSimulationSystem : BaseSystem<World, float>
{
    private readonly GraphOpsAttrRuntime _runtime;

    public GraphOpsAttrSimulationSystem(GameEngine engine, GraphOpsAttrRuntime runtime) : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.Tick(dt);
}
