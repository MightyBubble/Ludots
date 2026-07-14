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
        RejectedMissingBlackboard = 9
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
    /// next logic step can append the matching entity-intake outcome.
    /// </summary>
    public sealed class OrderAdmissionResultBuffer
    {
        public const string CapacityExceededError = "ORDER.ADMISSION.ERR.CapacityExceeded";
        public const int SubmitResultCount = (int)OrderSubmitResult.RejectedMissingBlackboard + 1;

        private OrderAdmissionOutcome[] _currentItems;
        private OrderAdmissionOutcome[] _pendingItems;
        private readonly long[] _observedByResult = new long[SubmitResultCount];
        private int _currentCount;
        private int _pendingCount;
        private int _currentReserved;
        private int _pendingReserved;
        private int _nextOrderId = 1;
        private bool _logicStepActive;

        public OrderAdmissionResultBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be positive.");
            }

            _currentItems = new OrderAdmissionOutcome[capacity];
            _pendingItems = new OrderAdmissionOutcome[capacity];
        }

        public int Count => _currentCount + _pendingCount;
        public int Capacity => _currentItems.Length;
        public int CurrentGenerationCount => _currentCount;
        public int PendingGenerationCount => _pendingCount;
        public int ReservedCount => _currentReserved + _pendingReserved;
        public bool LogicStepActive => _logicStepActive;
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

                return ref _pendingItems[index - _currentCount];
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

            outcome = default;
            return false;
        }

        internal void EnsureOrderId(ref Order order)
        {
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

        internal OrderAdmissionReservation Reserve(OrderAdmissionStage stage, int orderId)
        {
            if (TryReserve(stage, out OrderAdmissionReservation reservation))
            {
                return reservation;
            }

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

        private bool TryReserve(OrderAdmissionStage stage, out OrderAdmissionReservation reservation)
        {
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

        private bool ShouldWritePending(OrderAdmissionStage stage) =>
            stage == OrderAdmissionStage.GlobalIntake && !_logicStepActive;

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

            OrderAdmissionOutcome[] retired = _currentItems;
            _currentItems = _pendingItems;
            _pendingItems = retired;
            _currentCount = _pendingCount;
            _pendingCount = 0;
            _logicStepActive = true;
            Generation++;
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

            _logicStepActive = false;
        }
    }
}
