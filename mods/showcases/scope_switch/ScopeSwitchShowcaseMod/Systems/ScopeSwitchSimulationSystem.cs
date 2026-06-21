using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using ScopeSwitchShowcaseMod.Runtime;

namespace ScopeSwitchShowcaseMod.Systems;

internal sealed class ScopeSwitchSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly ScopeSwitchRuntime _runtime;

    public ScopeSwitchSimulationSystem(GameEngine engine, ScopeSwitchRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        _runtime.Update(_engine);
    }
}
