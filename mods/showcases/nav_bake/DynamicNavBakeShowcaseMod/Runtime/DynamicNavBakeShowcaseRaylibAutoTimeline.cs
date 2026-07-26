using System;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Scripting;

namespace DynamicNavBakeShowcaseMod.Runtime;

/// <summary>
/// Non-blocking Raylib auto-player for Dynamic NavBake showcase capture.
/// Owned by the presentation system instance (no static mutable state).
/// Invokes only public <see cref="DynamicNavBakeShowcaseActions"/>; never DrainUntilIdle / engine.Tick.
/// Initial/dynamic beats are in-motion route-ready; final completion follows authored
/// <see cref="DynamicNavBakeShowcaseRaylibAutoTimelineConfig.FinalCaptureCompletionMode"/>.
/// </summary>
internal sealed class DynamicNavBakeShowcaseRaylibAutoTimeline
{
    private enum Phase : byte
    {
        Boot = 0,
        WaitingAlgorithmEarliest = 1,
        WaitingAlgorithmCommit = 2,
        WaitingDeployStable = 3,
        WaitingInitialMove = 4,
        InitialReady = 5,
        WaitingDynamicAction = 6,
        WaitingDynamicCommit = 7,
        WaitingDynamicMove = 8,
        DynamicReady = 9,
        WaitingFinalAction = 10,
        WaitingFinalCommit = 11,
        WaitingFinalMove = 12,
        WaitingFinalArrival = 13,
        Completed = 14
    }

    private Phase _phase = Phase.Boot;
    private bool _algorithmEnsured;
    private bool _deployIssued;
    private bool _initialMoveIssued;
    private bool _dynamicActionIssued;
    private bool _dynamicMoveIssued;
    private bool _finalActionIssued;
    private bool _finalMoveIssued;
    private bool _initialScreenshotValidated;
    private bool _dynamicScreenshotValidated;
    private bool _finalScreenshotValidated;
    private bool _autoCameraEnsured;

    private ulong _commitBaselineGeneration;
    private int _quiescentFixedTickCount;
    private double _lastObservedFixedTotalTime = double.NaN;
    private ulong _actionBaselineGeneration;
    private ulong _dynamicCommittedGeneration;
    private ulong _initialRouteSignature;
    private ulong _dynamicRouteSignature;
    private ulong _finalRouteSignature;
    private bool _finalRouteSignatureRecorded;
    private int _arrivalStableFixedTickCount;
    private double _lastArrivalObservedFixedTotalTime = double.NaN;
    private bool _targetAlgorithmConfigured;
    private NavBakeAlgorithmKind _targetAlgorithm;

    public void Update(GameEngine engine, DynamicNavBakeShowcaseActions actions)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        if (actions == null)
        {
            throw new ArgumentNullException(nameof(actions));
        }

