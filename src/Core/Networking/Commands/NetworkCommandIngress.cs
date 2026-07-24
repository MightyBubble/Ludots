using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;

namespace Ludots.Core.Networking.Commands
{
    public sealed class NetworkCommandIngress
    {
        private const byte SeatEmpty = 0;
        private const byte SeatConnected = 1;
        private const byte SeatAwaitingReconnect = 2;

        private readonly NetworkCommandIngressConfig _config;
        private readonly World _world;
        private readonly NetworkEntityTable _entities;
        private readonly ControlDomainQuery _controlDomains;
        private readonly KnowledgeProjectionResolver _knowledge;
        private readonly OrderTypeRegistry _orderTypes;
        private readonly NetworkCommandSchemaRegistry _schemas;
        private readonly NetworkGameplayCommandGate _gameplayGate;
        private readonly OrderQueue _orders;
        private readonly NetworkCommandAdmissionResultBuffer _results;

        private readonly byte[] _seatStates;
        private readonly uint[] _seatGenerations;
        private readonly int[] _seatPlayerIds;
        private readonly Entity[] _seatControllers;
        private readonly long[] _rateBalances;
        private readonly int[] _rateTicks;
        private readonly long _rateCapacity;
        private readonly ulong[] _nextSequences;
        private readonly ulong[] _historySequences;
        private readonly NetworkCommandAdmissionOutcome[] _historyOutcomes;
        private readonly int[] _historyCounts;
        private readonly int[] _historyWriteIndices;

        private readonly bool[] _scheduled;
        private readonly int[] _scheduledTargetTicks;
        private readonly int[] _scheduledSeatSlots;
        private readonly ulong[] _scheduledSequences;
        private readonly int[] _scheduledOrderCounts;
        private readonly Order[] _scheduledOrders;
        private readonly int[] _drainSlots;
        private int _scheduledBatchCount;
        private int _lastDrainTick = -1;

        public NetworkCommandIngress(
            in NetworkCommandIngressConfig config,
            World world,
            NetworkEntityTable entities,
            ControlDomainQuery controlDomains,
            KnowledgeProjectionResolver knowledge,
            OrderTypeRegistry orderTypes,
            NetworkCommandSchemaRegistry schemas,
            NetworkGameplayCommandGate gameplayGate,
            OrderQueue orders,
            NetworkCommandAdmissionResultBuffer results)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _entities = entities ?? throw new ArgumentNullException(nameof(entities));
            _controlDomains = controlDomains ?? throw new ArgumentNullException(nameof(controlDomains));
            _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            _orderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
            _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            _gameplayGate = gameplayGate ?? throw new ArgumentNullException(nameof(gameplayGate));
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _results = results ?? throw new ArgumentNullException(nameof(results));
            if (!schemas.IsFrozen)
            {
                throw new ArgumentException("Network command schemas must be frozen before ingress construction.", nameof(schemas));
            }

            if (orders.Capacity < config.MaxActorsPerBatch)
            {
                throw new ArgumentException(
                    $"OrderQueue capacity {orders.Capacity} is below max actor batch {config.MaxActorsPerBatch}.",
                    nameof(orders));
            }

            if (results.Capacity < config.ScheduledBatchCapacity)
            {
                throw new ArgumentException(
                    $"Network result capacity {results.Capacity} is below scheduled batch capacity {config.ScheduledBatchCapacity}.",
                    nameof(results));
            }

            _config = config;
            _seatStates = new byte[config.SeatCapacity];
            _seatGenerations = new uint[config.SeatCapacity];
            _seatPlayerIds = new int[config.SeatCapacity];
            _seatControllers = new Entity[config.SeatCapacity];
            _rateBalances = new long[config.SeatCapacity];
            _rateTicks = new int[config.SeatCapacity];
            _rateCapacity = checked((long)config.BurstBatchCapacity * config.SimulationTickRateHz);
            _nextSequences = new ulong[config.SeatCapacity];
            int historySlots = checked(config.SeatCapacity * config.SequenceHistoryCapacity);
            _historySequences = new ulong[historySlots];
            _historyOutcomes = new NetworkCommandAdmissionOutcome[historySlots];
            _historyCounts = new int[config.SeatCapacity];
            _historyWriteIndices = new int[config.SeatCapacity];

