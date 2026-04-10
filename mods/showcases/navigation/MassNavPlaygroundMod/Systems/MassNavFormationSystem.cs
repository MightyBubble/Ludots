using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using MassNavPlaygroundMod.Runtime;

namespace MassNavPlaygroundMod.Systems;

internal sealed class MassNavFormationSystem : ISystem<float>
{
    private const int GoalRadiusCm = 120;

    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavFormationSystem(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavPlaygroundIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        _simulation.FormationRuntime.UpdateGoals(
            _engine.World,
            _simulation.AgentState,
            _simulation.SelectedEntities,
            _simulation.FrameIndex,
            GoalRadiusCm);
    }
}
