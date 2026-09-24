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
    private readonly bool _auditDisabled;
    private readonly bool _auditStatic = Environment.GetEnvironmentVariable("LUDOTS_AB_STATIC") == "1";
    private readonly bool _auditSparse = Environment.GetEnvironmentVariable("LUDOTS_AB_DENSITY") == "sparse";
    private bool _auditLayoutApplied;

    public MassNavigationSimulationStepSystem(GameEngine engine)
    {
        _engine = engine;
        _timingDiagnostics = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
        _auditDisabled = Environment.GetEnvironmentVariable("LUDOTS_AB_DISABLE_MASSNAV") == "1";
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (_auditDisabled)
        {
            return;
        }

        if (!MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        EnsureObserverCache(simulation);
        if (!simulation.AgentState.HasBoundAgents(simulation.MassNavigationFlow.UnitCount))
        {
            return;
        }

        if (!_auditLayoutApplied)
        {
            var flow = simulation.MassNavigationFlow;
            if (_auditSparse)
            {
                int columns = (int)Math.Ceiling(Math.Sqrt(flow.UnitCount));
                float spacing = (flow.FieldWidthCm - 600f) / columns;
                for (int i = 0; i < flow.UnitCount; i++)
                    flow.SetUnitPositionForTests(i, 300f + (i % columns + 0.5f) * spacing, 300f + (i / columns + 0.5f) * spacing);
            }
            if (_auditStatic)
                for (int i = 0; i < flow.UnitCount; i++) flow.HoldUnitAtCurrentPosition(i);
            _auditLayoutApplied = true;
        }
        int stepsToRun = simulation.CadenceScheduler.BeginFixedTick(dt);
        for (int stepIndex = 0; stepIndex < stepsToRun; stepIndex++)
        {
            MassNavigationCadenceStep step = simulation.CadenceScheduler.NextSimulationStep();
            simulation.ObserveSimTick();
            simulation.AuditFrameTotals[7]++;

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
            simulation.AuditFrameTotals[8] += simulation.MassNavigationFlow.LastAvoidanceNeighborCandidateCheckCount;
            if (step.RunHardResolve)
            {
                simulation.AuditFrameTotals[9] += simulation.LastHardResolvePairCheckCount;
                simulation.AuditFrameTotals[10] += simulation.LastHardResolveCandidateAgentCount;
                simulation.AuditFrameTotals[11] += simulation.LastHardResolvePenetratingPairCount;
            }
            simulation.AuditFrameTotals[12] += simulation.MassNavigationFlow.AuditMovedAgents;

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
        if (!result.Applied)
        {
            throw new System.InvalidOperationException(
                $"MassNavigation route execution failed for order {result.OrderToken}, agent {result.AgentIndex}: status={result.Status}, pathStatus={result.PathStatus}, domain={result.ResolvedDomain}, errorCode={result.ErrorCode}.");
        }
    }

}