        if (!_targetAlgorithmConfigured)
        {
            string? raw = Environment.GetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey);
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            _targetAlgorithm = NavBakeNames.ParseAlgorithm(
                raw,
                DynamicNavBakeShowcaseIds.AutoTimelineEnvKey);
            _targetAlgorithmConfigured = true;
        }

        NavBakeAlgorithmKind targetAlgorithm = _targetAlgorithm;

        if (!engine.TryGetService(CoreServiceKeys.HostFrameIndex, out int frameIndex))
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline is enabled via {DynamicNavBakeShowcaseIds.AutoTimelineEnvKey} " +
                "but CoreServiceKeys.HostFrameIndex is missing. Raylib must set HostFrameIndex before engine.Tick; " +
                "no default frame is allowed.");
        }

        if (!actions.IsActive)
        {
            return;
        }

        DynamicNavBakeShowcaseConfig config = actions.ActiveConfig;
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = config.RaylibAutoTimeline
            ?? throw new InvalidOperationException(
                "Dynamic NavBake auto timeline requires DynamicNavBakeShowcaseConfig.raylibAutoTimeline.");

        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        RuntimeNavMeshTelemetryService telemetry = RequireTelemetry(engine);
        ThrowIfFailedBatch(telemetry, frameIndex, _phase, queue);

        if (!_autoCameraEnsured)
        {
            actions.EnsureAutoCaptureCameraActive(engine);
            _autoCameraEnsured = true;
        }
        else
        {
            actions.ApplyAutoCapturePlayerFraming(engine);
        }

        Advance(engine, actions, config, timeline, targetAlgorithm, frameIndex, queue, telemetry);
        ValidateScreenshotGates(engine, actions, config, timeline, targetAlgorithm, frameIndex, queue, telemetry);
    }

    private void Advance(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        DynamicNavBakeShowcaseConfig config,
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline,
        NavBakeAlgorithmKind targetAlgorithm,
        int frameIndex,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        RuntimeNavMeshTelemetryService telemetry)
    {
        while (true)
        {
            Phase before = _phase;
            switch (_phase)
            {
                case Phase.Boot:
                case Phase.WaitingAlgorithmEarliest:
                    if (frameIndex < timeline.AlgorithmRequestEarliestFrame)
                    {
                        _phase = Phase.WaitingAlgorithmEarliest;
                        return;
                    }

                    if (!IsNavStable(config, queue, telemetry))
                    {
                        _phase = Phase.WaitingAlgorithmEarliest;
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.AlgorithmCommitDeadlineFrame,
                            "algorithm-ready",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    if (IsAlgorithmReady(queue, targetAlgorithm))
                    {
                        _algorithmEnsured = true;
                        _phase = Phase.WaitingDeployStable;
                        break;
                    }

                    BeginGenerationFence(telemetry);
                    EnsureTargetAlgorithm(engine, actions, queue, targetAlgorithm);
                    _phase = Phase.WaitingAlgorithmCommit;
                    return;

                case Phase.WaitingAlgorithmCommit:
                    if (!TryPassGenerationFence(engine, config, timeline, queue, telemetry))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.AlgorithmCommitDeadlineFrame,
                            "algorithm-ready",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    if (!IsAlgorithmReady(queue, targetAlgorithm))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.AlgorithmCommitDeadlineFrame,
                            "algorithm-ready",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    _phase = Phase.WaitingDeployStable;
                    break;

                case Phase.WaitingDeployStable:
                    if (!_deployIssued)
                    {
                        InvokeRequired(
                            "TryDeploySquadNonBlocking",
                            actions.TryDeploySquadNonBlocking(engine, out string deployError),
                            deployError);
                        _deployIssued = true;
                    }

                    if (!IsNavStable(config, queue, telemetry))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.InitialScreenshotFrame,
                            "deploy-stable",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    _phase = Phase.WaitingInitialMove;
                    break;

                case Phase.WaitingInitialMove:
                    if (!_initialMoveIssued)
                    {
                        InvokeRequired(
                            "TryCommandMoveToGoal(initial)",
                            actions.TryCommandMoveToGoal(engine, out string moveError),
                            moveError);
                        _initialMoveIssued = true;
                    }

                    if (!TryObserveFormalPlayerRouteReady(engine, actions, config, targetAlgorithm, queue, out DynamicNavBakeShowcaseFormalPlayerRouteSnapshot initialRoute))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.InitialScreenshotFrame,
                            "initial-formal-ready",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    _initialRouteSignature = initialRoute.PlayerRouteSignature;
                    _phase = Phase.InitialReady;
                    break;

                case Phase.InitialReady:
                    if (!TryObserveFormalPlayerRouteReady(engine, actions, config, targetAlgorithm, queue, out DynamicNavBakeShowcaseFormalPlayerRouteSnapshot initialKeepAlive))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.InitialScreenshotFrame,
                            "initial-formal-ready",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    _initialRouteSignature = initialKeepAlive.PlayerRouteSignature;
                    if (frameIndex < timeline.DynamicActionFrame)
                    {
                        _phase = Phase.WaitingDynamicAction;
                        return;
                    }

                    _phase = Phase.WaitingDynamicAction;
                    break;

                case Phase.WaitingDynamicAction:
                    if (frameIndex < timeline.DynamicActionFrame)
                    {
                        return;
                    }

                    if (!_dynamicActionIssued)
                    {
                        BeginGenerationFence(telemetry);
                        _actionBaselineGeneration = _commitBaselineGeneration;
                        InvokeRequired(
                            "TryBuildWall",
                            actions.TryBuildWall(engine, out string wallError),
                            wallError);
                        _dynamicActionIssued = true;
                        _phase = Phase.WaitingDynamicCommit;
                        // Presentation may run many host frames before the next FixedStep.
                        // Never observe the old idle generation as a dynamic commit on this same call.
                        return;
                    }

                    _phase = Phase.WaitingDynamicCommit;
                    break;

                case Phase.WaitingDynamicCommit:
                    if (!TryPassGenerationFence(engine, config, timeline, queue, telemetry))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.DynamicCommitDeadlineFrame,
                            "dynamic-commit",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    _dynamicCommittedGeneration = ReadCommittedGeneration(telemetry);
                    _phase = Phase.WaitingDynamicMove;
                    break;

                case Phase.WaitingDynamicMove:
                    if (!_dynamicMoveIssued)
                    {
                        InvokeRequired(
                            "TryCommandMoveToGoal(dynamic)",
                            actions.TryCommandMoveToGoal(engine, out string dynamicMoveError),
                            dynamicMoveError);
                        _dynamicMoveIssued = true;
                    }

                    if (!TryObserveFormalPlayerRouteReady(engine, actions, config, targetAlgorithm, queue, out DynamicNavBakeShowcaseFormalPlayerRouteSnapshot dynamicRoute))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.DynamicScreenshotFrame,
                            "dynamic-formal-ready",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    _dynamicRouteSignature = dynamicRoute.PlayerRouteSignature;
                    if (_dynamicRouteSignature == _initialRouteSignature)
                    {
                        throw new InvalidOperationException(
                            "Dynamic NavBake auto timeline dynamic route signature must differ from the initial route after the sealed gate.");
                    }

                    _phase = Phase.DynamicReady;
                    break;

                case Phase.DynamicReady:
                    if (!TryObserveFormalPlayerRouteReady(engine, actions, config, targetAlgorithm, queue, out DynamicNavBakeShowcaseFormalPlayerRouteSnapshot dynamicKeepAlive))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.DynamicScreenshotFrame,
                            "dynamic-formal-ready",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    _dynamicRouteSignature = dynamicKeepAlive.PlayerRouteSignature;
                    if (frameIndex < timeline.FinalActionFrame)
                    {
                        _phase = Phase.WaitingFinalAction;
                        return;
                    }

                    _phase = Phase.WaitingFinalAction;
                    break;

                case Phase.WaitingFinalAction:
                    if (frameIndex < timeline.FinalActionFrame)
                    {
                        return;
                    }

                    if (!_finalActionIssued)
                    {
                        BeginGenerationFence(telemetry);
                        InvokeRequired(
                            "TryDemolishWall",
                            actions.TryDemolishWall(engine, out string demolishError),
                            demolishError);
                        _finalActionIssued = true;
                        _phase = Phase.WaitingFinalCommit;
                        return;
                    }

                    _phase = Phase.WaitingFinalCommit;
                    break;

                case Phase.WaitingFinalCommit:
                    if (!TryPassGenerationFence(engine, config, timeline, queue, telemetry))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.FinalCommitDeadlineFrame,
                            "final-commit",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    _phase = Phase.WaitingFinalMove;
                    break;

                case Phase.WaitingFinalMove:
                    if (!_finalMoveIssued)
                    {
                        InvokeRequired(
                            "TryCommandMoveToGoal(final)",
                            actions.TryCommandMoveToGoal(engine, out string finalMoveError),
                            finalMoveError);
                        _finalMoveIssued = true;
                    }

                    if (!TryObserveFormalPlayerRouteReady(engine, actions, config, targetAlgorithm, queue, out DynamicNavBakeShowcaseFormalPlayerRouteSnapshot finalRoute))
                    {
                        ThrowIfPastDeadline(
                            frameIndex,
                            timeline.FinalScreenshotFrame,
                            "final-formal-ready",
                            _phase,
                            queue,
                            telemetry,
                            targetAlgorithm);
                        return;
                    }

                    if (finalRoute.PlayerRouteSignature == _dynamicRouteSignature)
                    {
                        throw new InvalidOperationException(
                            "Dynamic NavBake auto timeline final route signature must differ from the sealed-gate dynamic route.");
                    }

                    _finalRouteSignature = finalRoute.PlayerRouteSignature;
                    _finalRouteSignatureRecorded = true;
                    if (timeline.ResolvedFinalCaptureCompletionMode ==
                        DynamicNavBakeShowcaseFinalCaptureCompletionMode.RouteReady)
                    {
                        _phase = Phase.Completed;
                        break;
                    }

                    _arrivalStableFixedTickCount = 0;
                    _lastArrivalObservedFixedTotalTime = double.NaN;
                    _phase = Phase.WaitingFinalArrival;
                    break;

                case Phase.WaitingFinalArrival:
                    if (!TryPassFinalArrival(engine, actions, timeline))
                    {
                        if (frameIndex >= timeline.FinalScreenshotFrame)
                        {
                            DynamicNavBakeShowcaseSquadArrivalSnapshot arrival =
                                actions.CaptureSquadArrivalSnapshot(engine);
                            throw new InvalidOperationException(
                                FormatDeadlineFailure(
                                    "final-arrival",
                                    frameIndex,
                                    timeline.FinalScreenshotFrame,
                                    _phase,
                                    queue,
                                    telemetry,
                                    targetAlgorithm) +
                                " " + FormatArrivalDiagnostics(
                                    arrival,
                                    timeline,
                                    _arrivalStableFixedTickCount));
                        }

                        return;
                    }

                    _phase = Phase.Completed;
                    break;

                case Phase.Completed:
                    return;

                default:
                    throw new InvalidOperationException($"Unknown Dynamic NavBake auto timeline phase '{_phase}'.");
            }

            if (_phase == before)
            {
                return;
            }
        }
    }

    private bool TryPassFinalArrival(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline)
    {
        DynamicNavBakeShowcaseSquadArrivalSnapshot arrival = actions.CaptureSquadArrivalSnapshot(engine);
        if (arrival.OutsideToleranceWithoutMoveCount > 0)
        {
            throw new InvalidOperationException(
                "Dynamic NavBake auto timeline final-arrival observed a squad member whose move order is gone " +
                "while outside the authored goal-slot tolerance. Vanished orders alone never count as arrival. " +
                $"slot={arrival.FirstOutsideSlotIndex}, " +
                $"actual=({arrival.FirstOutsideXCm},{arrival.FirstOutsideZCm}), " +
                $"expected=({arrival.FirstExpectedXCm},{arrival.FirstExpectedZCm}), " +
                $"toleranceCm={timeline.FinalArrivalMemberToleranceCm}, " +
                $"idleInTolerance={arrival.IdleInToleranceCount}/{arrival.SquadCount}, " +
                $"activeMoveOrders={arrival.ActiveMoveOrderCount}, " +
                $"outsideWithoutMove={arrival.OutsideToleranceWithoutMoveCount}.");
        }

        if (!arrival.AllIdleInTolerance)
        {
            _arrivalStableFixedTickCount = 0;
            _lastArrivalObservedFixedTotalTime = double.NaN;
            return false;
        }

        double fixedTotal = Time.FixedTotalTime;
        if (double.IsNaN(_lastArrivalObservedFixedTotalTime))
        {
            // First all-idle-in-tolerance observation: start counting only on later distinct FixedSteps.
            _lastArrivalObservedFixedTotalTime = fixedTotal;
            _arrivalStableFixedTickCount = 0;
            return false;
        }

        if (fixedTotal > _lastArrivalObservedFixedTotalTime)
        {
            _lastArrivalObservedFixedTotalTime = fixedTotal;
            _arrivalStableFixedTickCount++;
        }

        return _arrivalStableFixedTickCount >= timeline.FinalArrivalRequiredStableFixedTicks;
    }

    private static string FormatArrivalDiagnostics(
        in DynamicNavBakeShowcaseSquadArrivalSnapshot arrival,
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline,
        int observedStableFixedTicks)
    {
        double farthestDistanceCm = arrival.FarthestDistanceSquaredCm >= 0
            ? Math.Sqrt(arrival.FarthestDistanceSquaredCm)
            : double.NaN;
        return
            $"arrival(idleInTolerance={arrival.IdleInToleranceCount}/{arrival.SquadCount}, " +
            $"activeMoveOrders={arrival.ActiveMoveOrderCount}, " +
            $"outsideWithoutMove={arrival.OutsideToleranceWithoutMoveCount}, " +
            $"observedStableFixedTicks={observedStableFixedTicks}, " +
            $"requiredStableFixedTicks={timeline.FinalArrivalRequiredStableFixedTicks}, " +
            $"toleranceCm={timeline.FinalArrivalMemberToleranceCm}, " +
            $"farthestSlot={arrival.FarthestSlotIndex}, " +
            $"farthestDistanceCm={farthestDistanceCm:F1}, " +
            $"actual=({arrival.FarthestXCm},{arrival.FarthestZCm}), " +
            $"expected=({arrival.FarthestExpectedXCm},{arrival.FarthestExpectedZCm})).";
    }

    private void ValidateScreenshotGates(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        DynamicNavBakeShowcaseConfig config,
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline,
        NavBakeAlgorithmKind targetAlgorithm,
        int frameIndex,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        RuntimeNavMeshTelemetryService telemetry)
    {
        if (!_initialScreenshotValidated && frameIndex >= timeline.InitialScreenshotFrame)
        {
            if (_phase < Phase.InitialReady)
            {
                throw new InvalidOperationException(
                    FormatDeadlineFailure(
                        "initial-screenshot",
                        frameIndex,
                        timeline.InitialScreenshotFrame,
                        _phase,
                        queue,
                        telemetry,
                        targetAlgorithm) +
                    " Player timeline must reach InitialReady (selected algorithm, deployed moving squad, ready formal routes) before the initial screenshot frame.");
            }

            RequireObservedFormalPlayerRouteReady(engine, actions, config, targetAlgorithm, queue, "initial-screenshot");
            RequirePlayerFramingGate(engine, actions, timeline, "initial-screenshot");
            _initialScreenshotValidated = true;
        }

        if (!_dynamicScreenshotValidated && frameIndex >= timeline.DynamicScreenshotFrame)
        {
            if (_phase < Phase.DynamicReady)
            {
                throw new InvalidOperationException(
                    FormatDeadlineFailure(
                        "dynamic-screenshot",
                        frameIndex,
                        timeline.DynamicScreenshotFrame,
                        _phase,
                        queue,
                        telemetry,
                        targetAlgorithm) +
                    " Player timeline must reach DynamicReady before the dynamic screenshot frame.");
            }

            DynamicNavBakeShowcaseFormalPlayerRouteSnapshot route = RequireObservedFormalPlayerRouteReady(
                engine,
                actions,
                config,
                targetAlgorithm,
                queue,
                "dynamic-screenshot");
            if (actions.WallDeployedCount != config.Gate.SegmentCount)
            {
                throw new InvalidOperationException(
                    $"Dynamic NavBake auto timeline 'dynamic-screenshot' requires wallDeployedCount={config.Gate.SegmentCount}, got {actions.WallDeployedCount}.");
            }

            if (route.CommittedGeneration <= _actionBaselineGeneration)
            {
                throw new InvalidOperationException(
                    $"Dynamic NavBake auto timeline 'dynamic-screenshot' requires generation > action baseline " +
                    $"(generation={route.CommittedGeneration}, baseline={_actionBaselineGeneration}).");
            }

            if (!IsNavStable(config, queue, telemetry))
            {
                throw new InvalidOperationException(
                    "Dynamic NavBake auto timeline 'dynamic-screenshot' requires a stable nav queue/telemetry after the sealed generation.");
            }

            if (route.PlayerRouteSignature == _initialRouteSignature)
            {
                throw new InvalidOperationException(
                    "Dynamic NavBake auto timeline 'dynamic-screenshot' requires a changed player route signature after the sealed gate.");
            }

            RequirePlayerFramingGate(engine, actions, timeline, "dynamic-screenshot");
            _dynamicScreenshotValidated = true;
        }

        if (!_finalScreenshotValidated && frameIndex >= timeline.FinalScreenshotFrame)
        {
            if (_phase < Phase.Completed)
            {
                throw new InvalidOperationException(
                    FormatDeadlineFailure(
                        "final-screenshot",
                        frameIndex,
                        timeline.FinalScreenshotFrame,
                        _phase,
                        queue,
                        telemetry,
                        targetAlgorithm) +
                    " Player timeline must reach Completed before the final screenshot frame.");
            }

            if (actions.WallDeployedCount != 0)
            {
                throw new InvalidOperationException(
                    $"Dynamic NavBake auto timeline 'final-screenshot' requires wallDeployedCount=0, got {actions.WallDeployedCount}.");
            }

            ulong committedGeneration = ReadCommittedGeneration(telemetry);
            if (committedGeneration <= _dynamicCommittedGeneration)
            {
                throw new InvalidOperationException(
                    $"Dynamic NavBake auto timeline 'final-screenshot' requires a newer generation after demolish " +
                    $"(generation={committedGeneration}, dynamicCommitted={_dynamicCommittedGeneration}).");
            }

            if (!_finalRouteSignatureRecorded || _finalRouteSignature == _dynamicRouteSignature)
            {
                throw new InvalidOperationException(
                    "Dynamic NavBake auto timeline 'final-screenshot' requires a stored restored-route signature " +
                    "distinct from the sealed-gate dynamic route.");
            }

            if (timeline.ResolvedFinalCaptureCompletionMode ==
                DynamicNavBakeShowcaseFinalCaptureCompletionMode.Arrival)
            {
                // Arrival mode: route sink removes completed tokens, so do not require a live formal route.
                DynamicNavBakeShowcaseSquadArrivalSnapshot arrival = actions.CaptureSquadArrivalSnapshot(engine);
                if (arrival.OutsideToleranceWithoutMoveCount > 0)
                {
                    throw new InvalidOperationException(
                        "Dynamic NavBake auto timeline 'final-screenshot' arrival gate observed a member outside " +
                        "goal-slot tolerance without an active move order. " +
                        $"slot={arrival.FirstOutsideSlotIndex}, " +
                        $"actual=({arrival.FirstOutsideXCm},{arrival.FirstOutsideZCm}), " +
                        $"expected=({arrival.FirstExpectedXCm},{arrival.FirstExpectedZCm}), " +
                        $"toleranceCm={timeline.FinalArrivalMemberToleranceCm}.");
                }

                if (!arrival.AllIdleInTolerance)
                {
                    throw new InvalidOperationException(
                        "Dynamic NavBake auto timeline 'final-screenshot' requires all authored squad members idle " +
                        "and inside goal-slot tolerance. " +
                        $"idleInTolerance={arrival.IdleInToleranceCount}/{arrival.SquadCount}, " +
                        $"activeMoveOrders={arrival.ActiveMoveOrderCount}.");
                }
            }
            else
            {
                DynamicNavBakeShowcaseFormalPlayerRouteSnapshot route = RequireObservedFormalPlayerRouteReady(
                    engine,
                    actions,
                    config,
                    targetAlgorithm,
                    queue,
                    "final-screenshot");
                if (route.PlayerRouteSignature != _finalRouteSignature)
                {
                    throw new InvalidOperationException(
                        "Dynamic NavBake auto timeline 'final-screenshot' route-ready mode requires the live restored " +
                        "route signature to match the signature recorded at final formal ready.");
                }

                if (route.PlayerRouteSignature == _dynamicRouteSignature)
                {
                    throw new InvalidOperationException(
                        "Dynamic NavBake auto timeline 'final-screenshot' requires a restored route signature distinct from the sealed-gate route.");
                }
            }

            RequirePlayerFramingGate(engine, actions, timeline, "final-screenshot");
            _finalScreenshotValidated = true;
        }
    }

    private void BeginGenerationFence(RuntimeNavMeshTelemetryService telemetry)
    {
        _commitBaselineGeneration = ReadCommittedGeneration(telemetry);
        _quiescentFixedTickCount = 0;
        _lastObservedFixedTotalTime = double.NaN;
    }

    private bool TryPassGenerationFence(
        GameEngine engine,
        DynamicNavBakeShowcaseConfig config,
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        RuntimeNavMeshTelemetryService telemetry)
    {
        _ = engine;
        ulong committed = ReadCommittedGeneration(telemetry);
        if (committed <= _commitBaselineGeneration)
        {
            _quiescentFixedTickCount = 0;
            _lastObservedFixedTotalTime = double.NaN;
            return false;
        }

        if (!IsNavStable(config, queue, telemetry))
        {
            _quiescentFixedTickCount = 0;
            _lastObservedFixedTotalTime = double.NaN;
            return false;
        }

        double fixedTotal = Time.FixedTotalTime;
        if (double.IsNaN(_lastObservedFixedTotalTime))
        {
            // First observation after a newer committed generation: start counting only when
            // a later distinct FixedStep advances Time.FixedTotalTime.
            _lastObservedFixedTotalTime = fixedTotal;
            _quiescentFixedTickCount = 0;
            return false;
        }

        if (fixedTotal > _lastObservedFixedTotalTime)
        {
            _lastObservedFixedTotalTime = fixedTotal;
            _quiescentFixedTickCount++;
        }

        return _quiescentFixedTickCount >= timeline.RequiredQuiescentFixedTicks;
    }

    private static void RequirePlayerFramingGate(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline,
        string gateName)
    {
        CameraManager camera = engine.GameSession.Camera
            ?? throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{gateName}' requires GameSession.Camera.");

        VirtualCameraBrain brain = camera.VirtualCameraBrain
            ?? throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{gateName}' requires VirtualCameraBrain.");
        string activeId = brain.ActiveCameraId;
        if (!string.Equals(activeId, DynamicNavBakeShowcaseIds.AutoCaptureCameraId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{gateName}' requires active virtual camera " +
                $"'{DynamicNavBakeShowcaseIds.AutoCaptureCameraId}', got '{(string.IsNullOrEmpty(activeId) ? "<none>" : activeId)}'.");
        }

        DynamicNavBakeShowcasePlayerFramingConfig framing = timeline.PlayerFraming
            ?? throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{gateName}' requires raylibAutoTimeline.playerFraming.");

        DynamicNavBakeShowcasePlayerFramingPose expected = actions.ResolvePlayerFramingPose(engine);
        Vector2 actualTarget = camera.State.TargetCm;
        float dx = actualTarget.X - expected.TargetCm.X;
        float dy = actualTarget.Y - expected.TargetCm.Y;
        float distanceSq = (dx * dx) + (dy * dy);
        float tolerance = timeline.CameraTargetToleranceCm;
        float toleranceSq = tolerance * tolerance;
        if (distanceSq > toleranceSq)
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{gateName}' camera target drifted off the deterministic player framing. " +
                $"actual=({actualTarget.X:F2},{actualTarget.Y:F2}) expected=({expected.TargetCm.X:F2},{expected.TargetCm.Y:F2}) " +
                $"toleranceCm={tolerance}.");
        }

        float distanceDelta = MathF.Abs(camera.State.DistanceCm - expected.DistanceCm);
        if (distanceDelta > framing.DistanceToleranceCm)
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{gateName}' camera distance drifted off the deterministic player framing. " +
                $"actual={camera.State.DistanceCm:F2} expected={expected.DistanceCm:F2} " +
                $"toleranceCm={framing.DistanceToleranceCm}.");
        }

        DynamicNavBakeShowcasePlayerFramingVisibility visibility =
            actions.CaptureSquadPlayerFramingVisibility(engine);
        if (visibility.InsideCount < framing.MinSquadMembersOnScreen)
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{gateName}' requires at least {framing.MinSquadMembersOnScreen} " +
                $"authored squad members inside the player framing coverage, got {visibility.InsideCount}; " +
                $"finite={visibility.FiniteProjectionCount}, " +
                $"screenBounds=({visibility.MinScreenX:F1},{visibility.MinScreenY:F1})-" +
                $"({visibility.MaxScreenX:F1},{visibility.MaxScreenY:F1}), " +
                $"safe=({framing.SafeInsetLeftPx},{framing.SafeInsetTopPx})-" +
                $"({framing.CaptureWidthPx - framing.SafeInsetRightPx},{framing.CaptureHeightPx - framing.SafeInsetBottomPx}), " +
                $"cameraTarget=({camera.State.TargetCm.X:F1},{camera.State.TargetCm.Y:F1}), " +
                $"distanceCm={camera.State.DistanceCm:F1}.");
        }

        if (visibility.FiniteProjectionCount <= 0)
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{gateName}' projected zero finite squad members for span gating.");
        }

        float projectedSpanPx = MathF.Max(
            visibility.MaxScreenX - visibility.MinScreenX,
            visibility.MaxScreenY - visibility.MinScreenY);
        if (!(projectedSpanPx >= framing.MinProjectedSquadSpanPx))
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{gateName}' requires projected squad span >= " +
                $"{framing.MinProjectedSquadSpanPx:F1}px, got {projectedSpanPx:F1}px; " +
                $"finite={visibility.FiniteProjectionCount}, " +
                $"screenBounds=({visibility.MinScreenX:F1},{visibility.MinScreenY:F1})-" +
                $"({visibility.MaxScreenX:F1},{visibility.MaxScreenY:F1}), " +
                $"distanceCm={camera.State.DistanceCm:F1}.");
        }
    }

    private void EnsureTargetAlgorithm(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        NavBakeAlgorithmKind targetAlgorithm)
    {
        if (_algorithmEnsured)
        {
            return;
        }

        if (queue.CurrentAlgorithm == targetAlgorithm && !queue.HasRequestedAlgorithm)
        {
            _algorithmEnsured = true;
            return;
        }

        InvokeRequired(
            $"TrySwitchAlgorithm({NavBakeNames.FormatAlgorithm(targetAlgorithm)})",
            actions.TrySwitchAlgorithm(engine, targetAlgorithm, out string error),
            error);
        _algorithmEnsured = true;
    }

    private static DynamicNavBakeShowcaseFormalPlayerRouteSnapshot RequireObservedFormalPlayerRouteReady(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        DynamicNavBakeShowcaseConfig config,
        NavBakeAlgorithmKind targetAlgorithm,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        string step)
    {
        if (!TryObserveFormalPlayerRouteReady(engine, actions, config, targetAlgorithm, queue, out DynamicNavBakeShowcaseFormalPlayerRouteSnapshot route))
        {
            DynamicNavBakeShowcaseFormalPlayerRouteSnapshot observed = actions.CaptureFormalPlayerRouteSnapshot(engine);
            throw new InvalidOperationException(
                $"Dynamic NavBake auto timeline '{step}' requires all authored squad members to have ready formal MassNavigation NavMesh routes " +
                $"(squadCount={config.Squad.Count}, formalAgents={observed.FormalReadyAgentCount}, domain={observed.AgreedPathDomain}, " +
                $"minWaypoints={observed.MinWaypointCount}, orchestration={actions.PathOrchestrationState}, " +
                $"pathStatus={actions.LastPathStatus}, pathPoints={actions.LastPathPointCount}, " +
                $"corridor={actions.LastCoarseCorridorNodeCount}, algorithm={NavBakeNames.FormatAlgorithm(queue.CurrentAlgorithm)}).");
        }

        return route;
    }

    /// <summary>
    /// Read-only formal NavMesh route readiness observation. Never issues gameplay commands.
    /// </summary>
    private static bool TryObserveFormalPlayerRouteReady(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        DynamicNavBakeShowcaseConfig config,
        NavBakeAlgorithmKind targetAlgorithm,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        out DynamicNavBakeShowcaseFormalPlayerRouteSnapshot route)
    {
        route = default;
        if (!actions.SquadDeployed)
        {
            return false;
        }

        if (queue.CurrentAlgorithm != targetAlgorithm || queue.HasRequestedAlgorithm)
        {
            return false;
        }

        if (actions.PathOrchestrationState != DynamicNavBakePathOrchestrationState.LocalSegmentReady ||
            actions.LastPathStatus != NavPathStatus.Ok ||
            actions.LastPathPointCount <= 1)
        {
            return false;
        }

        if (config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld &&
            actions.LastCoarseCorridorNodeCount <= 0)
        {
            return false;
        }

        DynamicNavBakeShowcaseFormalPlayerRouteSnapshot observed = actions.CaptureFormalPlayerRouteSnapshot(engine);
        if (observed.FormalReadyAgentCount != config.Squad.Count ||
            observed.AgreedPathDomain != PathDomain.NavMesh ||
            observed.MinWaypointCount <= 0)
        {
            return false;
        }

        route = observed;
        return true;
    }

    private static void InvokeRequired(string stepName, bool ok, string error)
    {
        if (ok)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Dynamic NavBake auto timeline step '{stepName}' failed: {(string.IsNullOrWhiteSpace(error) ? "<empty error>" : error)}");
    }

    internal static bool IsNavStable(
        DynamicNavBakeShowcaseConfig config,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        RuntimeNavMeshTelemetryService telemetry)
    {
        int residentTarget = checked(config.ResidentWidthChunks * config.ResidentHeightChunks);
        return queue.Status == RuntimeNavMeshRebuildStatus.Idle
               && queue.PendingTileCount == 0
               && queue.SealedRemainingCount == 0
               && !queue.HasResidentWindowTransition
               && queue.ResidentWindowCount == residentTarget
               && queue.CommittedResidentWindowCount == residentTarget
               && !telemetry.HasOpenGeneration
               && telemetry.FailedBatchCount == 0;
    }

    internal static bool IsAlgorithmReady(RuntimeIncrementalNavMeshRebuildQueue queue, NavBakeAlgorithmKind target)
        => !queue.HasRequestedAlgorithm && queue.CurrentAlgorithm == target;

    internal static ulong ReadCommittedGeneration(RuntimeNavMeshTelemetryService telemetry)
        => telemetry.CaptureSnapshot().LastGeneration;

    private static void ThrowIfFailedBatch(
        RuntimeNavMeshTelemetryService telemetry,
        int frameIndex,
        Phase phase,
        RuntimeIncrementalNavMeshRebuildQueue queue)
    {
        if (telemetry.FailedBatchCount == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Dynamic NavBake auto timeline observed FailedBatchCount={telemetry.FailedBatchCount} at hostFrame={frameIndex}, phase={phase}. " +
            FormatQueueDiagnostics(queue, telemetry));
    }

    private static void ThrowIfPastDeadline(
        int frameIndex,
        int deadlineFrame,
        string gateName,
        Phase phase,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        RuntimeNavMeshTelemetryService telemetry,
        NavBakeAlgorithmKind targetAlgorithm)
    {
        if (frameIndex < deadlineFrame)
        {
            return;
        }

        throw new InvalidOperationException(
            FormatDeadlineFailure(gateName, frameIndex, deadlineFrame, phase, queue, telemetry, targetAlgorithm));
    }

    private static string FormatDeadlineFailure(
        string gateName,
        int frameIndex,
        int deadlineFrame,
        Phase phase,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        RuntimeNavMeshTelemetryService telemetry,
        NavBakeAlgorithmKind targetAlgorithm)
    {
        return
            $"Dynamic NavBake auto timeline missed '{gateName}' deadline at hostFrame={frameIndex} (deadline={deadlineFrame}), phase={phase}, " +
            $"targetAlgorithm={NavBakeNames.FormatAlgorithm(targetAlgorithm)}. {FormatQueueDiagnostics(queue, telemetry)}";
    }

    private static string FormatQueueDiagnostics(
        RuntimeIncrementalNavMeshRebuildQueue queue,
        RuntimeNavMeshTelemetryService telemetry)
    {
        return
            $"queue(status={queue.Status}, pending={queue.PendingTileCount}, sealed={queue.SealedRemainingCount}, " +
            $"residentTransition={queue.HasResidentWindowTransition}, resident={queue.ResidentWindowCount}, " +
            $"committed={queue.CommittedResidentWindowCount}, currentAlgorithm={NavBakeNames.FormatAlgorithm(queue.CurrentAlgorithm)}, " +
            $"hasRequested={queue.HasRequestedAlgorithm}" +
            (queue.HasRequestedAlgorithm ? $", requested={NavBakeNames.FormatAlgorithm(queue.RequestedAlgorithm)}" : string.Empty) +
            $"), telemetry(hasOpenGeneration={telemetry.HasOpenGeneration}, failedBatchCount={telemetry.FailedBatchCount}, " +
            $"lastGeneration={telemetry.CaptureSnapshot().LastGeneration}).";
    }

    private static RuntimeIncrementalNavMeshRebuildQueue RequireQueue(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
            ?? throw new InvalidOperationException(
                "Dynamic NavBake auto timeline requires CoreServiceKeys.RuntimeNavMeshRebuildQueue.");
    }

    private static RuntimeNavMeshTelemetryService RequireTelemetry(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.RuntimeNavMeshTelemetry)
            ?? throw new InvalidOperationException(
                "Dynamic NavBake auto timeline requires CoreServiceKeys.RuntimeNavMeshTelemetry.");
    }
}
