using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using TeamResearchShowcaseMod.Runtime;

namespace TeamResearchShowcaseMod.Systems;

internal sealed class TeamResearchPresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly TeamResearchRuntime _runtime;

    public TeamResearchPresentationSystem(GameEngine engine, TeamResearchRuntime runtime)
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
