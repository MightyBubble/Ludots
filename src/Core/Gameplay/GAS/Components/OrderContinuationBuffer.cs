using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public enum OrderTerminalState : byte
    {
        Completed = 0,
        Cancelled = 1,
        Failed = 2
    }

    public enum OrderFailureReason : byte
    {
        None = 0,
        MissingBlackboardSlot = 1,
        NegativeAbilitySlot = 2,
        AbilitySlotOutOfRange = 3,
        AbilityUnavailable = 4,
        AbilityDefinitionMissing = 5,
        ActivationBlocked = 6,
        PreconditionFailed = 7,
        Interrupted = 8
    }

    public struct OrderContinuationEntry
    {
        public int TriggerOrderId;
        public Order Order;
    }

    public struct OrderContinuationBuffer
    {
        public const int MAX_CONTINUATIONS = 8;

        [InlineArray(MAX_CONTINUATIONS)]
        private struct OrderContinuationArray
        {
            private OrderContinuationEntry _element;
        }

        public int Count;

        private OrderContinuationArray _entries;

        public readonly bool HasEntries => Count > 0;

        public bool TryAdd(int triggerOrderId, in Order order)
        {
            if (triggerOrderId <= 0 || Count >= MAX_CONTINUATIONS)
            {
                return false;
            }

            _entries[Count++] = new OrderContinuationEntry
            {
                TriggerOrderId = triggerOrderId,
                Order = order
            };
            return true;
        }

        public bool RemoveByTrigger(int triggerOrderId)
        {
            bool removed = false;
            for (int i = Count - 1; i >= 0; i--)
            {
                if (_entries[i].TriggerOrderId != triggerOrderId)
                {
                    continue;
                }

                RemoveAt(i);
                removed = true;
            }

            return removed;
        }

        public int Extract(int triggerOrderId, Span<Order> destination)
        {
            if (triggerOrderId <= 0 || destination.Length == 0 || Count <= 0)
            {
                return 0;
            }

            int written = 0;
            int dst = 0;
            for (int src = 0; src < Count; src++)
            {
                OrderContinuationEntry entry = _entries[src];
                if (entry.TriggerOrderId == triggerOrderId)
                {
                    if (written < destination.Length)
                    {
                        destination[written++] = entry.Order;
                    }
                    continue;
                }

                if (dst != src)
                {
                    _entries[dst] = entry;
                }

                dst++;
            }

            Count = dst;
            return written;
        }

        private void RemoveAt(int index)
        {
            if ((uint)index >= (uint)Count)
            {
                return;
            }

            for (int i = index; i < Count - 1; i++)
            {
                _entries[i] = _entries[i + 1];
            }

            Count--;
        }
    }
}
