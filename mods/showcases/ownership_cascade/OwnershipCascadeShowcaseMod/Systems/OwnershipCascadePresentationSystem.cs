using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using OwnershipCascadeShowcaseMod.Runtime;

namespace OwnershipCascadeShowcaseMod.Systems;

internal sealed class OwnershipCascadePresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly OwnershipCascadeRuntime _runtime;

    public OwnershipCascadePresentationSystem(GameEngine engine, OwnershipCascadeRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        _runtime.RefreshPanel(_engine);
    }
}
