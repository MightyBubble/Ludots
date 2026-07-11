using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationFormationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationRuntimeBinding _binding;
    private MassNavigationSimulationRuntime Simulation => _binding.RequireCurrent();

    public MassNavigationFormationSystem(GameEngine engine, MassNavigationRuntimeBinding binding)
    {
        _engine = engine;
        _binding = binding;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            return;
        }

        MassNavigationSimulationRuntime simulation = Simulation;
        if (!simulation.AgentState.HasBoundAgents(simulation.MassNavigationFlow.UnitCount))
        {
            return;
        }

        int stepsToRun = simulation.CadenceScheduler.BeginFixedTick(dt);
        bool timingEnabled = simulation.Telemetry.TimingEnabled;
        for (int stepIndex = 0; stepIndex < stepsToRun; stepIndex++)
        {
            MassNavigationCadenceStep step = simulation.CadenceScheduler.NextSimulationStep();
            simulation.Telemetry.MarkCadenceStep(in step);
            simulation.Telemetry.ObserveSimTick();

            if (step.UpdateTargets)
            {
                long targetStart = timingEnabled ? Stopwatch.GetTimestamp() : 0L;
                simulation.NavGroupRuntime.UpdateTargets(
                    simulation.MassNavigationFlow,
                    simulation.FrameIndex);
                ApplyRouteExecutionTargets(simulation);
                if (timingEnabled)
                {
                    simulation.Telemetry.ObserveFormationTargets((Stopwatch.GetTimestamp() - targetStart) * 1000.0 / Stopwatch.Frequency);
                }
            }

            if (simulation.MassNavigationFlow.AdvanceFlowPipeline(
                    simulation.FlowConfig,
                    step.RefreshFlow,
                    step.RefreshCrowd,
                    step.RefreshObstacles,
                    timingEnabled ? simulation.Telemetry.ObserveFlowFieldRebuild : null))
            {
                simulation.MarkFlowReconcile();
            }

            long start = timingEnabled ? Stopwatch.GetTimestamp() : 0L;
            simulation.MassNavigationFlow.Step(
                step.SimulationDt,
                _engine.World,
                simulation.NavGroupRuntime,
                step.RunHardResolve,
                simulation.Cadence.HardResolveCandidateThresholdAgents,
                timingEnabled ? simulation.Telemetry.ObserveStepPrep : null,
                timingEnabled ? simulation.Telemetry.ObserveLocalSteering : null,
                timingEnabled ? simulation.Telemetry.ObserveHardResolve : null);
            if (timingEnabled)
            {
                simulation.Telemetry.ObserveSimStep((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }

            if (step.SyncEntities)
            {
                start = timingEnabled ? Stopwatch.GetTimestamp() : 0L;
                simulation.MassNavigationFlow.SyncEntities(_engine.World, simulation.AgentState);
                if (timingEnabled)
                {
                    simulation.Telemetry.ObserveEntitySync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                }
            }
        }
    }

    private void ApplyRouteExecutionTargets(MassNavigationSimulationRuntime simulation)
    {
        MassNavigationRouteExecutionSink? routeSink = _engine.GetService(MassNavigationKeys.RouteExecutionSink);
        if (routeSink == null || routeSink.ActiveRouteCount <= 0)
        {
            return;
        }

        MassNavigationRouteSinkResult result = routeSink.TryApplyTrackedRouteTargets(
            simulation,
            _engine.World);
        if (!result.Applied)
        {
            throw new System.InvalidOperationException(
                $"MassNavigation route execution failed for order {result.OrderToken}, agent {result.AgentIndex}: status={result.Status}, pathStatus={result.PathStatus}, domain={result.ResolvedDomain}, errorCode={result.ErrorCode}.");
        }
    }

}
