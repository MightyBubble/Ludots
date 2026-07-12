using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationSimulationStepSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationSimulationStepSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            return;
        }

        if (!_simulation.AgentState.HasBoundAgents(_simulation.MassNavigationFlow.UnitCount))
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
                    _simulation.MassNavigationFlow,
                    _simulation.FrameIndex);
                ApplyRouteExecutionTargets();
                _simulation.ObserveGroupTargetUpdate((Stopwatch.GetTimestamp() - targetStart) * 1000.0 / Stopwatch.Frequency);
            }

            if (_simulation.MassNavigationFlow.AdvanceFlowPipeline(
                    _simulation.FlowTuning,
                    step.RefreshFlow,
                    step.RefreshCrowd,
                    step.RefreshObstacles,
                    _simulation.ObserveFlowFieldRebuild))
            {
                _simulation.MarkFlowReconcile();
            }

            long start = Stopwatch.GetTimestamp();
            _simulation.MassNavigationFlow.Step(
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
                _simulation.MassNavigationFlow.SyncEntities(_engine.World, _simulation.AgentState);
                _simulation.ObserveEntitySync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }
        }
    }

    private void ApplyRouteExecutionTargets()
    {
        MassNavigationRouteExecutionSink? routeSink = _engine.GetService(MassNavigationKeys.RouteExecutionSink);
        if (routeSink == null || routeSink.ActiveRouteCount <= 0)
        {
            return;
        }

        MassNavigationRouteSinkResult result = routeSink.TryApplyTrackedRouteTargets(
            _simulation,
            _engine.World);
        if (!result.Applied)
        {
            throw new System.InvalidOperationException(
                $"MassNavigation route execution failed for order {result.OrderToken}, agent {result.AgentIndex}: status={result.Status}, pathStatus={result.PathStatus}, domain={result.ResolvedDomain}, errorCode={result.ErrorCode}.");
        }
    }

}
