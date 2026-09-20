using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardAbilityGraphSandboxMod.Runtime;

public sealed class AbilityGraphSandboxSimulationSystem : BaseSystem<World, float>
{
    private readonly AbilityGraphSandboxRuntime _runtime;
    public AbilityGraphSandboxSimulationSystem(GameEngine engine, AbilityGraphSandboxRuntime runtime) : base(engine.World)
        => _runtime = runtime;
    public override void Update(in float dt) => _runtime.Tick(dt);
}
