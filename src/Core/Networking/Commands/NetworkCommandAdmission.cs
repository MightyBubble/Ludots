using System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Networking.Commands
{
    public readonly struct NetworkCommandSeat
    {
        public NetworkCommandSeat(int slot, uint generation, int playerId)
        {
            Slot = slot;
            Generation = generation;
            PlayerId = playerId;
        }

        public int Slot { get; }
        public uint Generation { get; }
        public int PlayerId { get; }
    }

    public readonly struct NetworkCommandAdmissionOutcome
    {
        public NetworkCommandAdmissionOutcome(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            int actorCount,
            int orderId,
            int admissionBatchId,
            OrderSubmitResult result,
            bool isReplay)
            : this(
                in seat,
                clientBatchSequence,
                targetTick,
                actorCount,
                orderId,
                admissionBatchId,
                admissionBatchIndex: 0,
                DeriveStage(result),
                result,
                isReplay)
        {
        }

        public NetworkCommandAdmissionOutcome(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            int actorCount,
            int orderId,
            int admissionBatchId,
            ushort admissionBatchIndex,
            OrderAdmissionStage stage,
            OrderSubmitResult result,
            bool isReplay)
        {
            if (!IsKnownStage(stage))
            {
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown order admission stage.");
            }

            SeatSlot = seat.Slot;
            SeatGeneration = seat.Generation;
            PlayerId = seat.PlayerId;
            ClientBatchSequence = clientBatchSequence;
            TargetTick = targetTick;
            ActorCount = actorCount;
            OrderId = orderId;
            AdmissionBatchId = admissionBatchId;
            AdmissionBatchIndex = admissionBatchIndex;
            Stage = stage;
            Result = result;
            IsReplay = isReplay;
        }

        public int SeatSlot { get; }
        public uint SeatGeneration { get; }
        public int PlayerId { get; }
        public ulong ClientBatchSequence { get; }
        public int TargetTick { get; }
        public int ActorCount { get; }
        public int OrderId { get; }
        public int AdmissionBatchId { get; }
        public ushort AdmissionBatchIndex { get; }
        public OrderAdmissionStage Stage { get; }
        public OrderSubmitResult Result { get; }
        public bool IsReplay { get; }

        public NetworkCommandAdmissionOutcome AsReplay()
        {
            var seat = new NetworkCommandSeat(SeatSlot, SeatGeneration, PlayerId);
            return new NetworkCommandAdmissionOutcome(
                in seat,
                ClientBatchSequence,
                TargetTick,
                ActorCount,
                OrderId,
                AdmissionBatchId,
                AdmissionBatchIndex,
                Stage,
                Result,
                isReplay: true);
        }

        private static OrderAdmissionStage DeriveStage(OrderSubmitResult result) =>
            result is OrderSubmitResult.Queued or OrderSubmitResult.QueueFull
                ? OrderAdmissionStage.GlobalIntake
                : OrderAdmissionStage.NetworkIntake;

        private static bool IsKnownStage(OrderAdmissionStage stage) =>
            stage is OrderAdmissionStage.GlobalIntake
                or OrderAdmissionStage.EntityIntake
                or OrderAdmissionStage.NetworkIntake;
    }

    public sealed class NetworkCommandAdmissionResultBuffer
    {
        private readonly NetworkCommandAdmissionOutcome[] _items;
        private int _head;
        private int _count;

        public NetworkCommandAdmissionResultBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Result capacity must be positive.");
            }

            _items = new NetworkCommandAdmissionOutcome[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int AvailableCapacity => _items.Length - _count;

        public bool TryWrite(in NetworkCommandAdmissionOutcome outcome)
        {
            if (_count == _items.Length)
            {
                return false;
            }

            int tail = (_head + _count) % _items.Length;
            _items[tail] = outcome;
            _count++;
            return true;
        }

        public bool TryRead(out NetworkCommandAdmissionOutcome outcome)
        {
            if (_count == 0)
            {
                outcome = default;
                return false;
            }

            outcome = _items[_head];
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }

    public readonly struct NetworkStagedCommandFeedback
    {
        internal NetworkStagedCommandFeedback(
            in NetworkCommandAdmissionOutcome latestOutcome,
            int entityOutcomeCount,
            int activatedCount,
            int queuedCount,
            int pendingCount,
            int rejectedCount,
            bool isTerminal,
            ulong revision)
        {
            LatestOutcome = latestOutcome;
            EntityOutcomeCount = entityOutcomeCount;
            ActivatedCount = activatedCount;
            QueuedCount = queuedCount;
            PendingCount = pendingCount;
            RejectedCount = rejectedCount;
            IsTerminal = isTerminal;
            Revision = revision;
        }

        public NetworkCommandAdmissionOutcome LatestOutcome { get; }
        public ulong ClientBatchSequence => LatestOutcome.ClientBatchSequence;
        public OrderAdmissionStage Stage => LatestOutcome.Stage;
        public OrderSubmitResult Result => LatestOutcome.Result;
        public int EntityOutcomeCount { get; }
        public int ActivatedCount { get; }
        public int QueuedCount { get; }
        public int PendingCount { get; }
        public int RejectedCount { get; }
        public bool IsTerminal { get; }
        public ulong Revision { get; }
    }

    /// <summary>
    /// Fixed-capacity latest-state store for player-visible command feedback. Completed entries
    /// are retired only when a new command needs their slot; in-flight entries are never evicted.
    /// </summary>
    public sealed class NetworkStagedCommandFeedbackStore
    {
        private const byte UnsetEntityResult = byte.MaxValue;

        private readonly NetworkStagedCommandFeedback[] _items;
        private readonly bool[] _active;
        private readonly byte[] _entityResults;
        private readonly int _maxActorsPerCommandBatch;
        private int _count;
        private ulong _revision;

        public NetworkStagedCommandFeedbackStore(int capacity, int maxActorsPerCommandBatch)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Feedback capacity must be positive.");
            }

            if (maxActorsPerCommandBatch <= 0 || maxActorsPerCommandBatch > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxActorsPerCommandBatch),
                    maxActorsPerCommandBatch,
                    "Maximum actors per command batch must be between 1 and 65535.");
            }

            _items = new NetworkStagedCommandFeedback[capacity];
            _active = new bool[capacity];
            _entityResults = new byte[checked(capacity * maxActorsPerCommandBatch)];
            Array.Fill(_entityResults, UnsetEntityResult);
            _maxActorsPerCommandBatch = maxActorsPerCommandBatch;
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int AvailableCapacity => _items.Length - _count;
        public long RetiredCompletedCount { get; private set; }

        public bool TryObserve(in NetworkCommandAdmissionOutcome outcome)
        {
            if (outcome.ClientBatchSequence == 0 ||
                outcome.ActorCount <= 0 ||
                outcome.ActorCount > _maxActorsPerCommandBatch)
            {
                return false;
            }

            int slot = Find(outcome.ClientBatchSequence);
            if (slot < 0)
            {
                if (outcome.Stage != OrderAdmissionStage.NetworkIntake)
                {
                    return false;
                }

                slot = AcquireSlot();
                if (slot < 0)
                {
                    return false;
                }

                ClearEntityResults(slot);
                _items[slot] = default;
                _active[slot] = true;
                _count++;
            }

            ref readonly NetworkStagedCommandFeedback current = ref _items[slot];
            if (current.Revision != 0 &&
                (current.LatestOutcome.SeatSlot != outcome.SeatSlot ||
                 current.LatestOutcome.SeatGeneration != outcome.SeatGeneration ||
                 current.LatestOutcome.PlayerId != outcome.PlayerId ||
                 current.LatestOutcome.ActorCount != outcome.ActorCount ||
                 current.LatestOutcome.TargetTick != outcome.TargetTick))
            {
                return false;
            }

            return outcome.Stage switch
            {
                OrderAdmissionStage.NetworkIntake => ObserveNetwork(slot, in outcome),
                OrderAdmissionStage.GlobalIntake => ObserveGlobal(slot, in outcome),
                OrderAdmissionStage.EntityIntake => ObserveEntity(slot, in outcome),
                _ => false,
            };
        }

        public bool TryGet(ulong clientBatchSequence, out NetworkStagedCommandFeedback feedback)
        {
            int slot = Find(clientBatchSequence);
            if (slot < 0)
            {
                feedback = default;
                return false;
            }

            feedback = _items[slot];
            return true;
        }

        public void Clear()
        {
            Array.Clear(_items);
            Array.Clear(_active);
            Array.Fill(_entityResults, UnsetEntityResult);
            _count = 0;
            _revision = 0;
            RetiredCompletedCount = 0;
        }

        private bool ObserveNetwork(int slot, in NetworkCommandAdmissionOutcome outcome)
        {
            NetworkStagedCommandFeedback current = _items[slot];
            if (current.Revision != 0)
            {
                return current.Stage == OrderAdmissionStage.NetworkIntake &&
                    current.Result == outcome.Result;
            }

            bool terminal = outcome.Result != OrderSubmitResult.NetworkScheduled;
            Write(slot, in outcome, 0, 0, 0, 0, 0, terminal);
            return true;
        }

        private bool ObserveGlobal(int slot, in NetworkCommandAdmissionOutcome outcome)
        {
            NetworkStagedCommandFeedback current = _items[slot];
            if (current.IsTerminal ||
                current.Stage != OrderAdmissionStage.NetworkIntake ||
                current.Result != OrderSubmitResult.NetworkScheduled ||
                outcome.AdmissionBatchId <= 0)
            {
                return false;
            }

            bool terminal = outcome.Result != OrderSubmitResult.Queued;
            Write(slot, in outcome, 0, 0, 0, 0, 0, terminal);
            return true;
        }

        private bool ObserveEntity(int slot, in NetworkCommandAdmissionOutcome outcome)
        {
            NetworkStagedCommandFeedback current = _items[slot];
            if (current.IsTerminal ||
                current.Stage == OrderAdmissionStage.NetworkIntake ||
                current.LatestOutcome.AdmissionBatchId != outcome.AdmissionBatchId ||
                outcome.AdmissionBatchIndex >= outcome.ActorCount)
            {
                return false;
            }

            int resultIndex = checked(slot * _maxActorsPerCommandBatch + outcome.AdmissionBatchIndex);
            byte encodedResult = checked((byte)outcome.Result);
            byte previous = _entityResults[resultIndex];
            if (previous != UnsetEntityResult)
            {
                return previous == encodedResult;
            }

            _entityResults[resultIndex] = encodedResult;
            int entityOutcomeCount = current.EntityOutcomeCount + 1;
            int activatedCount = current.ActivatedCount;
            int queuedCount = current.QueuedCount;
            int pendingCount = current.PendingCount;
            int rejectedCount = current.RejectedCount;
            switch (outcome.Result)
            {
                case OrderSubmitResult.Activated:
                    activatedCount++;
                    break;
                case OrderSubmitResult.Queued:
                    queuedCount++;
                    break;
                case OrderSubmitResult.Pending:
                    pendingCount++;
                    break;
                default:
                    rejectedCount++;
                    break;
            }

            Write(
                slot,
                in outcome,
                entityOutcomeCount,
                activatedCount,
                queuedCount,
                pendingCount,
                rejectedCount,
                entityOutcomeCount == outcome.ActorCount);
            return true;
        }

        private void Write(
            int slot,
            in NetworkCommandAdmissionOutcome outcome,
            int entityOutcomeCount,
            int activatedCount,
            int queuedCount,
            int pendingCount,
            int rejectedCount,
            bool terminal)
        {
            _revision++;
            if (_revision == 0)
            {
                throw new InvalidOperationException("Command feedback revision exhausted.");
            }

            _items[slot] = new NetworkStagedCommandFeedback(
                in outcome,
                entityOutcomeCount,
                activatedCount,
                queuedCount,
                pendingCount,
                rejectedCount,
                terminal,
                _revision);
        }

        private int Find(ulong clientBatchSequence)
        {
            for (int i = 0; i < _active.Length; i++)
            {
                if (_active[i] && _items[i].ClientBatchSequence == clientBatchSequence)
                {
                    return i;
                }
            }

            return -1;
        }

        private int AcquireSlot()
        {
            int oldestCompleted = -1;
            ulong oldestRevision = ulong.MaxValue;
            for (int i = 0; i < _active.Length; i++)
            {
                if (!_active[i])
                {
                    return i;
                }

                if (_items[i].IsTerminal && _items[i].Revision < oldestRevision)
                {
                    oldestCompleted = i;
                    oldestRevision = _items[i].Revision;
                }
            }

            if (oldestCompleted >= 0)
            {
                _active[oldestCompleted] = false;
                _count--;
                RetiredCompletedCount++;
            }

            return oldestCompleted;
        }

        private void ClearEntityResults(int slot) =>
            Array.Fill(
                _entityResults,
                UnsetEntityResult,
                checked(slot * _maxActorsPerCommandBatch),
                _maxActorsPerCommandBatch);
    }
}
