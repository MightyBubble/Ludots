using Arch.Core;
using Arch.System;
using DiplomacyTradeGateShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace DiplomacyTradeGateShowcaseMod.Systems;

internal sealed class DiplomacyTradeGatePresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly DiplomacyTradeGateRuntime _runtime;

    public DiplomacyTradeGatePresentationSystem(GameEngine engine, DiplomacyTradeGateRuntime runtime)
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
