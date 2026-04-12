using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using MassNavFlowPlaygroundMod.Runtime;

namespace MassNavFlowPlaygroundMod.Systems;

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
        if (!MassNavFlowPlaygroundIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        _simulation.ObserveSimTick();

        long start = Stopwatch.GetTimestamp();
        _simulation.NavGroupRuntime.UpdateTargets(
            _simulation.FlowSimulation,
            _simulation.AgentState,
            _simulation.SelectedEntities,
            _simulation.FrameIndex);
        _simulation.ObserveFormationTargets((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

        if (_simulation.FlowSimulation.AdvanceFlowPipeline(_simulation.FlowTuning, _simulation.FrameIndex, _simulation.ObserveFlowFieldRebuild))
        {
            _simulation.MarkFlowReconcile();
        }

        start = Stopwatch.GetTimestamp();
        _simulation.FlowSimulation.Step(
            dt,
            _simulation.NavGroupRuntime,
            _simulation.ObserveStepPrep,
            _simulation.ObserveLocalSteering,
            _simulation.ObserveHardResolve);
        _simulation.ObserveSimStep((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

        start = Stopwatch.GetTimestamp();
        _simulation.FlowSimulation.SyncWorldPositions(_engine.World);
        _simulation.ObserveEntitySync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }
}