            _scheduled = new bool[config.ScheduledBatchCapacity];
            _scheduledTargetTicks = new int[config.ScheduledBatchCapacity];
            _scheduledSeatSlots = new int[config.ScheduledBatchCapacity];
            _scheduledSequences = new ulong[config.ScheduledBatchCapacity];
            _scheduledOrderCounts = new int[config.ScheduledBatchCapacity];
            _scheduledOrders = new Order[checked(config.ScheduledBatchCapacity * config.MaxActorsPerBatch)];
            _drainSlots = new int[config.ScheduledBatchCapacity];
        }

        public int ScheduledBatchCount => _scheduledBatchCount;

        public void BindSeat(in NetworkCommandSeat seat, Entity controller, int serverTick)
        {
            ValidateSeatShape(in seat);
            ValidateController(in seat, controller);
            if (serverTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(serverTick));
            }

            int slot = seat.Slot;
            if (_seatStates[slot] == SeatConnected &&
                _seatGenerations[slot] == seat.Generation &&
                _seatPlayerIds[slot] == seat.PlayerId &&
                _seatControllers[slot] == controller)
            {
                return;
            }

            if (_seatStates[slot] != SeatEmpty)
            {
                throw new InvalidOperationException($"Network command seat {slot} is already reserved.");
            }

            if (_seatGenerations[slot] != 0 && seat.Generation <= _seatGenerations[slot])
            {
                throw new InvalidOperationException(
                    $"Network command seat {slot} generation {seat.Generation} is not newer than released generation {_seatGenerations[slot]}.");
            }

