using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

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
        public readonly OrderAdmissionSource AdmissionSource;
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
            AdmissionSource = order.AdmissionSource;
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

    internal static class OrderAdmissionTracking
    {
        public static bool RequiresNetworkFeedback(in Order order) =>
            order.AdmissionSource == OrderAdmissionSource.Network;

        public static bool HasWaitingNetworkFeedback(in OrderBuffer buffer)
        {
            if (buffer.HasPending && RequiresNetworkFeedback(in buffer.PendingOrder.Order))
            {
                return true;
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                if (RequiresNetworkFeedback(in queued))
                {
                    return true;
                }
            }

            return false;
        }

        public static int CountRemovedWaiting(in OrderBuffer before, in OrderBuffer after)
        {
            int count = 0;
            if (before.HasPending)
            {
                Order pending = before.PendingOrder.Order;
                if (RequiresNetworkFeedback(in pending) && !Contains(in after, in pending))
                {
                    count++;
                }
            }

            for (int i = 0; i < before.QueuedCount; i++)
            {
                Order queued = before.GetQueued(i).Order;
                if (RequiresNetworkFeedback(in queued) && !Contains(in after, in queued))
                {
                    count++;
                }
            }

            return count;
        }

        public static void PublishRemovedWaiting(
            OrderAdmissionResultBuffer admissionResults,
            in OrderBuffer before,
            in OrderBuffer after,
            OrderSubmitResult result)
        {
            if (before.HasPending)
            {
                Order pending = before.PendingOrder.Order;
                if (RequiresNetworkFeedback(in pending) && !Contains(in after, in pending))
                {
                    Publish(admissionResults, in pending, result);
                }
            }

            for (int i = 0; i < before.QueuedCount; i++)
            {
                Order queued = before.GetQueued(i).Order;
                if (RequiresNetworkFeedback(in queued) && !Contains(in after, in queued))
                {
                    Publish(admissionResults, in queued, result);
                }
            }
        }

        private static bool Contains(in OrderBuffer buffer, in Order order)
        {
            if (buffer.HasActive && SameIdentity(in buffer.ActiveOrder.Order, in order))
            {
                return true;
            }

            if (buffer.HasPending && SameIdentity(in buffer.PendingOrder.Order, in order))
            {
                return true;
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                if (SameIdentity(in queued, in order))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SameIdentity(in Order left, in Order right) =>
            left.OrderId == right.OrderId &&
            left.AdmissionBatchId == right.AdmissionBatchId &&
            left.AdmissionBatchIndex == right.AdmissionBatchIndex;

        private static void Publish(
            OrderAdmissionResultBuffer admissionResults,
            in Order order,
            OrderSubmitResult result)
        {
            var outcome = new OrderAdmissionOutcome(
                in order,
                OrderAdmissionStage.EntityIntake,
                result);
            if (!admissionResults.TryWrite(in outcome))
            {
                throw new System.InvalidOperationException(
                    $"Order admission result capacity {admissionResults.Capacity} is exhausted.");
            }
        }
    }
}
