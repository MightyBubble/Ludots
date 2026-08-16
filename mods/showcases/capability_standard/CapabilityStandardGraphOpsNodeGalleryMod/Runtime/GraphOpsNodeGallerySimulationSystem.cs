using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public sealed class GraphOpsNodeGallerySimulationSystem : BaseSystem<World, float>
{
    private readonly GraphOpsNodeGalleryRuntime _runtime;
    private bool _sawFirstPlatformTick;

    public GraphOpsNodeGallerySimulationSystem(GameEngine engine, GraphOpsNodeGalleryRuntime runtime)
        : base(engine.World)
        => _runtime = runtime;

    public override void Update(in float dt)
    {
        // The first platform frame reports boot time as delta; unclamped it fast-forwards
        // several think beats before the first render, erasing the setup beat of recordings.
        if (!_sawFirstPlatformTick)
        {
            _sawFirstPlatformTick = true;
            return;
        }

        _runtime.Tick(MathF.Min(dt, 0.1f));
    }
}
