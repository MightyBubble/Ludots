using System;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Simulation;

namespace Ludots.Core.Networking.FixedInput
{
    public enum FixedInputSeatActivationState : byte
    {
        InvalidSeat = 0,
        AwaitingFirstInput = 1,
        Active = 2,
    }

    /// <summary>
    /// Fixed-capacity SoA authoritative fixed-input ingress keyed by authenticated
    /// <see cref="SessionSeatBinding"/>. Reads <see cref="AuthoritativeSimulationTickState"/>
    /// as the only Tick truth and never begins, commits, or restores ticks.
    /// Missing input is reported explicitly; zero / last-input is never fabricated.
    /// Acknowledgements are built only after the authoritative frame commit.
    /// </summary>
    public sealed class AuthoritativeFixedInputIngress
    {
        private readonly FixedInputProtocolConfig _config;
        private readonly AuthoritativeSimulationTickState _ticks;
        private readonly int _historyTicks;
        private readonly int _payloadBytes;
        private readonly int _seatCapacity;

        private readonly bool[] _seatBound;
        private readonly uint[] _seatGenerations;
        private readonly int[] _seatPlayerIds;

        private readonly uint[] _cellTicks;
        private readonly bool[] _cellOccupied;
        private readonly byte[] _payloads;

        private readonly bool[] _hasAcceptedBySeat;
        private readonly uint[] _activationTicksBySeat;
        private readonly uint[] _lastAcceptedTickBySeat;
        private readonly uint[] _latestReceivedTickBySeat;
        private readonly uint[] _latestMissingAtDeadlineBySeat;

        // Single-writer runtime-owned scratch for all-or-nothing batch classification.
        // Callers must not share one ingress across concurrent writers.
        private readonly FixedInputAdmissionDisposition[] _scratchDispositions;

        public AuthoritativeFixedInputIngress(
            in FixedInputProtocolConfig config,
            AuthoritativeSimulationTickState ticks)
        {
            _ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));
            config.EnsureValid();
            _config = config;
            _historyTicks = config.HistoryTicksPerSeat;
            _payloadBytes = config.FramePayloadBytes;
            _seatCapacity = config.SeatCapacity;

            _seatBound = new bool[_seatCapacity];
            _seatGenerations = new uint[_seatCapacity];
            _seatPlayerIds = new int[_seatCapacity];

            int cellCount = checked(_seatCapacity * _historyTicks);
            _cellTicks = new uint[cellCount];
            _cellOccupied = new bool[cellCount];
            _payloads = new byte[checked(cellCount * _payloadBytes)];

            _hasAcceptedBySeat = new bool[_seatCapacity];
            _activationTicksBySeat = new uint[_seatCapacity];
            _lastAcceptedTickBySeat = new uint[_seatCapacity];
            _latestReceivedTickBySeat = new uint[_seatCapacity];
            _latestMissingAtDeadlineBySeat = new uint[_seatCapacity];

