using Arch.Core;
using Arch.System;
using FourXAssociationShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace FourXAssociationShowcaseMod.Systems;

internal sealed class FourXAssociationPresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly FourXAssociationRuntime _runtime;

    public FourXAssociationPresentationSystem(GameEngine engine, FourXAssociationRuntime runtime)
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
