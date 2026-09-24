using System;
using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationSimulationStepSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private MassNavigationSimulationRuntime? _cachedObserverRuntime;
    private Action<double>? _observeStepPrep;
    private Action<double>? _observeLocalSteering;
    private Action<double>? _observeHardResolve;
    private Action<double>? _observeFlowFieldRebuild;
    private readonly PresentationTimingDiagnostics? _timingDiagnostics;

    public MassNavigationSimulationStepSystem(GameEngine engine)
    {
        _engine = engine;
        _timingDiagnostics = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
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
            if (step.AgentSliceRoundStart)
            {
                simulation.ObserveSimTick();
            }

            if (step.UpdateTargets && step.AgentSliceRoundStart)
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
                    step.AgentSliceIndex,
                    step.AgentSliceCount,
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
                step.AgentSliceIndex,
                step.AgentSliceCount,
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

        _timingDiagnostics?.ObserveMassNavigation(
            simulation.LastGroupTargetUpdateMs,
            simulation.LastFlowFieldRebuildMs,
            simulation.LastStepPrepMs,
            simulation.LastLocalSteeringMs,
            simulation.LastSimStepMs,
            simulation.LastHardResolveMs,
            simulation.LastEntitySyncMs,
            simulation.MassNavigationFlow.PendingEntitySyncCount);
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
        if (result.Applied)
        {
            return;
        }

        // An unreachable goal is an ordinary gameplay outcome, not a fault: the player may click
        // across an unwalkable gap. The agent that raised it holds position and the rest of the
        // batch still runs, so one bad order cannot kill the frame loop. Genuine wiring faults
        // (missing service, unbound agent, capacity breach) still fail fast.
        if (result.Status == MassNavigationRouteSinkStatus.SolveFailed ||
            result.Status == MassNavigationRouteSinkStatus.EmptyPath)
        {
            Ludots.Core.Diagnostics.Log.Warn(
                in Ludots.Core.Diagnostics.LogChannels.Engine,
                $"MassNavigation route not solvable for order {result.OrderToken}, agent {result.AgentIndex}: " +
                $"status={result.Status}, pathStatus={result.PathStatus}, domain={result.ResolvedDomain}, " +
                $"errorCode={result.ErrorCode}, start=({result.StartWorldCm.X:0.###},{result.StartWorldCm.Y:0.###}), " +
                $"goal=({result.DestinationWorldCm.X:0.###},{result.DestinationWorldCm.Y:0.###}); agent holds position.");
            return;
        }

        throw new System.InvalidOperationException(
            $"MassNavigation route execution failed for order {result.OrderToken}, agent {result.AgentIndex}: status={result.Status}, pathStatus={result.PathStatus}, domain={result.ResolvedDomain}, errorCode={result.ErrorCode}, start=({result.StartWorldCm.X:0.###},{result.StartWorldCm.Y:0.###}), goal=({result.DestinationWorldCm.X:0.###},{result.DestinationWorldCm.Y:0.###}).");
    }

}
