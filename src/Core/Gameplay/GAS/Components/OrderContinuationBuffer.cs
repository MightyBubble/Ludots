using System;
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
        Interrupted = 8,
        SubmissionQueueFull = 9,
        SubmissionRuleRejected = 10,
        SubmissionValidationRejected = 11,
        SubmissionInvalidActor = 12,
        SubmissionInvalidOrderType = 13,
        SubmissionBlackboardCapacity = 14,
        SubmissionMissingBlackboard = 15,
        SubmissionAdmissionCapacity = 16
    }

    public struct OrderContinuationEntry
    {
        public int TriggerOrderId;
        public Order Order;
    }

    public struct OrderContinuationBuffer
    {
        public const int MAX_CONTINUATIONS = 8;
        public const string ExtractionCapacityError = "ORDER.CONTINUATION.ERR.ExtractionCapacity";

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

        public int Extract(int triggerOrderId, Span<Order> destination)
        {
            if (triggerOrderId <= 0 || Count <= 0)
            {
                return 0;
            }

            int matchingCount = CountByTrigger(triggerOrderId);
            if (matchingCount == 0)
            {
                return 0;
            }
            if (destination.Length < matchingCount)
            {
                throw new InvalidOperationException(
                    $"{ExtractionCapacityError}: triggerOrderId={triggerOrderId}, matching={matchingCount}, capacity={destination.Length}.");
            }

            int written = 0;
            int dst = 0;
            for (int src = 0; src < Count; src++)
            {
                OrderContinuationEntry entry = _entries[src];
                if (entry.TriggerOrderId == triggerOrderId)
                {
                    destination[written++] = entry.Order;
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

        public readonly int CountByTrigger(int triggerOrderId)
        {
            if (triggerOrderId <= 0 || Count <= 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < Count; i++)
            {
                if (_entries[i].TriggerOrderId == triggerOrderId)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
