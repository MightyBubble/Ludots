using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavFormationSystem : ISystem<float>
{
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
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        _simulation.NavGroupRuntime.UpdateTargets(
            _simulation.WebParity,
            _simulation.AgentState,
            _simulation.SelectedEntities,
            _simulation.FrameIndex);
        _simulation.ObserveFormationTargets((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

        start = Stopwatch.GetTimestamp();
        _simulation.WebParity.Step(dt, _simulation.NavGroupRuntime, _simulation.ObserveHardResolve);
        _simulation.ObserveSimStep((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

        start = Stopwatch.GetTimestamp();
        _simulation.WebParity.SyncEntities(_engine.World, _simulation.AgentState);
        _simulation.ObserveEntitySync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }
}
