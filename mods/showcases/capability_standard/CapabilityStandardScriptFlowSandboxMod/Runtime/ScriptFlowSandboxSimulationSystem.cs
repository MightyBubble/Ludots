using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardScriptFlowSandboxMod.Runtime;

public sealed class ScriptFlowSandboxSimulationSystem : BaseSystem<World, float>
{
    private readonly ScriptFlowSandboxRuntime _runtime;
    public ScriptFlowSandboxSimulationSystem(GameEngine engine, ScriptFlowSandboxRuntime runtime) : base(engine.World)
        => _runtime = runtime;
    public override void Update(in float dt) => _runtime.Tick(dt);
}
