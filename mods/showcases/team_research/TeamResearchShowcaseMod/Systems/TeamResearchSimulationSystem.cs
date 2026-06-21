using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using TeamResearchShowcaseMod.Runtime;

namespace TeamResearchShowcaseMod.Systems;

internal sealed class TeamResearchSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly TeamResearchRuntime _runtime;

    public TeamResearchSimulationSystem(GameEngine engine, TeamResearchRuntime runtime)
        : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        if (!TeamResearchIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(TeamResearchIds.AddMemberActionId))
            {
                _runtime.AddNextMember(_engine);
            }

            if (input.PressedThisFrame(TeamResearchIds.ResearchPulseActionId))
            {
                _runtime.ResearchPulse(_engine);
            }

            if (input.PressedThisFrame(TeamResearchIds.ResetActionId))
            {
                _runtime.ResetResearch(_engine);
            }
        }

        _runtime.RefreshPanel(_engine);
    }
}
