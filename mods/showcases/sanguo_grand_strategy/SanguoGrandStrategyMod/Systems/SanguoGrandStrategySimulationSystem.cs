using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using SanguoGrandStrategyMod.Runtime;

namespace SanguoGrandStrategyMod.Systems;

internal sealed class SanguoGrandStrategySimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly SanguoGrandStrategyRuntime _runtime;

    public SanguoGrandStrategySimulationSystem(GameEngine engine, SanguoGrandStrategyRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        _runtime.Tick(_engine, dt);
    }
}
