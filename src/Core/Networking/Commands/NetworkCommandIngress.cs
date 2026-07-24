using System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Networking.Commands
{
    public sealed class NetworkCommandIngress
    {
        private readonly NetworkCommandIngressConfig _config;
        private readonly OrderQueue _orders;
        private readonly NetworkCommandAdmissionResultBuffer _results;
        private readonly bool[] _boundSeats;
        private readonly uint[] _seatGenerations;
        private readonly int[] _seatPlayerIds;
        private readonly long[] _rateBalances;
        private readonly int[] _rateTicks;
        private readonly long _rateCapacity;
        private readonly ulong[] _nextSequences;
        private readonly ulong[] _historySequences;
        private readonly NetworkCommandAdmissionOutcome[] _historyOutcomes;
        private readonly int[] _historyCounts;
        private readonly int[] _historyWriteIndices;

        public NetworkCommandIngress(
            in NetworkCommandIngressConfig config,
            OrderQueue orders,
            NetworkCommandAdmissionResultBuffer results)
        {
            ArgumentNullException.ThrowIfNull(orders);
            ArgumentNullException.ThrowIfNull(results);
            if (orders.Capacity < config.MaxActorsPerBatch)
            {
                throw new ArgumentException(
                    $"OrderQueue capacity {orders.Capacity} is below max actor batch {config.MaxActorsPerBatch}.",
                    nameof(orders));
            }

            _config = config;
            _orders = orders;
            _results = results;
            _boundSeats = new bool[config.SeatCapacity];
            _seatGenerations = new uint[config.SeatCapacity];
            _seatPlayerIds = new int[config.SeatCapacity];
            _rateBalances = new long[config.SeatCapacity];
            _rateTicks = new int[config.SeatCapacity];
            _rateCapacity = checked((long)config.BurstBatchCapacity * config.SimulationTickRateHz);
            _nextSequences = new ulong[config.SeatCapacity];
            int historySlots = checked(config.SeatCapacity * config.SequenceHistoryCapacity);
            _historySequences = new ulong[historySlots];
            _historyOutcomes = new NetworkCommandAdmissionOutcome[historySlots];
            _historyCounts = new int[config.SeatCapacity];
            _historyWriteIndices = new int[config.SeatCapacity];
        }

        public void BindSeat(in NetworkCommandSeat seat, int serverTick)
        {
            ValidateSeatShape(in seat);
            if (serverTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(serverTick));
            }

            int slot = seat.Slot;
            if (_boundSeats[slot])
            {
                if (_seatGenerations[slot] == seat.Generation && _seatPlayerIds[slot] == seat.PlayerId)
                {
                    return;
                }

                throw new InvalidOperationException($"Network command seat {slot} is already bound.");
            }

            _boundSeats[slot] = true;
            _seatGenerations[slot] = seat.Generation;
            _seatPlayerIds[slot] = seat.PlayerId;
            _rateBalances[slot] = _rateCapacity;
            _rateTicks[slot] = serverTick;
            _nextSequences[slot] = 1;
            _historyCounts[slot] = 0;
            _historyWriteIndices[slot] = 0;
            int historyOffset = slot * _config.SequenceHistoryCapacity;
            Array.Clear(_historySequences, historyOffset, _config.SequenceHistoryCapacity);
            Array.Clear(_historyOutcomes, historyOffset, _config.SequenceHistoryCapacity);
        }

        public NetworkCommandAdmissionOutcome Submit(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            int serverTick,
            Span<Order> orders)
        {
            if (_results.AvailableCapacity == 0)
            {
                return CreateOutcome(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    OrderSubmitResult.NetworkAdmissionBackpressured,
                    isReplay: false);
            }

            if (!IsBoundSeat(in seat))
            {
                return Publish(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    OrderSubmitResult.NetworkInvalidConnectionSeat);
            }

            ulong nextSequence = _nextSequences[seat.Slot];
            if (clientBatchSequence < nextSequence)
            {
                if (TryFindHistory(seat.Slot, clientBatchSequence, out NetworkCommandAdmissionOutcome original))
                {
                    NetworkCommandAdmissionOutcome replay = original.AsReplay();
                    if (!_results.TryWrite(in replay))
                    {
                        throw new InvalidOperationException("Network command result buffer is full.");
                    }

                    return replay;
                }

                return Publish(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    OrderSubmitResult.NetworkSequenceOutsideHistory);
            }

            if (clientBatchSequence > nextSequence)
            {
                return Publish(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    OrderSubmitResult.NetworkSequenceGap);
            }

            long targetDelta = (long)targetTick - serverTick;
            if (targetDelta < -_config.MaxPastTargetTicks)
            {
                NetworkCommandAdmissionOutcome expired = Publish(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    OrderSubmitResult.NetworkTargetTickExpired);
                RecordCompleted(seat.Slot, clientBatchSequence, in expired);
                return expired;
            }

            if (targetDelta > _config.MaxFutureTargetTicks)
            {
                NetworkCommandAdmissionOutcome tooFarAhead = Publish(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    OrderSubmitResult.NetworkTargetTickTooFarAhead);
                RecordCompleted(seat.Slot, clientBatchSequence, in tooFarAhead);
                return tooFarAhead;
            }

            if (orders.Length > _config.MaxActorsPerBatch)
            {
                NetworkCommandAdmissionOutcome actorLimit = Publish(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    OrderSubmitResult.NetworkActorLimitExceeded);
                RecordCompleted(seat.Slot, clientBatchSequence, in actorLimit);
                return actorLimit;
            }

            if (!TryValidateBatch(orders, out OrderSubmitResult validationResult))
            {
                NetworkCommandAdmissionOutcome invalid = Publish(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    validationResult);
                RecordCompleted(seat.Slot, clientBatchSequence, in invalid);
                return invalid;
            }

            if (orders.Length > _orders.AvailableCapacity)
            {
                NetworkCommandAdmissionOutcome queueFull = Publish(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    OrderSubmitResult.QueueFull);
                RecordCompleted(seat.Slot, clientBatchSequence, in queueFull);
                return queueFull;
            }

            if (!TryConsumeRate(seat.Slot, serverTick))
            {
                NetworkCommandAdmissionOutcome rateLimited = Publish(
                    in seat,
                    clientBatchSequence,
                    targetTick,
                    orders,
                    OrderSubmitResult.NetworkRateLimited);
                RecordCompleted(seat.Slot, clientBatchSequence, in rateLimited);
                return rateLimited;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                orders[i].PlayerId = _seatPlayerIds[seat.Slot];
                orders[i].SubmitStep = targetTick;
            }

            if (!_orders.TryEnqueueSharedBatch(orders))
            {
                throw new InvalidOperationException(
                    "OrderQueue capacity changed during single-writer network command admission.");
            }

            NetworkCommandAdmissionOutcome accepted = Publish(
                in seat,
                clientBatchSequence,
                targetTick,
                orders,
                OrderSubmitResult.Queued);
            RecordCompleted(seat.Slot, clientBatchSequence, in accepted);
            return accepted;
        }

        private static bool TryValidateBatch(ReadOnlySpan<Order> orders, out OrderSubmitResult result)
        {
            if (orders.IsEmpty)
            {
                result = OrderSubmitResult.ValidationRejected;
                return false;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                ref readonly Order order = ref orders[i];
                if (order.OrderTypeId <= 0 || order.OrderTypeId >= OrderTypeRegistry.MaxOrderTypes)
                {
                    result = OrderSubmitResult.InvalidOrderType;
                    return false;
                }

                if (order.Actor == Arch.Core.Entity.Null)
                {
                    result = OrderSubmitResult.InvalidEntity;
                    return false;
                }

                if (order.OrderId != 0 ||
                    order.AdmissionBatchId != 0 ||
                    order.AdmissionBatchSize != 0 ||
                    order.AdmissionBatchIndex != 0)
                {
                    result = OrderSubmitResult.ValidationRejected;
                    return false;
                }

                for (int prior = 0; prior < i; prior++)
                {
                    if (orders[prior].Actor == order.Actor)
                    {
                        result = OrderSubmitResult.ValidationRejected;
                        return false;
                    }
                }
            }

            result = OrderSubmitResult.Queued;
            return true;
        }

        private bool TryFindHistory(
            int slot,
            ulong clientBatchSequence,
            out NetworkCommandAdmissionOutcome outcome)
        {
            int offset = slot * _config.SequenceHistoryCapacity;
            int count = _historyCounts[slot];
            for (int i = 0; i < count; i++)
            {
                if (_historySequences[offset + i] == clientBatchSequence)
                {
                    outcome = _historyOutcomes[offset + i];
                    return true;
                }
            }

            outcome = default;
            return false;
        }

        private void RecordCompleted(
            int slot,
            ulong clientBatchSequence,
            in NetworkCommandAdmissionOutcome outcome)
        {
            int writeIndex = _historyWriteIndices[slot];
            int index = (slot * _config.SequenceHistoryCapacity) + writeIndex;
            _historySequences[index] = clientBatchSequence;
            _historyOutcomes[index] = outcome;
            _historyWriteIndices[slot] = (writeIndex + 1) % _config.SequenceHistoryCapacity;
            if (_historyCounts[slot] < _config.SequenceHistoryCapacity)
            {
                _historyCounts[slot]++;
            }

            _nextSequences[slot] = checked(clientBatchSequence + 1);
        }

        private bool TryConsumeRate(int slot, int serverTick)
        {
            long elapsedTicks = (long)serverTick - _rateTicks[slot];
            if (elapsedTicks < 0)
            {
                throw new InvalidOperationException(
                    $"Server tick regressed from {_rateTicks[slot]} to {serverTick} for command seat {slot}.");
            }

            long replenished = _rateBalances[slot] +
                ((long)elapsedTicks * _config.MaxBatchesPerSecond);
            long balance = Math.Min(_rateCapacity, replenished);
            if (balance < _config.SimulationTickRateHz)
            {
                return false;
            }

            _rateBalances[slot] = balance - _config.SimulationTickRateHz;
            _rateTicks[slot] = serverTick;
            return true;
        }

        private NetworkCommandAdmissionOutcome Publish(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            ReadOnlySpan<Order> orders,
            OrderSubmitResult result)
        {
            NetworkCommandAdmissionOutcome outcome = CreateOutcome(
                in seat,
                clientBatchSequence,
                targetTick,
                orders,
                result,
                isReplay: false);
            if (!_results.TryWrite(in outcome))
            {
                throw new InvalidOperationException("Network command result buffer is full.");
            }

            return outcome;
        }

        private static NetworkCommandAdmissionOutcome CreateOutcome(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            ReadOnlySpan<Order> orders,
            OrderSubmitResult result,
            bool isReplay)
        {
            int orderId = orders.IsEmpty ? 0 : orders[0].OrderId;
            int admissionBatchId = orders.IsEmpty ? 0 : orders[0].AdmissionBatchId;
            return new NetworkCommandAdmissionOutcome(
                in seat,
                clientBatchSequence,
                targetTick,
                orders.Length,
                orderId,
                admissionBatchId,
                result,
                isReplay);
        }

        private void ValidateSeatShape(in NetworkCommandSeat seat)
        {
            if ((uint)seat.Slot >= (uint)_config.SeatCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(seat), $"Seat slot {seat.Slot} is outside configured capacity.");
            }

            if (seat.Generation == 0 || seat.PlayerId <= 0)
            {
                throw new ArgumentException("Seat generation and player id must be positive.", nameof(seat));
            }
        }

        private bool IsBoundSeat(in NetworkCommandSeat seat)
        {
            if ((uint)seat.Slot >= (uint)_config.SeatCapacity ||
                seat.Generation == 0 ||
                seat.PlayerId <= 0)
            {
                return false;
            }

            int slot = seat.Slot;
            return _boundSeats[slot] &&
                _seatGenerations[slot] == seat.Generation &&
                _seatPlayerIds[slot] == seat.PlayerId;
        }
    }
}
