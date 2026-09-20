using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public sealed class GraphOpsNodeGallerySimulationSystem : BaseSystem<World, float>
{
    // Fixed per-render-frame step: boot-time slow frames and platform dt spikes must not
    // shift think-beat frame numbers, or the recording timeline (setup -> waves -> hold)
    // loses its anchors entirely.
    private const float PlatformFrameStep = 1f / 60f;

    private readonly GraphOpsNodeGalleryRuntime _runtime;

    public GraphOpsNodeGallerySimulationSystem(GameEngine engine, GraphOpsNodeGalleryRuntime runtime)
        : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt) => _runtime.Tick(PlatformFrameStep);
}
