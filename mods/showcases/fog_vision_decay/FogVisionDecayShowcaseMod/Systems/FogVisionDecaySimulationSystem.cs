using Arch.Core;
using Arch.System;
using FogVisionDecayShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace FogVisionDecayShowcaseMod.Systems;

internal sealed class FogVisionDecaySimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly FogVisionDecayShowcaseRuntime _runtime;

    public FogVisionDecaySimulationSystem(GameEngine engine, FogVisionDecayShowcaseRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        if (!FogVisionDecayIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(FogVisionDecayIds.TogglePatrolActionId))
            {
                _runtime.TogglePatrol();
            }

            if (input.PressedThisFrame(FogVisionDecayIds.StepPatrolActionId))
            {
                _runtime.StepPatrol(_engine);
            }

            if (input.PressedThisFrame(FogVisionDecayIds.CompactActionId))
            {
                _runtime.Compact(_engine);
            }
        }

        _runtime.Advance(_engine);
    }
}
