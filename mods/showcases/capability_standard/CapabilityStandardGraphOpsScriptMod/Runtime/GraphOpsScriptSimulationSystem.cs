using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsScriptMod.Runtime;

public sealed class GraphOpsScriptSimulationSystem : BaseSystem<World, float>
{
    private readonly GraphOpsScriptRuntime _runtime;

    public GraphOpsScriptSimulationSystem(GameEngine engine, GraphOpsScriptRuntime runtime) : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.Tick(dt);
}
