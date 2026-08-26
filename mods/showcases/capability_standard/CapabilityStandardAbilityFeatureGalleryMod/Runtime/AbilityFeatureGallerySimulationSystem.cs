using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardAbilityFeatureGalleryMod.Runtime;

public sealed class AbilityFeatureGallerySimulationSystem : BaseSystem<World, float>
{
    private readonly AbilityFeatureGalleryRuntime _runtime;

    public AbilityFeatureGallerySimulationSystem(GameEngine engine, AbilityFeatureGalleryRuntime runtime)
        : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.Tick(AbilityFeatureGalleryRuntime.FrameStep);
}
