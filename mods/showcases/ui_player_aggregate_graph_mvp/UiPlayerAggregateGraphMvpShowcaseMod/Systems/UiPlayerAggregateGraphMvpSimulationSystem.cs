using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

namespace UiPlayerAggregateGraphMvpShowcaseMod.Systems;

internal sealed class UiPlayerAggregateGraphMvpSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly UiPlayerAggregateGraphMvpRuntime _runtime;

    public UiPlayerAggregateGraphMvpSimulationSystem(GameEngine engine, UiPlayerAggregateGraphMvpRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        _runtime.Tick(_engine);
    }
}
