using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardAbilityFeatureGalleryMod.Runtime;

public sealed class AbilityFeatureGalleryCastOutcomeSystem : BaseSystem<World, float>
{
    private readonly AbilityFeatureGalleryRuntime _runtime;

    public AbilityFeatureGalleryCastOutcomeSystem(GameEngine engine, AbilityFeatureGalleryRuntime runtime)
        : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.ObserveCastOutcomes();
}
