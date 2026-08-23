using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphBehaviorIntegrationMod.Runtime;

public sealed class GraphBehaviorIntegrationSimulationSystem : BaseSystem<World, float>
{
    private readonly GraphBehaviorIntegrationRuntime _runtime;
    public GraphBehaviorIntegrationSimulationSystem(GameEngine engine, GraphBehaviorIntegrationRuntime runtime)
        : base(engine.World) => _runtime = runtime;
    public override void Update(in float dt) => _runtime.Tick(dt);
}
