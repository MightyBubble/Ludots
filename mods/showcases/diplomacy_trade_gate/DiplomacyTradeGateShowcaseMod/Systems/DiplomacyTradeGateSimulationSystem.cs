using Arch.Core;
using Arch.System;
using DiplomacyTradeGateShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace DiplomacyTradeGateShowcaseMod.Systems;

internal sealed class DiplomacyTradeGateSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly DiplomacyTradeGateRuntime _runtime;

    public DiplomacyTradeGateSimulationSystem(GameEngine engine, DiplomacyTradeGateRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        if (!DiplomacyTradeGateIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(DiplomacyTradeGateIds.TryTradeActionId))
            {
                _runtime.TryTrade(_engine);
            }

            if (input.PressedThisFrame(DiplomacyTradeGateIds.SignPactActionId))
            {
                _runtime.SignPact(_engine);
            }

            if (input.PressedThisFrame(DiplomacyTradeGateIds.EmbargoActionId))
            {
                _runtime.DeclareEmbargo(_engine);
            }

            if (input.PressedThisFrame(DiplomacyTradeGateIds.ClearEmbargoActionId))
            {
                _runtime.ClearEmbargo(_engine);
            }
        }
    }
}