            _seatStates[slot] = SeatConnected;
            _seatGenerations[slot] = seat.Generation;
            _seatPlayerIds[slot] = seat.PlayerId;
            _seatControllers[slot] = controller;
            _rateBalances[slot] = _rateCapacity;
            _rateTicks[slot] = serverTick;
            _nextSequences[slot] = 1;
            ClearHistory(slot);
        }

        public bool UnbindSeat(in NetworkCommandSeat seat)
        {
            if (!MatchesSeat(in seat, SeatConnected))
            {
                return false;
            }

            _seatStates[seat.Slot] = SeatAwaitingReconnect;
            return true;
        }

        public void RebindSeat(in NetworkCommandSeat seat, Entity controller, int serverTick)
        {
            ValidateSeatShape(in seat);
            ValidateController(in seat, controller);
            if (serverTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(serverTick));
            }

            if (!MatchesSeat(in seat, SeatAwaitingReconnect))
            {
                throw new InvalidOperationException(
                    $"Network command seat {seat.Slot}:{seat.Generation} is not awaiting reconnect for the supplied player.");
            }

            if (serverTick < _rateTicks[seat.Slot])
            {
                throw new InvalidOperationException(
                    $"Server tick regressed from {_rateTicks[seat.Slot]} to {serverTick} for command seat {seat.Slot}.");
            }

            _seatStates[seat.Slot] = SeatConnected;
            _seatControllers[seat.Slot] = controller;
        }

        public bool TryReleaseSeat(in NetworkCommandSeat seat)
        {
            if (!MatchesSeat(in seat, SeatConnected) && !MatchesSeat(in seat, SeatAwaitingReconnect))
            {
                return false;
            }

            int pendingCount = CountScheduledForSeat(seat.Slot);
            if (_results.AvailableCapacity < pendingCount)
            {
                return false;
            }

            for (int i = 0; i < _scheduled.Length; i++)
            {
                if (!_scheduled[i] || _scheduledSeatSlots[i] != seat.Slot)
                {
                    continue;
                }

                NetworkCommandAdmissionOutcome cancelled = CreateScheduledOutcome(
                    i,
                    OrderSubmitResult.NetworkInvalidConnectionSeat,
                    isReplay: false);
                WriteResult(in cancelled);
                UpdateHistory(seat.Slot, _scheduledSequences[i], in cancelled);
                ReleaseScheduledSlot(i);
            }

            int slot = seat.Slot;
            _seatStates[slot] = SeatEmpty;
            _seatPlayerIds[slot] = 0;
            _seatControllers[slot] = Entity.Null;
            _rateBalances[slot] = 0;
            _rateTicks[slot] = 0;
            _nextSequences[slot] = 0;
            ClearHistory(slot);
            return true;
        }

        public bool TryGetSeat(int slot, out NetworkCommandSeat seat, out bool connected)
        {
            if ((uint)slot >= (uint)_seatStates.Length || _seatStates[slot] == SeatEmpty)
            {
                seat = default;
                connected = false;
                return false;
            }

            seat = new NetworkCommandSeat(slot, _seatGenerations[slot], _seatPlayerIds[slot]);
            connected = _seatStates[slot] == SeatConnected;
            return true;
        }

        public NetworkCommandAdmissionOutcome Schedule(
            in NetworkCommandSeat seat,
            in NetworkCommandBatchHeader header,
            int serverTick,
            ReadOnlySpan<NetworkCommandWireEntry> entries)
        {
            if (_results.AvailableCapacity == 0)
            {
                return CreateOutcome(
                    in seat,
                    header.ClientBatchSequence,
                    header.TargetTick,
                    entries.Length,
                    orderId: 0,
                    admissionBatchId: 0,
                    OrderSubmitResult.NetworkAdmissionBackpressured,
                    isReplay: false);
            }

            if (!MatchesSeat(in seat, SeatConnected) || !_world.IsAlive(_seatControllers[seat.Slot]))
            {
                return Publish(
                    in seat,
                    header.ClientBatchSequence,
                    header.TargetTick,
                    entries.Length,
                    OrderSubmitResult.NetworkInvalidConnectionSeat);
            }

            ulong nextSequence = _nextSequences[seat.Slot];
            if (header.ClientBatchSequence < nextSequence)
            {
                if (TryFindHistory(seat.Slot, header.ClientBatchSequence, out NetworkCommandAdmissionOutcome original))
                {
                    NetworkCommandAdmissionOutcome replay = original.AsReplay();
                    WriteResult(in replay);
                    return replay;
                }

                return Publish(
                    in seat,
                    header.ClientBatchSequence,
                    header.TargetTick,
                    entries.Length,
                    OrderSubmitResult.NetworkSequenceOutsideHistory);
            }

            if (header.ClientBatchSequence > nextSequence || header.ClientBatchSequence == 0)
            {
                return Publish(
                    in seat,
                    header.ClientBatchSequence,
                    header.TargetTick,
                    entries.Length,
                    OrderSubmitResult.NetworkSequenceGap);
            }

            if (!_gameplayGate.TryAdmit(out OrderSubmitResult phaseRejection))
            {
                return CompleteRejected(
                    in seat,
                    in header,
                    entries.Length,
                    phaseRejection);
            }

            long targetDelta = (long)header.TargetTick - serverTick;
            if (targetDelta < -_config.MaxPastTargetTicks)
            {
                return CompleteRejected(
                    in seat,
                    in header,
                    entries.Length,
                    OrderSubmitResult.NetworkTargetTickExpired);
            }

            if (targetDelta > _config.MaxFutureTargetTicks)
            {
                return CompleteRejected(
                    in seat,
                    in header,
                    entries.Length,
                    OrderSubmitResult.NetworkTargetTickTooFarAhead);
            }

            if (entries.IsEmpty || header.SessionEpoch == 0 || header.EntryCount != entries.Length)
            {
                return CompleteRejected(
                    in seat,
                    in header,
                    entries.Length,
                    OrderSubmitResult.NetworkCommandSchemaMismatch);
            }

            if (entries.Length > _config.MaxActorsPerBatch)
            {
                return CompleteRejected(
                    in seat,
                    in header,
                    entries.Length,
                    OrderSubmitResult.NetworkActorLimitExceeded);
            }

            if (!TryFindFreeScheduledSlot(out int scheduledSlot))
            {
                return CompleteRejected(
                    in seat,
                    in header,
                    entries.Length,
                    OrderSubmitResult.NetworkScheduleFull);
            }

            Span<Order> destination = GetScheduledOrders(scheduledSlot, entries.Length);
            if (!TryMaterializeBatch(
                    seat.Slot,
                    serverTick,
                    entries,
                    destination,
                    out OrderSubmitResult validationResult))
            {
                return CompleteRejected(in seat, in header, entries.Length, validationResult);
            }

            if (!TryConsumeRate(seat.Slot, serverTick))
            {
                return CompleteRejected(
                    in seat,
                    in header,
                    entries.Length,
                    OrderSubmitResult.NetworkRateLimited);
            }

            _scheduled[scheduledSlot] = true;
            _scheduledTargetTicks[scheduledSlot] = header.TargetTick;
            _scheduledSeatSlots[scheduledSlot] = seat.Slot;
            _scheduledSequences[scheduledSlot] = header.ClientBatchSequence;
            _scheduledOrderCounts[scheduledSlot] = entries.Length;
            _scheduledBatchCount++;

            NetworkCommandAdmissionOutcome accepted = Publish(
                in seat,
                header.ClientBatchSequence,
                header.TargetTick,
                entries.Length,
                OrderSubmitResult.NetworkScheduled);
            RecordCompleted(seat.Slot, header.ClientBatchSequence, in accepted);
            return accepted;
        }

        public int DrainScheduled(int serverTick)
        {
            if (serverTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(serverTick));
            }

            if (_lastDrainTick > serverTick)
            {
                throw new InvalidOperationException(
                    $"Network command drain tick regressed from {_lastDrainTick} to {serverTick}.");
            }

            _lastDrainTick = serverTick;
            int dueCount = CollectDueBatches(serverTick);
            int processed = 0;
            for (int i = 0; i < dueCount; i++)
            {
                if (_results.AvailableCapacity == 0)
                {
                    break;
                }

                int scheduledSlot = _drainSlots[i];
                int orderCount = _scheduledOrderCounts[scheduledSlot];
                Span<Order> batch = GetScheduledOrders(scheduledSlot, orderCount);
                OrderSubmitResult result;
                if (orderCount > _orders.AvailableCapacity)
                {
                    result = OrderSubmitResult.QueueFull;
                }
                else
                {
                    if (!_orders.TryEnqueueSharedBatch(batch))
                    {
                        throw new InvalidOperationException(
                            "OrderQueue capacity changed during single-writer network command admission.");
                    }

                    result = OrderSubmitResult.Queued;
                }

                NetworkCommandAdmissionOutcome outcome = CreateScheduledOutcome(
                    scheduledSlot,
                    result,
                    isReplay: false);
                WriteResult(in outcome);
                UpdateHistory(_scheduledSeatSlots[scheduledSlot], _scheduledSequences[scheduledSlot], in outcome);
                ReleaseScheduledSlot(scheduledSlot);
                processed++;
            }

            return processed;
        }

        private bool TryMaterializeBatch(
            int seatSlot,
            int serverTick,
            ReadOnlySpan<NetworkCommandWireEntry> entries,
            Span<Order> destination,
            out OrderSubmitResult result)
        {
            Entity controller = _seatControllers[seatSlot];
            int playerId = _seatPlayerIds[seatSlot];
            for (int i = 0; i < entries.Length; i++)
            {
                ref readonly NetworkCommandWireEntry entry = ref entries[i];
                if (!entry.Actor.IsValid)
                {
                    result = OrderSubmitResult.NetworkInvalidActorHandle;
                    return false;
                }

                if (!_entities.TryResolve(entry.Actor, out Entity actor) || !_world.IsAlive(actor))
                {
                    result = OrderSubmitResult.NetworkStaleActorGeneration;
                    return false;
                }

                if (!_controlDomains.IsControllableBy(controller, actor))
                {
                    result = OrderSubmitResult.NetworkActorNotControlled;
                    return false;
                }

                for (int prior = 0; prior < i; prior++)
                {
                    if (destination[prior].Actor == actor)
                    {
                        result = OrderSubmitResult.NetworkCommandSchemaMismatch;
                        return false;
                    }
                }

                NetworkCommandTargetPayload targetPayload = entry.Target;
                if (!_orderTypes.IsRegistered(entry.OrderTypeId) ||
                    !_schemas.TryGet(entry.OrderTypeId, out NetworkCommandSchema schema) ||
                    !TryValidateTargetShape(in targetPayload, in schema))
                {
                    result = OrderSubmitResult.NetworkCommandSchemaMismatch;
                    return false;
                }

                Entity target = Entity.Null;
                bool hasEntityTarget = schema.TargetKind is NetworkCommandTargetKind.NetworkEntity or
                    NetworkCommandTargetKind.WorldPositionAndEntity;
                if (hasEntityTarget)
                {
                    if (!entry.Target.TryGetTargetEntity(out NetworkEntityHandle targetHandle))
                    {
                        result = OrderSubmitResult.NetworkInvalidTargetHandle;
                        return false;
                    }

                    if (!_entities.TryResolve(targetHandle, out target) || !_world.IsAlive(target))
                    {
                        result = OrderSubmitResult.NetworkStaleTargetGeneration;
                        return false;
                    }

                    bool canTarget = schema.RequiredTargetPositionAccess == KnowledgePositionAccess.None
                        ? _knowledge.CanKnowEntity(controller, target, serverTick)
                        : _knowledge.CanReadPosition(
                            controller,
                            target,
                            serverTick,
                            schema.RequiredTargetPositionAccess);
                    if (!canTarget)
                    {
                        result = OrderSubmitResult.NetworkTargetNotKnown;
                        return false;
                    }
                }

                OrderArgs args = default;
                if (schema.TargetKind is NetworkCommandTargetKind.WorldPositionCm or
                    NetworkCommandTargetKind.WorldPositionAndEntity)
                {
                    args = OrderArgs.CreateSingleWorldCm(new Vector3(
                        entry.Target.PositionXCm,
                        entry.Target.PositionYCm,
                        entry.Target.PositionZCm));
                }

                args.I0 = entry.Target.Arg0;
                args.I1 = entry.Target.Arg1;
                destination[i] = new Order
                {
                    OrderTypeId = entry.OrderTypeId,
                    PlayerId = playerId,
                    Actor = actor,
                    Target = target,
                    Args = args,
                    SubmitMode = schema.SubmitMode,
                };
            }

            result = OrderSubmitResult.NetworkScheduled;
            return true;
        }

        private static bool TryValidateTargetShape(
            in NetworkCommandTargetPayload target,
            in NetworkCommandSchema schema)
        {
            if (target.Kind != schema.TargetKind ||
                (!schema.AllowArg0 && target.Arg0 != 0) ||
                (!schema.AllowArg1 && target.Arg1 != 0))
            {
                return false;
            }

            bool hasPosition = target.Kind is NetworkCommandTargetKind.WorldPositionCm or
                NetworkCommandTargetKind.WorldPositionAndEntity;
            bool hasEntity = target.Kind is NetworkCommandTargetKind.NetworkEntity or
                NetworkCommandTargetKind.WorldPositionAndEntity;
            if (!hasPosition &&
                (target.PositionXCm != 0 || target.PositionYCm != 0 || target.PositionZCm != 0))
            {
                return false;
            }

            if (!hasEntity && (target.TargetSlot != 0 || target.TargetGeneration != 0))
            {
                return false;
            }

            return true;
        }

        private int CollectDueBatches(int serverTick)
        {
            int count = 0;
            for (int slot = 0; slot < _scheduled.Length; slot++)
            {
                if (!_scheduled[slot] || _scheduledTargetTicks[slot] > serverTick)
                {
                    continue;
                }

                int insert = count;
                while (insert > 0 && CompareScheduled(slot, _drainSlots[insert - 1]) < 0)
                {
                    _drainSlots[insert] = _drainSlots[insert - 1];
                    insert--;
                }

                _drainSlots[insert] = slot;
                count++;
            }

            return count;
        }

        private int CompareScheduled(int left, int right)
        {
            int byTick = _scheduledTargetTicks[left].CompareTo(_scheduledTargetTicks[right]);
            if (byTick != 0)
            {
                return byTick;
            }

            int bySeat = _scheduledSeatSlots[left].CompareTo(_scheduledSeatSlots[right]);
            if (bySeat != 0)
            {
                return bySeat;
            }

            return _scheduledSequences[left].CompareTo(_scheduledSequences[right]);
        }

        private NetworkCommandAdmissionOutcome CompleteRejected(
            in NetworkCommandSeat seat,
            in NetworkCommandBatchHeader header,
            int actorCount,
            OrderSubmitResult result)
        {
            NetworkCommandAdmissionOutcome outcome = Publish(
                in seat,
                header.ClientBatchSequence,
                header.TargetTick,
                actorCount,
                result);
            RecordCompleted(seat.Slot, header.ClientBatchSequence, in outcome);
            return outcome;
        }

        private NetworkCommandAdmissionOutcome CreateScheduledOutcome(
            int scheduledSlot,
            OrderSubmitResult result,
            bool isReplay)
        {
            int seatSlot = _scheduledSeatSlots[scheduledSlot];
            var seat = new NetworkCommandSeat(
                seatSlot,
                _seatGenerations[seatSlot],
                _seatPlayerIds[seatSlot]);
            ReadOnlySpan<Order> orders = GetScheduledOrders(
                scheduledSlot,
                _scheduledOrderCounts[scheduledSlot]);
            int orderId = orders.IsEmpty ? 0 : orders[0].OrderId;
            int admissionBatchId = orders.IsEmpty ? 0 : orders[0].AdmissionBatchId;
            return CreateOutcome(
                in seat,
                _scheduledSequences[scheduledSlot],
                _scheduledTargetTicks[scheduledSlot],
                orders.Length,
                orderId,
                admissionBatchId,
                result,
                isReplay);
        }

        private bool TryFindFreeScheduledSlot(out int slot)
        {
            if (_scheduledBatchCount >= _scheduled.Length)
            {
                slot = -1;
                return false;
            }

            for (int i = 0; i < _scheduled.Length; i++)
            {
                if (!_scheduled[i])
                {
                    slot = i;
                    return true;
                }
            }

            throw new InvalidOperationException("Scheduled command count is inconsistent with its slot table.");
        }

        private Span<Order> GetScheduledOrders(int scheduledSlot, int count)
        {
            int offset = scheduledSlot * _config.MaxActorsPerBatch;
            return _scheduledOrders.AsSpan(offset, count);
        }

        private int CountScheduledForSeat(int seatSlot)
        {
            int count = 0;
            for (int i = 0; i < _scheduled.Length; i++)
            {
                count += _scheduled[i] && _scheduledSeatSlots[i] == seatSlot ? 1 : 0;
            }

            return count;
        }

        private void ReleaseScheduledSlot(int scheduledSlot)
        {
            _scheduled[scheduledSlot] = false;
            _scheduledTargetTicks[scheduledSlot] = 0;
            _scheduledSeatSlots[scheduledSlot] = 0;
            _scheduledSequences[scheduledSlot] = 0;
            int orderCount = _scheduledOrderCounts[scheduledSlot];
            GetScheduledOrders(scheduledSlot, orderCount).Clear();
            _scheduledOrderCounts[scheduledSlot] = 0;
            _scheduledBatchCount--;
        }

        private bool TryFindHistory(
            int slot,
            ulong clientBatchSequence,
            out NetworkCommandAdmissionOutcome outcome)
        {
            if (TryFindHistoryIndex(slot, clientBatchSequence, out int index))
            {
                outcome = _historyOutcomes[index];
                return true;
            }

            outcome = default;
            return false;
        }

        private bool TryFindHistoryIndex(int slot, ulong clientBatchSequence, out int index)
        {
            int offset = slot * _config.SequenceHistoryCapacity;
            int count = _historyCounts[slot];
            for (int i = 0; i < count; i++)
            {
                int candidate = offset + i;
                if (_historySequences[candidate] == clientBatchSequence)
                {
                    index = candidate;
                    return true;
                }
            }

            index = -1;
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

        private void UpdateHistory(
            int slot,
            ulong clientBatchSequence,
            in NetworkCommandAdmissionOutcome outcome)
        {
            if (!TryFindHistoryIndex(slot, clientBatchSequence, out int index))
            {
                throw new InvalidOperationException(
                    $"Scheduled command sequence {clientBatchSequence} is missing from seat {slot} history.");
            }

            _historyOutcomes[index] = outcome;
        }

        private void ClearHistory(int slot)
        {
            _historyCounts[slot] = 0;
            _historyWriteIndices[slot] = 0;
            int historyOffset = slot * _config.SequenceHistoryCapacity;
            Array.Clear(_historySequences, historyOffset, _config.SequenceHistoryCapacity);
            Array.Clear(_historyOutcomes, historyOffset, _config.SequenceHistoryCapacity);
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
                (elapsedTicks * _config.MaxBatchesPerSecond);
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
            int actorCount,
            OrderSubmitResult result)
        {
            NetworkCommandAdmissionOutcome outcome = CreateOutcome(
                in seat,
                clientBatchSequence,
                targetTick,
                actorCount,
                orderId: 0,
                admissionBatchId: 0,
                result,
                isReplay: false);
            WriteResult(in outcome);
            return outcome;
        }

        private void WriteResult(in NetworkCommandAdmissionOutcome outcome)
        {
            if (!_results.TryWrite(in outcome))
            {
                throw new InvalidOperationException("Network command result buffer is full.");
            }
        }

        private static NetworkCommandAdmissionOutcome CreateOutcome(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            int actorCount,
            int orderId,
            int admissionBatchId,
            OrderSubmitResult result,
            bool isReplay)
        {
            return new NetworkCommandAdmissionOutcome(
                in seat,
                clientBatchSequence,
                targetTick,
                actorCount,
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

        private void ValidateController(in NetworkCommandSeat seat, Entity controller)
        {
            if (!_world.IsAlive(controller) ||
                !_world.TryGet(controller, out PlayerIdentity identity) ||
                identity.PlayerId != seat.PlayerId)
            {
                throw new ArgumentException(
                    $"Seat player {seat.PlayerId} requires its live PlayerIdentity representative.",
                    nameof(controller));
            }
        }

        private bool MatchesSeat(in NetworkCommandSeat seat, byte requiredState)
        {
            if ((uint)seat.Slot >= (uint)_config.SeatCapacity ||
                seat.Generation == 0 ||
                seat.PlayerId <= 0)
            {
                return false;
            }

            int slot = seat.Slot;
            return _seatStates[slot] == requiredState &&
                _seatGenerations[slot] == seat.Generation &&
                _seatPlayerIds[slot] == seat.PlayerId;
        }
    }
}
