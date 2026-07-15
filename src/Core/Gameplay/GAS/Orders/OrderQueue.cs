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

        public OrderQueue(int capacity, OrderAdmissionResultBuffer? admissionResults = null)
        {
            if (capacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be positive.");
            }
            _items = new Order[capacity];
            _admissionResults = admissionResults;
        }

        public int Count => _count;
        public int Capacity => _items.Length;

        public bool TryEnqueue(in Order order)
        {
            return OrderSubmitResultSemantics.IsAccepted(Submit(in order));
        }

        public bool TryEnqueueAssigned(ref Order order)
        {
            return OrderSubmitResultSemantics.IsAccepted(SubmitAssigned(ref order));
        }

        public OrderSubmitResult Submit(in Order order)
        {
            var value = order;
            return SubmitAssigned(ref value);
        }

        public OrderSubmitResult SubmitAssigned(ref Order order)
        {
            EnsureOrderId(ref order);
            OrderAdmissionReservation reservation = _admissionResults == null
                ? default
                : _admissionResults.Reserve(
                    OrderAdmissionStage.GlobalIntake,
                    order.OrderId,
                    order.OrderTypeId);
            bool committed = false;
            try
            {
                OrderSubmitResult result;
                if (!IsValidOrderTypeId(order.OrderTypeId))
                {
                    result = OrderSubmitResult.RejectedInvalidOrderType;
                }
                else if (_count >= _items.Length)
                {
                    result = OrderSubmitResult.RejectedQueueFull;
                }
                else
                {
                    _items[_tail] = order;
                    _tail = (_tail + 1) % _items.Length;
                    _count++;
                    result = OrderSubmitResult.Queued;
                }

                CommitAdmission(in reservation, in order, result);
                committed = true;
                return result;
            }
            finally
            {
                if (!committed && reservation.IsValid)
                {
                    _admissionResults!.Cancel(in reservation);
                }
            }
        }

        public void EnsureOrderId(ref Order order)
        {
            if (_admissionResults != null)
            {
                _admissionResults.EnsureOrderId(ref order);
                return;
            }

            if (order.OrderId == 0)
            {
                order.OrderId = _nextOrderId++;
            }
        }

        private static bool IsValidOrderTypeId(int orderTypeId)
        {
            return orderTypeId > 0 && orderTypeId < OrderTypeRegistry.MaxOrderTypes;
        }

        private void CommitAdmission(
            in OrderAdmissionReservation reservation,
            in Order order,
            OrderSubmitResult result)
        {
            if (_admissionResults == null) return;
            var outcome = new OrderAdmissionOutcome(order.OrderId, order.OrderTypeId, OrderAdmissionStage.GlobalIntake, result);
            _admissionResults.Commit(in reservation, in outcome);
        }

        public bool TryPeek(out Order order)
        {
            if (_count == 0)
            {
                order = default;
                return false;
            }

            order = _items[_head];
            return true;
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
