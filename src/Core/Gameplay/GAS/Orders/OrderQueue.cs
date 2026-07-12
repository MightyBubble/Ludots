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

        public OrderQueue(int capacity = 4096)
        {
            if (capacity < 64) capacity = 64;
            _items = new Order[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int AvailableCapacity => _items.Length - _count;

        public bool TryEnqueue(in Order order)
        {
            var value = order;
            return TryEnqueueAssigned(ref value);
        }

        public bool TryEnqueueAssigned(ref Order order)
        {
            ValidateOrderTypeId(order.OrderTypeId);
            if (_count >= _items.Length) return false;
            EnsureOrderId(ref order);

            _items[_tail] = order;
            _tail = (_tail + 1) % _items.Length;
            _count++;
            return true;
        }

        /// <summary>
        /// Atomically admits a caller-owned batch into this queue. Capacity and order type ids are
        /// validated before any queue state or order id is changed, so rejection never leaves a
        /// partially submitted command group.
        /// </summary>
        public bool TryEnqueueBatch(Span<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return true;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                ValidateOrderTypeId(orders[i].OrderTypeId);
            }

            if (orders.Length > AvailableCapacity)
            {
                return false;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                EnsureOrderId(ref orders[i]);
                _items[_tail] = orders[i];
                _tail = (_tail + 1) % _items.Length;
            }

            _count += orders.Length;
            return true;
        }

        /// <summary>
        /// Atomically admits a fan-out batch whose rows represent one logical order. The queue owns
        /// the shared id so producers cannot create ids that collide with other intake paths.
        /// </summary>
        public bool TryEnqueueSharedBatch(Span<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return true;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                ValidateOrderTypeId(orders[i].OrderTypeId);
                if (orders[i].OrderId != 0)
                {
                    throw new System.InvalidOperationException(
                        "OrderQueue shared batch requires caller order ids to be zero.");
                }
            }

            if (orders.Length > AvailableCapacity)
            {
                return false;
            }

            int sharedOrderId = _nextOrderId++;
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i].OrderId = sharedOrderId;
                _items[_tail] = orders[i];
                _tail = (_tail + 1) % _items.Length;
            }

            _count += orders.Length;
            return true;
        }

        public void EnsureOrderId(ref Order order)
        {
            if (order.OrderId == 0)
            {
                order.OrderId = _nextOrderId++;
            }
        }

        private static void ValidateOrderTypeId(int orderTypeId)
        {
            if (orderTypeId <= 0 || orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
            {
                throw new System.InvalidOperationException(
                    $"OrderQueue requires a positive order type id below {OrderTypeRegistry.MaxOrderTypes}; got {orderTypeId}.");
            }
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
            _head = 0;
            _tail = 0;
            _count = 0;
        }

    }
}
