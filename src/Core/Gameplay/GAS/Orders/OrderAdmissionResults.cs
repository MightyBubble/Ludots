using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public enum OrderAdmissionStage : byte
    {
        GlobalIntake = 0,
        EntityIntake = 1
    }

    public enum OrderSubmitResult : byte
    {
        Activated = 0,
        Queued = 1,
        Pending = 2,
        RejectedQueueFull = 3,
        RejectedByRule = 4,
        RejectedValidation = 5,
        RejectedInvalidActor = 6,
        RejectedInvalidOrderType = 7,
        RejectedBlackboardCapacity = 8,
        RejectedMissingBlackboard = 9,
        RejectedAdmissionCapacity = 10
    }

    public static class OrderSubmitResultSemantics
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAccepted(OrderSubmitResult result)
        {
            return result switch
            {
                OrderSubmitResult.Activated => true,
                OrderSubmitResult.Queued => true,
                OrderSubmitResult.Pending => true,
                OrderSubmitResult.RejectedQueueFull => false,
                OrderSubmitResult.RejectedByRule => false,
                OrderSubmitResult.RejectedValidation => false,
                OrderSubmitResult.RejectedInvalidActor => false,
                OrderSubmitResult.RejectedInvalidOrderType => false,
                OrderSubmitResult.RejectedBlackboardCapacity => false,
                OrderSubmitResult.RejectedMissingBlackboard => false,
                OrderSubmitResult.RejectedAdmissionCapacity => false,
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown order submit result."),
            };
        }

        public static OrderFailureReason ToFailureReason(OrderSubmitResult result)
        {
            return result switch
            {
                OrderSubmitResult.RejectedQueueFull => OrderFailureReason.SubmissionQueueFull,
                OrderSubmitResult.RejectedByRule => OrderFailureReason.SubmissionRuleRejected,
                OrderSubmitResult.RejectedValidation => OrderFailureReason.SubmissionValidationRejected,
                OrderSubmitResult.RejectedInvalidActor => OrderFailureReason.SubmissionInvalidActor,
                OrderSubmitResult.RejectedInvalidOrderType => OrderFailureReason.SubmissionInvalidOrderType,
                OrderSubmitResult.RejectedBlackboardCapacity => OrderFailureReason.SubmissionBlackboardCapacity,
                OrderSubmitResult.RejectedMissingBlackboard => OrderFailureReason.SubmissionMissingBlackboard,
                OrderSubmitResult.RejectedAdmissionCapacity => OrderFailureReason.SubmissionAdmissionCapacity,
                OrderSubmitResult.Activated or OrderSubmitResult.Queued or OrderSubmitResult.Pending =>
                    throw new ArgumentException($"Accepted order submit result {result} has no failure reason.", nameof(result)),
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown order submit result."),
            };
        }
    }

    public readonly struct OrderAdmissionOutcome
    {
        public readonly int OrderId;
        public readonly int OrderTypeId;
        public readonly OrderAdmissionStage Stage;
        public readonly OrderSubmitResult Result;

        public OrderAdmissionOutcome(int orderId, int orderTypeId, OrderAdmissionStage stage, OrderSubmitResult result)
        {
            OrderId = orderId;
            OrderTypeId = orderTypeId;
            Stage = stage;
            Result = result;
        }
    }

    internal readonly struct OrderAdmissionReservation
    {
        internal readonly bool IsPendingGeneration;
        internal readonly bool IsValid;

        internal OrderAdmissionReservation(bool isPendingGeneration)
        {
            IsPendingGeneration = isPendingGeneration;
            IsValid = true;
        }
    }

    /// <summary>
    /// Two fixed-capacity generations keep presentation/external intake outcomes until the
    /// next logic step can append the matching entity-intake outcome. Capacity rejections are
    /// retained in a separate fixed-capacity result area so every rejected order remains part of
    /// the same query, ordering, diagnostics, and generation lifecycle contract.
    /// </summary>
    public sealed class OrderAdmissionResultBuffer
    {
        public const string CapacityExceededError = "ORDER.ADMISSION.ERR.CapacityExceeded";
        public const string RejectionCapacityExceededError = "ORDER.ADMISSION.ERR.RejectionCapacityExceeded";
        public const string TerminalFaultedError = "ORDER.ADMISSION.ERR.TerminalFaulted";
        public const string EntityIntakeClosedError = "ORDER.ADMISSION.ERR.EntityIntakeClosed";
        public const int SubmitResultCount = (int)OrderSubmitResult.RejectedAdmissionCapacity + 1;

        private OrderAdmissionOutcome[] _currentItems;
        private OrderAdmissionOutcome[] _pendingItems;
        private OrderAdmissionOutcome[] _currentRejections;
        private OrderAdmissionOutcome[] _pendingRejections;
        private readonly long[] _observedByResult = new long[SubmitResultCount];
        private int _currentCount;
        private int _pendingCount;
        private int _currentRejectionCount;
        private int _pendingRejectionCount;
        private int _currentReserved;
        private int _pendingReserved;
        private int _nextOrderId = 1;
        private bool _logicStepActive;
        private bool _entityIntakeOpen;
        private string? _terminalFaultMessage;

        public OrderAdmissionResultBuffer(int capacity, int rejectionCapacity)
        {
            if (capacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be positive.");
            }
            if (rejectionCapacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(rejectionCapacity),
                    rejectionCapacity,
                    "rejectionCapacity must be positive.");
            }

            _currentItems = new OrderAdmissionOutcome[capacity];
            _pendingItems = new OrderAdmissionOutcome[capacity];
            _currentRejections = new OrderAdmissionOutcome[rejectionCapacity];
            _pendingRejections = new OrderAdmissionOutcome[rejectionCapacity];
        }

        public int Count => CurrentGenerationCount + PendingGenerationCount;
        public int Capacity => _currentItems.Length;
        public int RejectionCapacity => _currentRejections.Length;
        public int GenerationCapacity => checked(Capacity + RejectionCapacity);
        public int CurrentGenerationCount => _currentCount + _currentRejectionCount;
        public int PendingGenerationCount => _pendingCount + _pendingRejectionCount;
        public int ReservedCount => _currentReserved + _pendingReserved;
        public bool LogicStepActive => _logicStepActive;
        public bool EntityIntakeOpen => _entityIntakeOpen;
        public bool IsTerminalFaulted => _terminalFaultMessage != null;
        public int HighWatermark { get; private set; }
        public long OverflowCount { get; private set; }
        public uint Generation { get; private set; }

        public ref readonly OrderAdmissionOutcome this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(index));
                }

                if (index < _currentCount)
                {
                    return ref _currentItems[index];
                }

                index -= _currentCount;
                if (index < _currentRejectionCount)
                {
                    return ref _currentRejections[index];
                }

                index -= _currentRejectionCount;
                if (index < _pendingCount)
                {
                    return ref _pendingItems[index];
                }

                return ref _pendingRejections[index - _pendingCount];
            }
        }

        public bool TryWrite(in OrderAdmissionOutcome outcome)
        {
            int resultIndex = ValidateResult(outcome.Result);
            if (!TryReserve(outcome.Stage, out OrderAdmissionReservation reservation))
            {
                _observedByResult[resultIndex]++;
                return false;
            }

            try
            {
                Commit(in reservation, in outcome);
                return true;
            }
            catch
            {
                Cancel(in reservation);
                throw;
            }
        }

        public bool TryGet(int orderId, OrderAdmissionStage stage, out OrderAdmissionOutcome outcome)
        {
            for (int i = _currentCount - 1; i >= 0; i--)
            {
                if (_currentItems[i].OrderId == orderId && _currentItems[i].Stage == stage)
                {
                    outcome = _currentItems[i];
                    return true;
                }
            }

            for (int i = _pendingCount - 1; i >= 0; i--)
            {
                if (_pendingItems[i].OrderId == orderId && _pendingItems[i].Stage == stage)
                {
                    outcome = _pendingItems[i];
                    return true;
                }
            }

            for (int i = _currentRejectionCount - 1; i >= 0; i--)
            {
                if (_currentRejections[i].OrderId == orderId && _currentRejections[i].Stage == stage)
                {
                    outcome = _currentRejections[i];
                    return true;
                }
            }

            for (int i = _pendingRejectionCount - 1; i >= 0; i--)
            {
                if (_pendingRejections[i].OrderId == orderId && _pendingRejections[i].Stage == stage)
                {
                    outcome = _pendingRejections[i];
                    return true;
                }
            }

            outcome = default;
            return false;
        }

        internal void EnsureOrderId(ref Order order)
        {
            EnsureWritableForNewOrder(OrderAdmissionStage.GlobalIntake);

            if (order.OrderId < 0)
            {
                throw new System.InvalidOperationException(
                    $"ORDER.ADMISSION.ERR.InvalidOrderId: value={order.OrderId}.");
            }

            if (order.OrderId == 0)
            {
                if (_nextOrderId <= 0)
                {
                    throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.OrderIdCapacityExceeded");
                }

                order.OrderId = _nextOrderId;
            }

            if (_nextOrderId > 0 && order.OrderId >= _nextOrderId)
            {
                _nextOrderId = order.OrderId == int.MaxValue ? 0 : order.OrderId + 1;
            }
        }

        internal void EnsureWritableForNewOrders(OrderAdmissionStage stage, int count)
        {
            if (count < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(count));
            }
            if (count == 0)
            {
                return;
            }

            ThrowIfTerminalFaulted();
            bool writePending = ShouldWritePending(stage);
            int written = writePending ? _pendingCount : _currentCount;
            int reserved = writePending ? _pendingReserved : _currentReserved;
            int rejectionCount = writePending ? _pendingRejectionCount : _currentRejectionCount;
            OrderAdmissionOutcome[] items = writePending ? _pendingItems : _currentItems;
            OrderAdmissionOutcome[] rejections = writePending ? _pendingRejections : _currentRejections;
            if (written + reserved + count > items.Length &&
                rejectionCount + count > rejections.Length)
            {
                OverflowCount++;
                EnterTerminalFault(stage);
            }
        }

        internal bool CanReserve(OrderAdmissionStage stage, int count)
        {
            if (count < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(count));
            }
            if (count == 0)
            {
                return true;
            }

            ThrowIfTerminalFaulted();
            bool writePending = ShouldWritePending(stage);
            int written = writePending ? _pendingCount : _currentCount;
            int reserved = writePending ? _pendingReserved : _currentReserved;
            OrderAdmissionOutcome[] items = writePending ? _pendingItems : _currentItems;
            return written + reserved + count <= items.Length;
        }

        internal bool CanRecordCapacityFailures(OrderAdmissionStage stage, int count)
        {
            if (count < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(count));
            }
            if (count == 0)
            {
                return true;
            }

            ThrowIfTerminalFaulted();
            bool writePending = ShouldWritePending(stage);
            int rejectionCount = writePending ? _pendingRejectionCount : _currentRejectionCount;
            OrderAdmissionOutcome[] rejections = writePending
                ? _pendingRejections
                : _currentRejections;
            return rejectionCount + count <= rejections.Length;
        }

        internal void RecordCapacityFailures(ReadOnlySpan<Order> orders, OrderAdmissionStage stage)
        {
            if (orders.IsEmpty)
            {
                return;
            }

            ThrowIfTerminalFaulted();
            bool writePending = ShouldWritePending(stage);
            ref int count = ref (writePending
                ? ref _pendingRejectionCount
                : ref _currentRejectionCount);
            OrderAdmissionOutcome[] rejections = writePending
                ? _pendingRejections
                : _currentRejections;
            if (count + orders.Length > rejections.Length)
            {
                OverflowCount++;
                EnterTerminalFault(stage);
            }

            for (int i = 0; i < orders.Length; i++)
            {
                rejections[count++] = new OrderAdmissionOutcome(
                    orders[i].OrderId,
                    orders[i].OrderTypeId,
                    stage,
                    OrderSubmitResult.RejectedAdmissionCapacity);
                _observedByResult[(int)OrderSubmitResult.RejectedAdmissionCapacity]++;
            }

            if (Count > HighWatermark)
            {
                HighWatermark = Count;
            }
        }

        internal OrderAdmissionReservation Reserve(OrderAdmissionStage stage, int orderId, int orderTypeId)
        {
            if (TryReserve(stage, out OrderAdmissionReservation reservation))
            {
                return reservation;
            }

            RecordCapacityFailure(orderId, orderTypeId, stage);
            throw new System.InvalidOperationException(
                $"{CapacityExceededError}: stage={stage}, orderId={orderId}, generation={Generation}, capacity={Capacity}.");
        }

        internal void Commit(in OrderAdmissionReservation reservation, in OrderAdmissionOutcome outcome)
        {
            if (!reservation.IsValid)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.InvalidReservation");
            }

            bool expectedPending = ShouldWritePending(outcome.Stage);
            if (reservation.IsPendingGeneration != expectedPending)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.ReservationGenerationChanged");
            }

            int resultIndex = ValidateResult(outcome.Result);

            ref int reserved = ref (expectedPending ? ref _pendingReserved : ref _currentReserved);
            if (reserved <= 0)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.ReservationNotOwned");
            }

            ref int count = ref (expectedPending ? ref _pendingCount : ref _currentCount);
            OrderAdmissionOutcome[] items = expectedPending ? _pendingItems : _currentItems;
            items[count] = outcome;
            count++;
            reserved--;
            _observedByResult[resultIndex]++;
            if (Count > HighWatermark)
            {
                HighWatermark = Count;
            }
        }

        internal void Cancel(in OrderAdmissionReservation reservation)
        {
            if (!reservation.IsValid)
            {
                return;
            }

            ref int reserved = ref (reservation.IsPendingGeneration ? ref _pendingReserved : ref _currentReserved);
            if (reserved <= 0)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.ReservationNotOwned");
            }

            reserved--;
        }

        private void RecordCapacityFailure(int orderId, int orderTypeId, OrderAdmissionStage stage)
        {
            bool writePending = ShouldWritePending(stage);
            ref int count = ref (writePending
                ? ref _pendingRejectionCount
                : ref _currentRejectionCount);
            OrderAdmissionOutcome[] rejections = writePending
                ? _pendingRejections
                : _currentRejections;
            if (count >= rejections.Length)
            {
                EnterTerminalFault(stage);
            }

            rejections[count++] = new OrderAdmissionOutcome(
                orderId,
                orderTypeId,
                stage,
                OrderSubmitResult.RejectedAdmissionCapacity);
            _observedByResult[(int)OrderSubmitResult.RejectedAdmissionCapacity]++;
            if (Count > HighWatermark)
            {
                HighWatermark = Count;
            }
        }

        private bool TryReserve(OrderAdmissionStage stage, out OrderAdmissionReservation reservation)
        {
            ThrowIfTerminalFaulted();
            bool writePending = ShouldWritePending(stage);
            int count = writePending ? _pendingCount : _currentCount;
            ref int reserved = ref (writePending ? ref _pendingReserved : ref _currentReserved);
            OrderAdmissionOutcome[] items = writePending ? _pendingItems : _currentItems;
            if (count + reserved >= items.Length)
            {
                OverflowCount++;
                reservation = default;
                return false;
            }

            reserved++;
            reservation = new OrderAdmissionReservation(writePending);
            return true;
        }

        private void EnsureWritableForNewOrder(OrderAdmissionStage stage)
        {
            ThrowIfTerminalFaulted();
            bool writePending = ShouldWritePending(stage);
            int count = writePending ? _pendingCount : _currentCount;
            int reserved = writePending ? _pendingReserved : _currentReserved;
            int rejectionCount = writePending ? _pendingRejectionCount : _currentRejectionCount;
            OrderAdmissionOutcome[] items = writePending ? _pendingItems : _currentItems;
            OrderAdmissionOutcome[] rejections = writePending ? _pendingRejections : _currentRejections;
            if (count + reserved >= items.Length && rejectionCount >= rejections.Length)
            {
                OverflowCount++;
                EnterTerminalFault(stage);
            }
        }

        private void ThrowIfTerminalFaulted()
        {
            if (_terminalFaultMessage != null)
            {
                throw new System.InvalidOperationException(_terminalFaultMessage);
            }
        }

        private void EnterTerminalFault(OrderAdmissionStage stage)
        {
            _terminalFaultMessage ??=
                $"{TerminalFaultedError}: cause={RejectionCapacityExceededError}, stage={stage}, generation={Generation}, capacity={Capacity}, rejectionCapacity={RejectionCapacity}.";
            throw new System.InvalidOperationException(_terminalFaultMessage);
        }

        private bool ShouldWritePending(OrderAdmissionStage stage)
        {
            if (stage == OrderAdmissionStage.GlobalIntake)
            {
                return !_logicStepActive || !_entityIntakeOpen;
            }

            if (!_logicStepActive)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.EntityIntakeOutsideLogicStep");
            }
            if (!_entityIntakeOpen)
            {
                throw new System.InvalidOperationException(EntityIntakeClosedError);
            }

            return false;
        }

        private int ValidateResult(OrderSubmitResult result)
        {
            int resultIndex = (int)result;
            if ((uint)resultIndex >= (uint)_observedByResult.Length)
            {
                throw new System.InvalidOperationException(
                    $"ORDER.ADMISSION.ERR.UnknownSubmitResult: value={resultIndex}.");
            }

            return resultIndex;
        }

        public long GetObservedCount(OrderSubmitResult result)
        {
            int index = (int)result;
            if ((uint)index >= (uint)_observedByResult.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(result));
            }

            return _observedByResult[index];
        }

        public void BeginLogicStep()
        {
            if (_logicStepActive)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.LogicStepAlreadyActive");
            }
            if (ReservedCount != 0)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.OutstandingReservation");
            }

            OrderAdmissionOutcome[] retiredItems = _currentItems;
            int retiredCount = _currentCount;
            OrderAdmissionOutcome[] retiredRejections = _currentRejections;
            int retiredRejectionCount = _currentRejectionCount;

            _currentItems = _pendingItems;
            _pendingItems = retiredItems;
            _currentRejections = _pendingRejections;
            _pendingRejections = retiredRejections;
            _currentCount = _pendingCount;
            _pendingCount = 0;
            _currentRejectionCount = _pendingRejectionCount;
            _pendingRejectionCount = 0;
            _logicStepActive = true;
            _entityIntakeOpen = true;
            Generation++;

            // Accepted GlobalIntake must remain queryable until EntityIntake consumes the order.
            // Orders enqueued during an open intake window after OrderBufferSystem ran would otherwise
            // lose GlobalIntake at the next BeginLogicStep while still sitting in the queue.
            CarryForwardUnpairedAcceptedGlobalIntake(retiredItems, retiredCount);
            CarryForwardUnpairedAcceptedGlobalIntake(retiredRejections, retiredRejectionCount);
        }

        private void CarryForwardUnpairedAcceptedGlobalIntake(OrderAdmissionOutcome[] retired, int retiredCount)
        {
            for (int i = 0; i < retiredCount; i++)
            {
                ref readonly OrderAdmissionOutcome outcome = ref retired[i];
                if (outcome.Stage != OrderAdmissionStage.GlobalIntake ||
                    !OrderSubmitResultSemantics.IsAccepted(outcome.Result))
                {
                    continue;
                }

                if (HasStage(outcome.OrderId, OrderAdmissionStage.EntityIntake))
                {
                    continue;
                }

                if (_currentCount >= _currentItems.Length)
                {
                    OverflowCount++;
                    EnterTerminalFault(OrderAdmissionStage.GlobalIntake);
                }

                _currentItems[_currentCount++] = outcome;
                if (Count > HighWatermark)
                {
                    HighWatermark = Count;
                }
            }
        }

        private bool HasStage(int orderId, OrderAdmissionStage stage)
        {
            for (int i = _currentCount - 1; i >= 0; i--)
            {
                if (_currentItems[i].OrderId == orderId && _currentItems[i].Stage == stage)
                {
                    return true;
                }
            }

            for (int i = _currentRejectionCount - 1; i >= 0; i--)
            {
                if (_currentRejections[i].OrderId == orderId && _currentRejections[i].Stage == stage)
                {
                    return true;
                }
            }

            for (int i = _pendingCount - 1; i >= 0; i--)
            {
                if (_pendingItems[i].OrderId == orderId && _pendingItems[i].Stage == stage)
                {
                    return true;
                }
            }

            for (int i = _pendingRejectionCount - 1; i >= 0; i--)
            {
                if (_pendingRejections[i].OrderId == orderId && _pendingRejections[i].Stage == stage)
                {
                    return true;
                }
            }

            return false;
        }

        public void EndEntityIntake()
        {
            if (!_logicStepActive)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.LogicStepNotActive");
            }
            if (!_entityIntakeOpen)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.EntityIntakeAlreadyClosed");
            }
            if (ReservedCount != 0)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.OutstandingReservation");
            }

            _entityIntakeOpen = false;
        }

        public void EndLogicStep()
        {
            if (!_logicStepActive)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.LogicStepNotActive");
            }
            if (ReservedCount != 0)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.OutstandingReservation");
            }
            if (_entityIntakeOpen)
            {
                throw new System.InvalidOperationException("ORDER.ADMISSION.ERR.EntityIntakeStillOpen");
            }

            _logicStepActive = false;
        }

        /// <summary>
        /// Aborts all volatile admission work after the authoritative world is replaced.
        /// Order ids remain monotonic so callers cannot confuse post-restore submissions with
        /// outcomes observed before the restore boundary.
        /// </summary>
        public void ResetForWorldRestore()
        {
            _currentCount = 0;
            _pendingCount = 0;
            _currentRejectionCount = 0;
            _pendingRejectionCount = 0;
            _currentReserved = 0;
            _pendingReserved = 0;
            _logicStepActive = false;
            _entityIntakeOpen = false;
            _terminalFaultMessage = null;
        }
    }
}
