using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public enum OrderSubmitMode : byte
    {
        Immediate = 0,
        Queued = 1
    }

    public struct Order
    {
        public int OrderId;
        public int OrderTypeId;
        public int PlayerId;
        public Entity Actor;
        public Entity Target;
        public Entity TargetContext;
        public OrderArgs Args;
        public int SubmitStep;
        public OrderSubmitMode SubmitMode;
    }

    public sealed class OrderQueue
    {
        private readonly Order[] _items;
        private int _head;
        private int _tail;
        private int _count;
        private int _nextOrderId = 1;
        private readonly OrderAdmissionResultBuffer? _admissionResults;

        public OrderQueue(int capacity = 4096, OrderAdmissionResultBuffer? admissionResults = null)
        {
            if (capacity < 64) capacity = 64;
            _items = new Order[capacity];
            _admissionResults = admissionResults;
        }

        public int Count => _count;
        public int Capacity => _items.Length;

        public bool TryEnqueue(in Order order)
        {
            return IsAccepted(Submit(in order));
        }

        public bool TryEnqueueAssigned(ref Order order)
        {
            return IsAccepted(SubmitAssigned(ref order));
        }

        public OrderSubmitResult Submit(in Order order)
        {
            var value = order;
            return SubmitAssigned(ref value);
        }

        public OrderSubmitResult SubmitAssigned(ref Order order)
        {
            EnsureOrderId(ref order);
            if (!IsValidOrderTypeId(order.OrderTypeId))
            {
                WriteAdmission(in order, OrderSubmitResult.RejectedInvalidOrderType);
                return OrderSubmitResult.RejectedInvalidOrderType;
            }

            if (_count >= _items.Length)
            {
                WriteAdmission(in order, OrderSubmitResult.RejectedQueueFull);
                return OrderSubmitResult.RejectedQueueFull;
            }

            _items[_tail] = order;
            _tail = (_tail + 1) % _items.Length;
            _count++;
            WriteAdmission(in order, OrderSubmitResult.Queued);
            return OrderSubmitResult.Queued;
        }

        private static bool IsAccepted(OrderSubmitResult result) =>
            result == OrderSubmitResult.Activated ||
            result == OrderSubmitResult.Queued ||
            result == OrderSubmitResult.Pending;

        public void EnsureOrderId(ref Order order)
        {
            if (order.OrderId == 0)
            {
                order.OrderId = _nextOrderId++;
            }
        }

        private static bool IsValidOrderTypeId(int orderTypeId)
        {
            return orderTypeId > 0 && orderTypeId < OrderTypeRegistry.MaxOrderTypes;
        }

        private void WriteAdmission(in Order order, OrderSubmitResult result)
        {
            if (_admissionResults == null) return;
            var outcome = new OrderAdmissionOutcome(order.OrderId, order.OrderTypeId, OrderAdmissionStage.GlobalIntake, result);
            _admissionResults.TryWrite(in outcome);
        }

        public bool TryDequeue(out Order order)
        {
            if (_count == 0)
            {
                order = default;
                return false;
            }

            order = _items[_head];
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }

        public void Clear()
        {
            for (int i = 0, index = _head; i < _count; i++, index = (index + 1) % _items.Length)
            {
                if (_items[index].Args.Spatial.Payload.IsValid)
                {
                    throw new System.InvalidOperationException(
                        "ORDER.QUEUE.ERR.PayloadClearRequiresOwner");
                }
            }

            _head = 0;
            _tail = 0;
            _count = 0;
        }

    }
}
