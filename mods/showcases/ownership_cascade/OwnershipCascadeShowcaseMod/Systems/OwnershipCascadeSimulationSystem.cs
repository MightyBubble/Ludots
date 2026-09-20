using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using OwnershipCascadeShowcaseMod.Runtime;

namespace OwnershipCascadeShowcaseMod.Systems;

internal sealed class OwnershipCascadeSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly OwnershipCascadeRuntime _runtime;

    public OwnershipCascadeSimulationSystem(GameEngine engine, OwnershipCascadeRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        if (!OwnershipCascadeIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(OwnershipCascadeIds.CaptureActionId))
            {
                _runtime.CaptureForSolePossessedRep(_engine);
            }

            if (input.PressedThisFrame(OwnershipCascadeIds.ReclaimActionId))
            {
                _runtime.ReclaimForEnemy(_engine);
            }
        }
    }
}
