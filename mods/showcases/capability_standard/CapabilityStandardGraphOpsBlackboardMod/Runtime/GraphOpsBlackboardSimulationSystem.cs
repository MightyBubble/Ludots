using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsBlackboardMod.Runtime;

public sealed class GraphOpsBlackboardSimulationSystem : BaseSystem<World, float>
{
  private readonly GraphOpsBlackboardRuntime _runtime;

  public GraphOpsBlackboardSimulationSystem(GameEngine engine, GraphOpsBlackboardRuntime runtime) : base(engine.World)
    => _runtime = runtime;

  public override void Update(in float dt) => _runtime.Tick(dt);
}
