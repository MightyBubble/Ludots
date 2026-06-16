using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationFormationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationFormationSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        _simulation.AdvanceCommandFocusHoldTick();
        if (!_simulation.AgentState.HasBoundAgents(_simulation.MassFlow.UnitCount))
        {
            return;
        }

        int stepsToRun = _simulation.CadenceScheduler.BeginFixedTick(dt);
        for (int stepIndex = 0; stepIndex < stepsToRun; stepIndex++)
        {
            MassNavigationCadenceStep step = _simulation.CadenceScheduler.NextSimulationStep();
            _simulation.ObserveSimTick();

            if (step.UpdateTargets)
            {
                long targetStart = Stopwatch.GetTimestamp();
                _simulation.NavGroupRuntime.UpdateTargets(
                    _engine.World,
                    _simulation.MassFlow,
                    _simulation.AgentState,
                    _simulation.SelectedEntities,
                    _simulation.FrameIndex,
                    _simulation.Cadence.TargetUpdateMemberBudgetPerStep);
                _simulation.ObserveFormationTargets((Stopwatch.GetTimestamp() - targetStart) * 1000.0 / Stopwatch.Frequency);
            }

            if (_simulation.MassFlow.AdvanceFlowPipeline(
                    _simulation.FlowTuning,
                    step.RefreshFlow,
                    step.RefreshCrowd,
                    step.RefreshObstacles,
                    _simulation.ObserveFlowFieldRebuild))
            {
                _simulation.MarkFlowReconcile();
            }

            long start = Stopwatch.GetTimestamp();
            _simulation.MassFlow.Step(
                step.SimulationDt,
                _engine.World,
                _simulation.NavGroupRuntime,
                step.RunHardResolve,
                _simulation.Cadence.HardResolveCandidateThresholdAgents,
                _simulation.ObserveStepPrep,
                _simulation.ObserveLocalSteering,
                _simulation.ObserveHardResolve);
            _simulation.ObserveSimStep((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

            if (step.SyncEntities)
            {
                start = Stopwatch.GetTimestamp();
                _simulation.MassFlow.SyncEntities(_engine.World, _simulation.AgentState);
                _simulation.ObserveEntitySync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }
        }
    }

}


