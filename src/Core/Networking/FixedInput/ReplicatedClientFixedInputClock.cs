using System;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Networking.FixedInput
{
    /// <summary>
    /// Outcomes for one <see cref="ReplicatedClientFixedInputClock.Advance"/> call.
    /// Failures never silently skip elapsed time or hide enqueue/send faults.
    /// </summary>
    public enum ReplicatedClientFixedInputClockAdvanceStatus : byte
    {
        /// <summary>No fixed step was due (zero delta, pause, or not yet enough accumulated time).</summary>
        Idle = 0,
        /// <summary>One or more fixed steps were emitted successfully.</summary>
        Stepped = 1,
        /// <summary>Client is not connected; elapsed time is not accumulated and no input is sent.</summary>
        NotConnected = 2,
        /// <summary>Payload source failed; no tick advanced and accumulated due time is retained.</summary>
        SourceFailed = 3,
        /// <summary>Outbox enqueue rejected; no tick advanced and accumulated due time is retained.</summary>
        EnqueueRejected = 4,
        /// <summary>
        /// Pulse failed after a successful enqueue. Terminal local contract fault — the tick is already
        /// owned by the outbox and must never be submitted again.
        /// </summary>
        PulseFailed = 5,
        /// <summary>
        /// Accumulated due steps exceed the configured backlog ceiling.
        /// No steps are emitted and accumulated time is retained (never silently discarded).
        /// </summary>
        CatchUpBacklogExceeded = 6,
        /// <summary>
        /// Connected but waiting for a new authoritative fixed-input ACK observed after the Connected edge.
        /// Elapsed time is not accumulated and no input is sampled or enqueued.
        /// </summary>
        WaitingForAuthoritativeAcknowledgement = 7,
    }

    /// <summary>
    /// Result of advancing the replicated-client fixed-input clock.
    /// </summary>
    public readonly struct ReplicatedClientFixedInputClockAdvanceResult
    {
        public ReplicatedClientFixedInputClockAdvanceResult(
            ReplicatedClientFixedInputClockAdvanceStatus status,
            int stepsEmitted,
            uint lastTargetTick,
            FixedInputOutboxEnqueueStatus enqueueStatus)
        {
            Status = status;
            StepsEmitted = stepsEmitted;
            LastTargetTick = lastTargetTick;
            EnqueueStatus = enqueueStatus;
        }

        public ReplicatedClientFixedInputClockAdvanceStatus Status { get; }
        public int StepsEmitted { get; }
        public uint LastTargetTick { get; }
        public FixedInputOutboxEnqueueStatus EnqueueStatus { get; }

        public bool IsSuccess =>
            Status is ReplicatedClientFixedInputClockAdvanceStatus.Idle
                or ReplicatedClientFixedInputClockAdvanceStatus.Stepped
                or ReplicatedClientFixedInputClockAdvanceStatus.NotConnected
                or ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement;
    }

    /// <summary>
    /// Formal replicated-client fixed-input clock driven by elapsed real time at
    /// <c>NetworkRuntimeConfig.SimulationTickRateHz</c>. Independent of presentation/render update
    /// semantics; never sends inside <see cref="INetworkRuntimePort.PumpReplicatedClient"/>.
    /// Target-tick SSOT is
    /// <c>max(lastEnqueued + 1, acknowledgedCommittedThrough + FixedInputLeadTicks)</c>.
    /// </summary>
    public sealed class ReplicatedClientFixedInputClock
    {
        /// <summary>
        /// Absorbs float→double promotion noise so exact N×(1/Hz) render frames are not under-counted.
        /// Never used to invent an extra step from incomplete time.
        /// </summary>
        private const double DueStepEpsilonSeconds = 1e-9d;

        private readonly IReplicatedClientFixedInputPort _client;
        private readonly IFixedInputPayloadSource _source;
        private readonly int _simulationTickRateHz;
        private readonly int _payloadBytes;
        private readonly int _fixedInputLeadTicks;
        private readonly int _fixedInputMaxFutureTicks;
        private readonly int _maxStepsPerAdvance;
        private readonly int _maxAccumulatedSteps;
        private readonly double _tickDurationSeconds;
        private readonly byte[] _payloadScratch;

        private double _accumulatorSeconds;
        private uint _lastEmittedTargetTick;
        private ulong _armedSessionEpochValue;
        private ulong _ackObservationBaseline;
        private bool _armed;
        private bool _waitingForAuthoritativeAck;
        private bool _observedConnected;
        private bool _terminalPulseFault;

        public ReplicatedClientFixedInputClock(
            IReplicatedClientFixedInputPort client,
            IFixedInputPayloadSource source,
            int simulationTickRateHz,
            int payloadBytes,
            int fixedInputLeadTicks,
            int fixedInputMaxFutureTicks,
            int maxStepsPerAdvance,
            int maxAccumulatedSteps)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (simulationTickRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(simulationTickRateHz));
            }

            if (payloadBytes <= 0 || payloadBytes > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadBytes));
            }

            if (fixedInputMaxFutureTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedInputMaxFutureTicks));
            }

            if (fixedInputLeadTicks < 1 || fixedInputLeadTicks > fixedInputMaxFutureTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedInputLeadTicks),
                    fixedInputLeadTicks,
                    $"Fixed-input lead ticks must satisfy 1 <= lead <= max future ticks ({fixedInputMaxFutureTicks}).");
            }

            if (maxStepsPerAdvance <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxStepsPerAdvance));
            }

            if (maxAccumulatedSteps < maxStepsPerAdvance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxAccumulatedSteps),
                    maxAccumulatedSteps,
                    $"maxAccumulatedSteps must be >= maxStepsPerAdvance ({maxStepsPerAdvance}).");
            }

            _simulationTickRateHz = simulationTickRateHz;
            _payloadBytes = payloadBytes;
            _fixedInputLeadTicks = fixedInputLeadTicks;
            _fixedInputMaxFutureTicks = fixedInputMaxFutureTicks;
            _maxStepsPerAdvance = maxStepsPerAdvance;
            _maxAccumulatedSteps = maxAccumulatedSteps;
            _tickDurationSeconds = 1d / simulationTickRateHz;
            _payloadScratch = new byte[payloadBytes];
        }

        public int SimulationTickRateHz => _simulationTickRateHz;
        public int PayloadBytes => _payloadBytes;
        public int FixedInputLeadTicks => _fixedInputLeadTicks;
        public int FixedInputMaxFutureTicks => _fixedInputMaxFutureTicks;
        public int MaxStepsPerAdvance => _maxStepsPerAdvance;
        public int MaxAccumulatedSteps => _maxAccumulatedSteps;
        public double AccumulatedSeconds => _accumulatorSeconds;
        public uint LastEmittedTargetTick => _lastEmittedTargetTick;
        public bool IsArmed => _armed;
        public bool IsWaitingForAuthoritativeAcknowledgement => _waitingForAuthoritativeAck;
        public bool IsTerminalPulseFaulted => _terminalPulseFault;
        public SessionEpoch ArmedSessionEpoch =>
            _armed ? new SessionEpoch(_armedSessionEpochValue) : SessionEpoch.Empty;

        /// <summary>
        /// Peek the next target tick that would be selected from current port ACK/outbox SSOT.
        /// Throws when the tick domain overflows.
        /// </summary>
        public uint PeekNextTargetTick()
        {
            EnsureNotTerminalPulseFaulted();
            return SelectNextTargetTickOrThrow();
        }

        /// <summary>
        /// Advances the fixed-input clock by elapsed real time.
        /// Zero delta pauses. Non-finite or negative deltas fail fast.
        /// </summary>
        public ReplicatedClientFixedInputClockAdvanceResult Advance(float elapsedRealSeconds)
        {
            EnsureNotTerminalPulseFaulted();

            if (!float.IsFinite(elapsedRealSeconds) || elapsedRealSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedRealSeconds));
            }

            if (_client.State != ReplicatedClientConnectionState.Connected)
            {
                // Pause: do not accumulate while disconnected, and never send.
                // Explicit disarm so the next Connected edge requires a fresh ACK observation.
                Disarm();
                _observedConnected = false;
                return new ReplicatedClientFixedInputClockAdvanceResult(
                    ReplicatedClientFixedInputClockAdvanceStatus.NotConnected,
                    stepsEmitted: 0,
                    lastTargetTick: _lastEmittedTargetTick,
                    enqueueStatus: FixedInputOutboxEnqueueStatus.InvalidInput);
            }

            SessionEpoch epoch = _client.SessionEpoch;
            if (epoch.IsEmpty)
            {
                throw new NetworkRuntimeException(
                    new NetworkRuntimeFault(
                        NetworkRuntimeFaultSeverity.LocalContractViolation,
                        NetworkRuntimeFaultCode.SessionContractViolation,
                        detail: (int)ReplicatedClientFixedInputClockAdvanceStatus.NotConnected));
            }

            if (!_observedConnected)
            {
                // Rising edge Connected: arm (or re-arm) and wait for a NEW ACK after this edge.
                ArmForConnectedEdge(epoch);
                _observedConnected = true;
            }
            else
            {
                EnsureArmedForSession(epoch);
            }

            if (_waitingForAuthoritativeAck)
            {
                if (_client.FixedInputAcknowledgementObservationVersion <= _ackObservationBaseline)
                {
                    return new ReplicatedClientFixedInputClockAdvanceResult(
                        ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement,
                        stepsEmitted: 0,
                        lastTargetTick: _lastEmittedTargetTick,
                        enqueueStatus: FixedInputOutboxEnqueueStatus.InvalidInput);
                }

                _waitingForAuthoritativeAck = false;
            }

            if (elapsedRealSeconds == 0f)
            {
                return IdleResult();
            }

            _accumulatorSeconds += elapsedRealSeconds;
            int dueSteps = (int)((_accumulatorSeconds + DueStepEpsilonSeconds) / _tickDurationSeconds);
            if (dueSteps <= 0)
            {
                return IdleResult();
            }

            if (dueSteps > _maxAccumulatedSteps)
            {
                return new ReplicatedClientFixedInputClockAdvanceResult(
                    ReplicatedClientFixedInputClockAdvanceStatus.CatchUpBacklogExceeded,
                    stepsEmitted: 0,
                    lastTargetTick: _lastEmittedTargetTick,
                    enqueueStatus: FixedInputOutboxEnqueueStatus.CapacityExceeded);
            }

            int stepsToEmit = Math.Min(dueSteps, _maxStepsPerAdvance);
            int emitted = 0;
            for (int i = 0; i < stepsToEmit; i++)
            {
                ReplicatedClientFixedInputClockAdvanceResult step = TryEmitOneFixedStep();
                if (step.Status != ReplicatedClientFixedInputClockAdvanceStatus.Stepped)
                {
                    return new ReplicatedClientFixedInputClockAdvanceResult(
                        step.Status,
                        emitted,
                        step.LastTargetTick,
                        step.EnqueueStatus);
                }

                _accumulatorSeconds -= _tickDurationSeconds;
                if (_accumulatorSeconds < 0d)
                {
                    _accumulatorSeconds = 0d;
                }

                emitted++;
            }

            return new ReplicatedClientFixedInputClockAdvanceResult(
                ReplicatedClientFixedInputClockAdvanceStatus.Stepped,
                emitted,
                _lastEmittedTargetTick,
                FixedInputOutboxEnqueueStatus.Enqueued);
        }

        /// <summary>
        /// Explicit session reset used by tests and reconnect composition. Clears accumulated time and
        /// requires a fresh authoritative ACK observation before sampling again.
        /// </summary>
        public void ResetForSession(SessionEpoch sessionEpoch)
        {
            if (sessionEpoch.IsEmpty)
            {
                throw new ArgumentException("Session epoch must be non-empty.", nameof(sessionEpoch));
            }

            EnsureNotTerminalPulseFaulted();
            ArmForConnectedEdge(sessionEpoch);
            _observedConnected = _client.State == ReplicatedClientConnectionState.Connected
                && _client.SessionEpoch == sessionEpoch;
        }

        private ReplicatedClientFixedInputClockAdvanceResult TryEmitOneFixedStep()
        {
            uint targetTick = SelectNextTargetTickOrThrow();

            Span<byte> payload = _payloadScratch.AsSpan(0, _payloadBytes);
            FixedInputPayloadSampleStatus sampled = _source.TrySample(targetTick, payload);
            if (sampled != FixedInputPayloadSampleStatus.Sampled)
            {
                return new ReplicatedClientFixedInputClockAdvanceResult(
                    ReplicatedClientFixedInputClockAdvanceStatus.SourceFailed,
                    stepsEmitted: 0,
                    lastTargetTick: _lastEmittedTargetTick,
                    enqueueStatus: FixedInputOutboxEnqueueStatus.InvalidInput);
            }

            FixedInputOutboxEnqueueStatus enqueued = _client.TrySubmitFixedInput(targetTick, payload);
            if (enqueued != FixedInputOutboxEnqueueStatus.Enqueued)
            {
                return new ReplicatedClientFixedInputClockAdvanceResult(
                    ReplicatedClientFixedInputClockAdvanceStatus.EnqueueRejected,
                    stepsEmitted: 0,
                    lastTargetTick: _lastEmittedTargetTick,
                    enqueueStatus: enqueued);
            }

            if (!_client.TryPulseFixedInputSend())
            {
                // Tick is already owned by the outbox. Never remain in a normal retry state that could
                // submit the same tick again — this is a permanent local contract fault.
                _terminalPulseFault = true;
                throw new NetworkRuntimeException(
                    new NetworkRuntimeFault(
                        NetworkRuntimeFaultSeverity.LocalContractViolation,
                        NetworkRuntimeFaultCode.FixedInputRejected,
                        wireKind: NetworkWireKind.FixedInputBatch,
                        detail: (int)ReplicatedClientFixedInputClockAdvanceStatus.PulseFailed));
            }

            _lastEmittedTargetTick = targetTick;
            return new ReplicatedClientFixedInputClockAdvanceResult(
                ReplicatedClientFixedInputClockAdvanceStatus.Stepped,
                stepsEmitted: 1,
                lastTargetTick: targetTick,
                enqueueStatus: FixedInputOutboxEnqueueStatus.Enqueued);
        }

        private uint SelectNextTargetTickOrThrow()
        {
            if (!FixedInputWireCodec.TryComputeNextTargetTick(
                    _client.LastEnqueuedFixedInputTargetTick,
                    _client.HasEnqueuedFixedInputTargetTick,
                    _client.FixedInputAcknowledgedCommittedTick,
                    _fixedInputLeadTicks,
                    out uint nextTargetTick))
            {
                throw new NetworkRuntimeException(
                    new NetworkRuntimeFault(
                        NetworkRuntimeFaultSeverity.LocalContractViolation,
                        NetworkRuntimeFaultCode.FixedInputRejected,
                        wireKind: NetworkWireKind.FixedInputBatch,
                        detail: (int)FixedInputOutboxEnqueueStatus.InvalidInput));
            }

            return nextTargetTick;
        }

        private void EnsureArmedForSession(SessionEpoch epoch)
        {
            if (_armed && _armedSessionEpochValue == epoch.Value)
            {
                return;
            }

            // New session generation: clear pause accumulator and require a fresh ACK for the new outbox.
            ArmForConnectedEdge(epoch);
        }

        private void ArmForConnectedEdge(SessionEpoch epoch)
        {
            _armed = true;
            _armedSessionEpochValue = epoch.Value;
            _accumulatorSeconds = 0d;
            _lastEmittedTargetTick = 0;
            _ackObservationBaseline = _client.FixedInputAcknowledgementObservationVersion;
            _waitingForAuthoritativeAck = true;
        }

        private void Disarm()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            _armedSessionEpochValue = 0;
            _accumulatorSeconds = 0d;
            _waitingForAuthoritativeAck = false;
            _ackObservationBaseline = 0;
            _lastEmittedTargetTick = 0;
        }

        private void EnsureNotTerminalPulseFaulted()
        {
            if (!_terminalPulseFault)
            {
                return;
            }

            throw new NetworkRuntimeException(
                new NetworkRuntimeFault(
                    NetworkRuntimeFaultSeverity.LocalContractViolation,
                    NetworkRuntimeFaultCode.FixedInputRejected,
                    wireKind: NetworkWireKind.FixedInputBatch,
                    detail: (int)ReplicatedClientFixedInputClockAdvanceStatus.PulseFailed));
        }

        private ReplicatedClientFixedInputClockAdvanceResult IdleResult() =>
            new(
                ReplicatedClientFixedInputClockAdvanceStatus.Idle,
                stepsEmitted: 0,
                lastTargetTick: _lastEmittedTargetTick,
                enqueueStatus: FixedInputOutboxEnqueueStatus.Enqueued);
    }
}
