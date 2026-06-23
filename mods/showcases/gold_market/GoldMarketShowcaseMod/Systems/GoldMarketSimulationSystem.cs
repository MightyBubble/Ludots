using Arch.Core;
using Arch.System;
using GoldMarketShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace GoldMarketShowcaseMod.Systems;

internal sealed class GoldMarketSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly GoldMarketRuntime _runtime;

    public GoldMarketSimulationSystem(GameEngine engine, GoldMarketRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        if (!GoldMarketIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(GoldMarketIds.BuyActionId))
            {
                _runtime.TryBuy(_engine);
            }

            if (input.PressedThisFrame(GoldMarketIds.ExpensiveActionId))
            {
                _runtime.TryExpensive(_engine);
            }

            if (input.PressedThisFrame(GoldMarketIds.FailureActionId))
            {
                _runtime.TryAtomicFailure(_engine);
            }

            if (input.PressedThisFrame(GoldMarketIds.RefillActionId))
            {
                _runtime.RefillGold(_engine);
            }
        }
    }
}
