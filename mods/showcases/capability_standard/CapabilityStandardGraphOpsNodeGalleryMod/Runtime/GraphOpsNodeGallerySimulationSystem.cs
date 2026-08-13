using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public sealed class GraphOpsNodeGallerySimulationSystem : BaseSystem<World, float>
{
    private readonly GraphOpsNodeGalleryRuntime _runtime;

    public GraphOpsNodeGallerySimulationSystem(GameEngine engine, GraphOpsNodeGalleryRuntime runtime)
        : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.Tick(dt);
}
