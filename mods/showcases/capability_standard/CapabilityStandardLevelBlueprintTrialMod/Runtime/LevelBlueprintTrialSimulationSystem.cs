using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardLevelBlueprintTrialMod.Runtime;

public sealed class LevelBlueprintTrialSimulationSystem : BaseSystem<World, float>
{
    private readonly LevelBlueprintTrialRuntime _runtime;
    public LevelBlueprintTrialSimulationSystem(GameEngine engine, LevelBlueprintTrialRuntime runtime) : base(engine.World)
        => _runtime = runtime;
    public override void Update(in float dt) => _runtime.Tick(dt);
}
