using Arch.Core;
using Arch.System;
using AssociationStressShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;

namespace AssociationStressShowcaseMod.Systems;

internal sealed class AssociationStressSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly AssociationStressShowcaseRuntime _runtime;

    public AssociationStressSimulationSystem(GameEngine engine, AssociationStressShowcaseRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        if (!AssociationStressIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        if (_engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(AssociationStressIds.IncreaseScaleActionId))
            {
                _runtime.IncreaseScale(_engine);
            }

            if (input.PressedThisFrame(AssociationStressIds.DecreaseScaleActionId))
            {
                _runtime.DecreaseScale(_engine);
            }

            if (input.PressedThisFrame(AssociationStressIds.TogglePulseActionId))
            {
                _runtime.TogglePulse(_engine);
            }

            if (input.PressedThisFrame(AssociationStressIds.CompactActionId))
            {
                _runtime.Compact(_engine);
            }
        }

        _runtime.Advance(_engine);
    }
}
