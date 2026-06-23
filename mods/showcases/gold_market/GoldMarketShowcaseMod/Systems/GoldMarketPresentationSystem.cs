using Arch.Core;
using Arch.System;
using GoldMarketShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace GoldMarketShowcaseMod.Systems;

internal sealed class GoldMarketPresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly GoldMarketRuntime _runtime;

    public GoldMarketPresentationSystem(GameEngine engine, GoldMarketRuntime runtime)
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
