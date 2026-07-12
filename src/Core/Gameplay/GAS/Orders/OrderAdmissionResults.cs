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
        RejectedInvalidOrderType = 7
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

    /// <summary>
    /// Two fixed-capacity generations keep presentation/external intake outcomes until the
    /// next logic step can append the matching entity-intake outcome.
    /// </summary>
    public sealed class OrderAdmissionResultBuffer
    {
        private OrderAdmissionOutcome[] _currentItems;
        private OrderAdmissionOutcome[] _pendingItems;
        private readonly long[] _observedByResult = new long[8];
        private int _currentCount;
        private int _pendingCount;
        private bool _logicStepActive;

        public OrderAdmissionResultBuffer(int capacity = 4096)
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
            int resultIndex = (int)outcome.Result;
            if ((uint)resultIndex >= (uint)_observedByResult.Length)
            {
                throw new System.InvalidOperationException(
                    $"ORDER.ADMISSION.ERR.UnknownSubmitResult: value={resultIndex}.");
            }

            _observedByResult[resultIndex]++;
            bool writePending = outcome.Stage == OrderAdmissionStage.GlobalIntake && !_logicStepActive;
            ref int count = ref (writePending ? ref _pendingCount : ref _currentCount);
            OrderAdmissionOutcome[] items = writePending ? _pendingItems : _currentItems;
            if (count >= items.Length)
            {
                OverflowCount++;
                return false;
            }

            items[count++] = outcome;
            if (Count > HighWatermark)
            {
                HighWatermark = Count;
            }
            return true;
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

            _logicStepActive = false;
        }
    }
}
