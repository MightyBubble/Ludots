using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public enum OrderAdmissionStage : byte
    {
        GlobalIntake = 0,
        EntityIntake = 1,
        NetworkIntake = 2
    }

    public readonly struct OrderAdmissionOutcome
    {
        public readonly int OrderId;
        public readonly int OrderTypeId;
        public readonly int PlayerId;
        public readonly Entity Actor;
        public readonly int AdmissionBatchId;
        public readonly ushort AdmissionBatchSize;
        public readonly ushort AdmissionBatchIndex;
        public readonly OrderAdmissionStage Stage;
        public readonly OrderSubmitResult Result;

        public OrderAdmissionOutcome(
            in Order order,
            OrderAdmissionStage stage,
            OrderSubmitResult result)
        {
            OrderId = order.OrderId;
            OrderTypeId = order.OrderTypeId;
            PlayerId = order.PlayerId;
            Actor = order.Actor;
            AdmissionBatchId = order.AdmissionBatchId;
            AdmissionBatchSize = order.AdmissionBatchSize;
            AdmissionBatchIndex = order.AdmissionBatchIndex;
            Stage = stage;
            Result = result;
        }
    }

    public sealed class OrderAdmissionResultBuffer
    {
        private readonly OrderAdmissionOutcome[] _items;
        private int _head;
        private int _count;

        public OrderAdmissionResultBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    "Order admission result capacity must be positive.");
            }

            _items = new OrderAdmissionOutcome[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int AvailableCapacity => _items.Length - _count;
        public long OverflowCount { get; private set; }

        public bool TryWrite(in OrderAdmissionOutcome outcome)
        {
            if (_count >= _items.Length)
            {
                OverflowCount++;
                return false;
            }

            int tail = (_head + _count) % _items.Length;
            _items[tail] = outcome;
            _count++;
            return true;
        }

        public bool TryRead(out OrderAdmissionOutcome outcome)
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
}
