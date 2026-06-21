using Arch.Core;
using Arch.System;
using FourXAssociationShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace FourXAssociationShowcaseMod.Systems;

internal sealed class FourXAssociationSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly FourXAssociationRuntime _runtime;

    public FourXAssociationSimulationSystem(GameEngine engine, FourXAssociationRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        if (!FourXAssociationIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        if (input.PressedThisFrame(FourXAssociationIds.ScoutActionId))
        {
            _runtime.Scout(_engine);
        }

        if (input.PressedThisFrame(FourXAssociationIds.AdvanceFogActionId))
        {
            _runtime.AdvanceFog(_engine);
        }

        if (input.PressedThisFrame(FourXAssociationIds.PactActionId))
        {
            _runtime.SignPact(_engine);
        }

        if (input.PressedThisFrame(FourXAssociationIds.TradeActionId))
        {
            _runtime.TryTrade(_engine);
        }

        if (input.PressedThisFrame(FourXAssociationIds.AddResearcherActionId))
        {
            _runtime.AddResearcher(_engine);
        }

        if (input.PressedThisFrame(FourXAssociationIds.ResearchActionId))
        {
            _runtime.ResearchPulse(_engine);
        }
    }
}
