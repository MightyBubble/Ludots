using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardSkillGraphSandboxMod.Runtime;

public sealed class SkillGraphSandboxSimulationSystem : BaseSystem<World, float>
{
    private readonly SkillGraphSandboxRuntime _runtime;
    public SkillGraphSandboxSimulationSystem(GameEngine engine, SkillGraphSandboxRuntime runtime) : base(engine.World)
        => _runtime = runtime;
    public override void Update(in float dt) => _runtime.Tick(dt);
}