            _scratchDispositions = new FixedInputAdmissionDisposition[config.MaxFramesPerBatch];
        }

        public FixedInputProtocolConfig Config => _config;
        public AuthoritativeSimulationTickState TickState => _ticks;
        public int SeatCapacity => _seatCapacity;
        public int HistoryTicksPerSeat => _historyTicks;
        public int FramePayloadBytes => _payloadBytes;

        public int AcceptedCount { get; private set; }
        public int AcceptedOutOfOrderCount { get; private set; }
        public int DuplicateCount { get; private set; }
        public int ConflictCount { get; private set; }
        public int LateCount { get; private set; }
        public int TooFarFutureCount { get; private set; }
        public int ExecutionCutoffRejectionCount { get; private set; }
        public int RingWrapCount { get; private set; }
        public int BatchRejectedCount { get; private set; }
        public int MissingAtDeadlineCount { get; private set; }

        public void BindSeat(in SessionSeatBinding seat)
        {
            ValidateSeatShape(in seat);
            int slot = seat.Slot;
            if (_seatBound[slot] &&
                _seatGenerations[slot] == seat.Generation &&
                _seatPlayerIds[slot] == seat.PlayerId.Value)
            {
                return;
            }

            if (_seatBound[slot])
            {
                throw new InvalidOperationException(
                    $"Fixed-input seat {slot} is already bound to generation {_seatGenerations[slot]} player {_seatPlayerIds[slot]}.");
            }

            if (_seatGenerations[slot] != 0 && seat.Generation <= _seatGenerations[slot])
            {
                throw new InvalidOperationException(
                    $"Fixed-input seat {slot} generation {seat.Generation} is not newer than released generation {_seatGenerations[slot]}.");
            }

            _seatBound[slot] = true;
            _seatGenerations[slot] = seat.Generation;
            _seatPlayerIds[slot] = seat.PlayerId.Value;
            ClearSeatHistory(slot);
        }

        public void RebindSeat(in SessionSeatBinding seat)
        {
            if (!MatchesSeat(in seat))
            {
                throw new InvalidOperationException(
                    $"Cannot rebind fixed-input seat {seat.Slot}:{seat.Generation} because its current binding differs.");
            }

            ClearSeatHistory(seat.Slot);
        }

        public bool TryReleaseSeat(in SessionSeatBinding seat)
        {
            if (!MatchesSeat(in seat))
            {
                return false;
            }

            ClearSeatHistory(seat.Slot);
            _seatBound[seat.Slot] = false;
            // Generation retained so reuse requires a newer generation.
            _seatPlayerIds[seat.Slot] = 0;
            return true;
        }

        public bool TryGetSeat(int slot, out SessionSeatBinding seat)
        {
            if ((uint)slot >= (uint)_seatCapacity || !_seatBound[slot])
            {
                seat = default;
                return false;
            }

            seat = new SessionSeatBinding(slot, _seatGenerations[slot], new PlayerId(_seatPlayerIds[slot]));
            return true;
        }

        public FixedInputSeatActivationState GetSeatActivationState(
            in SessionSeatBinding seat,
            out uint activationTick)
        {
            if (!MatchesSeat(in seat))
            {
                activationTick = 0;
                return FixedInputSeatActivationState.InvalidSeat;
            }

            if (!_hasAcceptedBySeat[seat.Slot])
            {
                activationTick = 0;
                return FixedInputSeatActivationState.AwaitingFirstInput;
            }

            activationTick = _activationTicksBySeat[seat.Slot];
            return FixedInputSeatActivationState.Active;
        }

        /// <summary>
        /// All-or-nothing batch admission: the batch is fully classified against the pre-batch ring
        /// state; hard rejects (including Conflict and RingWrap) mutate nothing. Soft outcomes
        /// (duplicate/late/cutoff/future) are applied after classification without inventing input.
        /// </summary>
        public FixedInputBatchAdmissionStatus TryAdmitBatch(
            in SessionSeatBinding seat,
            in NetworkFixedInputBatchHeader header,
            ReadOnlySpan<uint> targetTicks,
            ReadOnlySpan<byte> payloads,
            Span<FixedInputAdmissionDisposition> dispositions)
        {
            if (header.FrameCount != targetTicks.Length)
            {
                return RejectBatch(dispositions, 0, FixedInputAdmissionDisposition.BatchRejected);
            }

            if (dispositions.Length < header.FrameCount)
            {
                throw new ArgumentException(
                    "Disposition destination must cover every frame in the batch.",
                    nameof(dispositions));
            }

            if (header.FrameCount > _config.MaxFramesPerBatch)
            {
                return RejectBatch(dispositions, header.FrameCount, FixedInputAdmissionDisposition.BatchRejected);
            }

            if (header.SessionEpoch != _config.SessionEpoch)
            {
                return RejectBatch(dispositions, header.FrameCount, FixedInputAdmissionDisposition.EpochMismatch);
            }

            if (header.SchemaId != _config.SchemaId)
            {
                return RejectBatch(dispositions, header.FrameCount, FixedInputAdmissionDisposition.SchemaMismatch);
            }

            if (header.FramePayloadBytes != _payloadBytes)
            {
                return RejectBatch(dispositions, header.FrameCount, FixedInputAdmissionDisposition.PayloadMismatch);
            }

            long expectedPayloadBytes = (long)header.FrameCount * _payloadBytes;
            if (payloads.Length != expectedPayloadBytes)
            {
                return RejectBatch(dispositions, header.FrameCount, FixedInputAdmissionDisposition.PayloadMismatch);
            }

            if (!MatchesSeat(in seat))
            {
                return RejectBatch(dispositions, header.FrameCount, FixedInputAdmissionDisposition.InvalidSeatGeneration);
            }

            if (header.FrameCount == 0)
            {
                return RejectBatch(dispositions, 0, FixedInputAdmissionDisposition.BatchRejected);
            }

            if (!FixedInputWireCodec.IsValidSimulationTickField(header.AcknowledgedCommittedTick))
            {
                return RejectBatch(dispositions, header.FrameCount, FixedInputAdmissionDisposition.TickOutOfRange);
            }

            for (int i = 0; i < header.FrameCount; i++)
            {
                if (!FixedInputWireCodec.IsValidInputTargetTick(targetTicks[i]))
                {
                    return RejectBatch(dispositions, header.FrameCount, FixedInputAdmissionDisposition.TickOutOfRange);
                }

                if (i > 0 && targetTicks[i] <= targetTicks[i - 1])
                {
                    return RejectBatch(dispositions, header.FrameCount, FixedInputAdmissionDisposition.InvalidFrameOrder);
                }
            }

            Span<FixedInputAdmissionDisposition> planned = _scratchDispositions.AsSpan(0, header.FrameCount);
            bool hardReject = false;
            FixedInputAdmissionDisposition hardReason = FixedInputAdmissionDisposition.BatchRejected;
            for (int i = 0; i < header.FrameCount; i++)
            {
                FixedInputAdmissionDisposition disposition = ClassifyFrame(
                    seat.Slot,
                    targetTicks[i],
                    payloads.Slice(i * _payloadBytes, _payloadBytes));
                planned[i] = disposition;
                if (IsHardReject(disposition))
                {
                    hardReject = true;
                    hardReason = disposition;
                    break;
                }
            }

            if (hardReject)
            {
                return RejectBatch(dispositions, header.FrameCount, hardReason);
            }

            for (int i = 0; i < header.FrameCount; i++)
            {
                FixedInputAdmissionDisposition disposition = planned[i];
                dispositions[i] = disposition;
                ApplyDispositionCounters(disposition);
                if (disposition is FixedInputAdmissionDisposition.Accepted
                    or FixedInputAdmissionDisposition.AcceptedOutOfOrder)
                {
                    WriteFrame(
                        seat.Slot,
                        targetTicks[i],
                        payloads.Slice(i * _payloadBytes, _payloadBytes),
                        disposition);
                }
            }

            return FixedInputBatchAdmissionStatus.Success;
        }

        public FixedInputLookupResult TryGet(
            in SessionSeatBinding seat,
            uint tick,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!MatchesSeat(in seat))
            {
                return FixedInputLookupResult.InvalidSeat;
            }

            if (!FixedInputWireCodec.IsValidInputTargetTick(tick))
            {
                return FixedInputLookupResult.InvalidTick;
            }

            if (destination.Length < _payloadBytes)
            {
                throw new ArgumentException(
                    $"Destination must hold {_payloadBytes} payload bytes.",
                    nameof(destination));
            }

            int cell = CellIndex(seat.Slot, tick);
            if (!_cellOccupied[cell] || _cellTicks[cell] != tick)
            {
                if (_ticks.IsExecuting && (uint)_ticks.ExecutingTick == tick)
                {
                    int slot = seat.Slot;
                    if (tick > _latestMissingAtDeadlineBySeat[slot])
                    {
                        _latestMissingAtDeadlineBySeat[slot] = tick;
                    }

                    MissingAtDeadlineCount++;
                    return FixedInputLookupResult.MissingAtDeadline;
                }

                return FixedInputLookupResult.Missing;
            }

            _payloads.AsSpan(cell * _payloadBytes, _payloadBytes).CopyTo(destination);
            bytesWritten = _payloadBytes;
            return FixedInputLookupResult.Present;
        }

        /// <summary>
        /// Builds the post-commit fixed-input acknowledgement for <paramref name="seat"/>.
        /// Fail-fast while <see cref="AuthoritativeSimulationTickState.IsExecuting"/>:
        /// ACK is only valid after the authoritative frame commit.
        /// </summary>
        public NetworkFixedInputAcknowledgement BuildAcknowledgement(in SessionSeatBinding seat)
        {
            if (_ticks.IsExecuting)
            {
                throw new InvalidOperationException(
                    "Fixed-input acknowledgement may only be built after the authoritative frame commit.");
            }

            if (!MatchesSeat(in seat))
            {
                throw new InvalidOperationException(
                    $"Cannot build fixed-input acknowledgement for unbound seat {seat.Slot}:{seat.Generation}.");
            }

            int slot = seat.Slot;
            uint committedThrough = _ticks.CommittedTick < 0
                ? 0u
                : (uint)_ticks.CommittedTick;
            uint latestReceived = _latestReceivedTickBySeat[slot];
            ulong mask = 0;

            if (latestReceived != 0)
            {
                for (int bit = 0; bit < 64; bit++)
                {
                    if (latestReceived < (uint)bit)
                    {
                        break;
                    }

                    uint candidate = latestReceived - (uint)bit;
                    if (candidate == 0)
                    {
                        break;
                    }

                    int cell = CellIndex(slot, candidate);
                    if (_cellOccupied[cell] && _cellTicks[cell] == candidate)
                    {
                        mask |= 1UL << bit;
                    }
                }
            }

            return new NetworkFixedInputAcknowledgement(
                _config.SessionEpoch,
                _config.SchemaId,
                committedThrough,
                latestReceived,
                mask,
                _latestMissingAtDeadlineBySeat[slot]);
        }

        private FixedInputAdmissionDisposition ClassifyFrame(
            int seatSlot,
            uint tick,
            ReadOnlySpan<byte> payload)
        {
            if (!FixedInputWireCodec.IsValidInputTargetTick(tick))
            {
                return FixedInputAdmissionDisposition.TickOutOfRange;
            }

            if (_ticks.IsExecuting && (uint)_ticks.ExecutingTick == tick)
            {
                return FixedInputAdmissionDisposition.RejectedAtExecutionCutoff;
            }

            uint committed = _ticks.CommittedTick < 0 ? 0u : (uint)_ticks.CommittedTick;
            if (tick <= committed)
            {
                return FixedInputAdmissionDisposition.Late;
            }

            ulong latestAllowed = (ulong)committed + (ulong)_config.MaxFutureTicks;
            if (tick > latestAllowed)
            {
                return FixedInputAdmissionDisposition.TooFarFuture;
            }

            int cell = CellIndex(seatSlot, tick);
            if (_cellOccupied[cell] && _cellTicks[cell] != tick)
            {
                // Safe reuse: an older modulo-equivalent tick that is already committed may be overwritten.
                // RingWrap is only an error while the occupied different tick remains uncommitted.
                if (_cellTicks[cell] > committed)
                {
                    return FixedInputAdmissionDisposition.RingWrap;
                }
            }

            if (_cellOccupied[cell] && _cellTicks[cell] == tick)
            {
                if (PayloadEquals(cell, payload))
                {
                    return FixedInputAdmissionDisposition.Duplicate;
                }

                return FixedInputAdmissionDisposition.Conflict;
            }

            bool outOfOrder = _hasAcceptedBySeat[seatSlot] && tick < _lastAcceptedTickBySeat[seatSlot];
            return outOfOrder
                ? FixedInputAdmissionDisposition.AcceptedOutOfOrder
                : FixedInputAdmissionDisposition.Accepted;
        }

        private void WriteFrame(
            int seatSlot,
            uint tick,
            ReadOnlySpan<byte> payload,
            FixedInputAdmissionDisposition disposition)
        {
            int cell = CellIndex(seatSlot, tick);
            _cellTicks[cell] = tick;
            _cellOccupied[cell] = true;
            payload.CopyTo(_payloads.AsSpan(cell * _payloadBytes, _payloadBytes));

            if (!_hasAcceptedBySeat[seatSlot])
            {
                _activationTicksBySeat[seatSlot] = tick;
            }

            if (!_hasAcceptedBySeat[seatSlot] || tick > _lastAcceptedTickBySeat[seatSlot])
            {
                _lastAcceptedTickBySeat[seatSlot] = tick;
            }

            _hasAcceptedBySeat[seatSlot] = true;
            if (tick > _latestReceivedTickBySeat[seatSlot])
            {
                _latestReceivedTickBySeat[seatSlot] = tick;
            }

            _ = disposition;
        }

        private void ApplyDispositionCounters(FixedInputAdmissionDisposition disposition)
        {
            switch (disposition)
            {
                case FixedInputAdmissionDisposition.Accepted:
                    AcceptedCount++;
                    break;
                case FixedInputAdmissionDisposition.AcceptedOutOfOrder:
                    AcceptedOutOfOrderCount++;
                    break;
                case FixedInputAdmissionDisposition.Duplicate:
                    DuplicateCount++;
                    break;
                case FixedInputAdmissionDisposition.Conflict:
                    ConflictCount++;
                    break;
                case FixedInputAdmissionDisposition.Late:
                    LateCount++;
                    break;
                case FixedInputAdmissionDisposition.TooFarFuture:
                    TooFarFutureCount++;
                    break;
                case FixedInputAdmissionDisposition.RejectedAtExecutionCutoff:
                    ExecutionCutoffRejectionCount++;
                    break;
                case FixedInputAdmissionDisposition.RingWrap:
                    RingWrapCount++;
                    break;
                default:
                    break;
            }
        }

        private FixedInputBatchAdmissionStatus RejectBatch(
            Span<FixedInputAdmissionDisposition> dispositions,
            int frameCount,
            FixedInputAdmissionDisposition reason)
        {
            BatchRejectedCount++;
            if (IsHardReject(reason) && reason is not FixedInputAdmissionDisposition.BatchRejected)
            {
                ApplyDispositionCounters(reason);
            }

            int fill = Math.Min(frameCount, dispositions.Length);
            for (int i = 0; i < fill; i++)
            {
                dispositions[i] = reason;
            }

            return FixedInputBatchAdmissionStatus.Rejected;
        }

        private static bool IsHardReject(FixedInputAdmissionDisposition disposition) =>
            disposition is FixedInputAdmissionDisposition.InvalidSeatGeneration
                or FixedInputAdmissionDisposition.EpochMismatch
                or FixedInputAdmissionDisposition.SchemaMismatch
                or FixedInputAdmissionDisposition.PayloadMismatch
                or FixedInputAdmissionDisposition.RingWrap
                or FixedInputAdmissionDisposition.Conflict
                or FixedInputAdmissionDisposition.ReservedNonZero
                or FixedInputAdmissionDisposition.InvalidFrameOrder
                or FixedInputAdmissionDisposition.TickOutOfRange
                or FixedInputAdmissionDisposition.BatchRejected;

        private bool PayloadEquals(int cell, ReadOnlySpan<byte> payload)
        {
            ReadOnlySpan<byte> existing = _payloads.AsSpan(cell * _payloadBytes, _payloadBytes);
            return existing.SequenceEqual(payload);
        }

        private void ClearSeatHistory(int seatSlot)
        {
            int baseIndex = seatSlot * _historyTicks;
            for (int local = 0; local < _historyTicks; local++)
            {
                int cell = baseIndex + local;
                _cellTicks[cell] = 0;
                _cellOccupied[cell] = false;
                _payloads.AsSpan(cell * _payloadBytes, _payloadBytes).Clear();
            }

            _hasAcceptedBySeat[seatSlot] = false;
            _activationTicksBySeat[seatSlot] = 0;
            _lastAcceptedTickBySeat[seatSlot] = 0;
            _latestReceivedTickBySeat[seatSlot] = 0;
            _latestMissingAtDeadlineBySeat[seatSlot] = 0;
        }

        private int CellIndex(int seatSlot, uint tick)
        {
            int tickIndex = (int)(tick % (uint)_historyTicks);
            return (seatSlot * _historyTicks) + tickIndex;
        }

        private void ValidateSeatShape(in SessionSeatBinding seat)
        {
            if (!seat.IsValid || (uint)seat.Slot >= (uint)_seatCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(seat), "Seat binding is outside fixed-input capacity.");
            }
        }

        private bool MatchesSeat(in SessionSeatBinding seat)
        {
            if (!seat.IsValid || (uint)seat.Slot >= (uint)_seatCapacity)
            {
                return false;
            }

            int slot = seat.Slot;
            return _seatBound[slot] &&
                _seatGenerations[slot] == seat.Generation &&
                _seatPlayerIds[slot] == seat.PlayerId.Value;
        }
    }
}
