using Arch.Core;
using Arch.System;
using AssociationStressShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace AssociationStressShowcaseMod.Systems;

internal sealed class AssociationStressPresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly AssociationStressShowcaseRuntime _runtime;

    public AssociationStressPresentationSystem(GameEngine engine, AssociationStressShowcaseRuntime runtime)
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
