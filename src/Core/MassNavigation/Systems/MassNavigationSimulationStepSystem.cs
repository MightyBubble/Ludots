using System;
using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationSimulationStepSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private MassNavigationSimulationRuntime? _cachedObserverRuntime;
    private Action<double>? _observeStepPrep;
    private Action<double>? _observeLocalSteering;
    private Action<double>? _observeHardResolve;
    private Action<double>? _observeFlowFieldRebuild;

    public MassNavigationSimulationStepSystem(GameEngine engine)
    {
        _engine = engine;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        EnsureObserverCache(simulation);
        if (!simulation.AgentState.HasBoundAgents(simulation.MassNavigationFlow.UnitCount))
        {
            return;
        }

        int stepsToRun = simulation.CadenceScheduler.BeginFixedTick(dt);
        for (int stepIndex = 0; stepIndex < stepsToRun; stepIndex++)
        {
            MassNavigationCadenceStep step = simulation.CadenceScheduler.NextSimulationStep();
            simulation.ObserveSimTick();

            if (step.UpdateTargets)
            {
                long targetStart = Stopwatch.GetTimestamp();
                simulation.NavGroupRuntime.UpdateTargets(
                    simulation.MassNavigationFlow,
                    simulation.FrameIndex);
                ApplyRouteExecutionTargets(simulation);
                simulation.ObserveGroupTargetUpdate((Stopwatch.GetTimestamp() - targetStart) * 1000.0 / Stopwatch.Frequency);
            }

            if (simulation.MassNavigationFlow.AdvanceFlowPipeline(
                    simulation.FlowTuning,
                    step.RefreshFlow,
                    step.RefreshCrowd,
                    step.RefreshObstacles,
                    _observeFlowFieldRebuild!))
            {
                simulation.MarkFlowReconcile();
            }

            long start = Stopwatch.GetTimestamp();
            simulation.MassNavigationFlow.Step(
                step.SimulationDt,
                _engine.World,
                simulation.NavGroupRuntime,
                step.RunHardResolve,
                simulation.Cadence.HardResolveCandidateThresholdAgents,
                _observeStepPrep!,
                _observeLocalSteering!,
                _observeHardResolve!);
            simulation.ObserveSimStep((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

            if (step.SyncEntities)
            {
                start = Stopwatch.GetTimestamp();
                // Displaced agents: re-ingest the externally committed WorldPositionCm
                // before syncing, so neighbors keep avoiding the displaced agent at its real pose
                // while SyncEntities skips writing it.
                simulation.MassNavigationFlow.SyncDisplacedAgentPoses(_engine.World, simulation.AgentState);
                simulation.MassNavigationFlow.SyncEntities(_engine.World, simulation.AgentState);
                simulation.ObserveEntitySync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }
        }
    }

    private void EnsureObserverCache(MassNavigationSimulationRuntime simulation)
    {
        if (ReferenceEquals(_cachedObserverRuntime, simulation))
        {
            return;
        }

        _cachedObserverRuntime = simulation;
        _observeStepPrep = simulation.ObserveStepPrep;
        _observeLocalSteering = simulation.ObserveLocalSteering;
        _observeHardResolve = simulation.ObserveHardResolve;
        _observeFlowFieldRebuild = simulation.ObserveFlowFieldRebuild;
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
