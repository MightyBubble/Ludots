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
        /// <summary>Pulse failed after a successful enqueue; runtime/send contract is broken.</summary>
        PulseFailed = 5,
        /// <summary>
        /// Accumulated due steps exceed the configured backlog ceiling.
        /// No steps are emitted and accumulated time is retained (never silently discarded).
        /// </summary>
        CatchUpBacklogExceeded = 6,
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
                or ReplicatedClientFixedInputClockAdvanceStatus.NotConnected;
    }

    /// <summary>
    /// Formal replicated-client fixed-input clock driven by elapsed real time at
    /// <c>NetworkRuntimeConfig.SimulationTickRateHz</c>. Independent of presentation/render update
    /// semantics; never sends inside <see cref="INetworkRuntimePort.PumpReplicatedClient"/>.
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
        private readonly int _maxStepsPerAdvance;
        private readonly int _maxAccumulatedSteps;
        private readonly double _tickDurationSeconds;
        private readonly byte[] _payloadScratch;

        private double _accumulatorSeconds;
        private uint _nextTargetTick;
        private uint _lastEmittedTargetTick;
        private ulong _armedSessionEpochValue;
        private bool _armed;
        private bool _observedConnected;

        public ReplicatedClientFixedInputClock(
            IReplicatedClientFixedInputPort client,
            IFixedInputPayloadSource source,
            int simulationTickRateHz,
            int payloadBytes,
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
            _maxStepsPerAdvance = maxStepsPerAdvance;
            _maxAccumulatedSteps = maxAccumulatedSteps;
            _tickDurationSeconds = 1d / simulationTickRateHz;
            _payloadScratch = new byte[payloadBytes];
            _nextTargetTick = 1;
        }

        public int SimulationTickRateHz => _simulationTickRateHz;
        public int PayloadBytes => _payloadBytes;
        public int MaxStepsPerAdvance => _maxStepsPerAdvance;
        public int MaxAccumulatedSteps => _maxAccumulatedSteps;
        public double AccumulatedSeconds => _accumulatorSeconds;
        public uint NextTargetTick => _nextTargetTick;
        public uint LastEmittedTargetTick => _lastEmittedTargetTick;
        public bool IsArmed => _armed;
        public SessionEpoch ArmedSessionEpoch =>
            _armed ? new SessionEpoch(_armedSessionEpochValue) : SessionEpoch.Empty;

        /// <summary>
        /// Advances the fixed-input clock by elapsed real time.
        /// Zero delta pauses. Non-finite or negative deltas fail fast.
        /// </summary>
        public ReplicatedClientFixedInputClockAdvanceResult Advance(float elapsedRealSeconds)
        {
            if (!float.IsFinite(elapsedRealSeconds) || elapsedRealSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedRealSeconds));
            }

            if (_client.State != ReplicatedClientConnectionState.Connected)
            {
                // Pause: do not accumulate while disconnected, and never send.
                // Explicit disarm so the next Connected edge restarts tick SSOT for the new/outbox session.
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
                // Rising edge Connected: arm (or re-arm) for this session generation.
                Arm(epoch);
                _observedConnected = true;
            }
            else
            {
                EnsureArmedForSession(epoch);
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
        /// restarts target-tick selection at 1 for the given non-empty epoch.
        /// </summary>
        public void ResetForSession(SessionEpoch sessionEpoch)
        {
            if (sessionEpoch.IsEmpty)
            {
                throw new ArgumentException("Session epoch must be non-empty.", nameof(sessionEpoch));
            }

            Arm(sessionEpoch);
        }

        private ReplicatedClientFixedInputClockAdvanceResult TryEmitOneFixedStep()
        {
            uint targetTick = _nextTargetTick;
            if (!FixedInputWireCodec.IsValidInputTargetTick(targetTick))
            {
                throw new NetworkRuntimeException(
                    new NetworkRuntimeFault(
                        NetworkRuntimeFaultSeverity.LocalContractViolation,
                        NetworkRuntimeFaultCode.FixedInputRejected,
                        wireKind: NetworkWireKind.FixedInputBatch,
                        detail: (int)FixedInputOutboxEnqueueStatus.InvalidInput));
            }

            Span<byte> payload = _payloadScratch.AsSpan(0, _payloadBytes);
            FixedInputPayloadSampleStatus sampled = _source.TrySample(payload);
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
                return new ReplicatedClientFixedInputClockAdvanceResult(
                    ReplicatedClientFixedInputClockAdvanceStatus.PulseFailed,
                    stepsEmitted: 0,
                    lastTargetTick: _lastEmittedTargetTick,
                    enqueueStatus: FixedInputOutboxEnqueueStatus.Enqueued);
            }

            _lastEmittedTargetTick = targetTick;
            _nextTargetTick = checked(targetTick + 1);
            return new ReplicatedClientFixedInputClockAdvanceResult(
                ReplicatedClientFixedInputClockAdvanceStatus.Stepped,
                stepsEmitted: 1,
                lastTargetTick: targetTick,
                enqueueStatus: FixedInputOutboxEnqueueStatus.Enqueued);
        }

        private void EnsureArmedForSession(SessionEpoch epoch)
        {
            if (_armed && _armedSessionEpochValue == epoch.Value)
            {
                return;
            }

            // New session generation: restart tick SSOT at 1 and clear pause accumulator.
            Arm(epoch);
        }

        private void Arm(SessionEpoch epoch)
        {
            _armed = true;
            _armedSessionEpochValue = epoch.Value;
            _accumulatorSeconds = 0d;
            _nextTargetTick = 1;
            _lastEmittedTargetTick = 0;
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
            // Keep last/next tick values only until the next session arm; they are not authoritative while disarmed.
            _nextTargetTick = 1;
            _lastEmittedTargetTick = 0;
        }

        private ReplicatedClientFixedInputClockAdvanceResult IdleResult() =>
            new(
                ReplicatedClientFixedInputClockAdvanceStatus.Idle,
                stepsEmitted: 0,
                lastTargetTick: _lastEmittedTargetTick,
                enqueueStatus: FixedInputOutboxEnqueueStatus.Enqueued);
    }
}
