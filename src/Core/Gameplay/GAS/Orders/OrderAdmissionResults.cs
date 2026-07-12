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
    /// Fixed-capacity admission outcome snapshot retained for one logic step.
    /// Producers append during the step; the SchemaUpdate reset is the sole clear owner.
    /// </summary>
    public sealed class OrderAdmissionResultBuffer
    {
        private readonly OrderAdmissionOutcome[] _items;
        private readonly long[] _observedByResult = new long[8];
        private int _head;
        private int _count;

        public OrderAdmissionResultBuffer(int capacity = 4096)
        {
            if (capacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be positive.");
            }

            _items = new OrderAdmissionOutcome[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int HighWatermark { get; private set; }
        public long OverflowCount { get; private set; }
        public uint Generation { get; private set; }

        public ref readonly OrderAdmissionOutcome this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(index));
                }

                return ref _items[(_head + index) % _items.Length];
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
            if (_count >= _items.Length)
            {
                OverflowCount++;
                return false;
            }

            int tail = (_head + _count) % _items.Length;
            _items[tail] = outcome;
            _count++;
            if (_count > HighWatermark)
            {
                HighWatermark = _count;
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

        public void Clear()
        {
            _head = 0;
            _count = 0;
            Generation++;
        }
    }
}
