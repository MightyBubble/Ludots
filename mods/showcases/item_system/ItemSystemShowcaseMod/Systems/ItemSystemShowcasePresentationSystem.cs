using Arch.Core;
using Arch.System;
using ItemSystemShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace ItemSystemShowcaseMod.Systems;

internal sealed class ItemSystemShowcasePresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly ItemSystemShowcaseRuntime _runtime;

    public ItemSystemShowcasePresentationSystem(GameEngine engine, ItemSystemShowcaseRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        _runtime.Update(_engine, dt);
    }
}
