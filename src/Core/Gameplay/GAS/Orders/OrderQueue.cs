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

        public bool TryEnqueue(in Order order)
        {
            var value = order;
            return TryEnqueueAssigned(ref value);
        }

        public bool TryEnqueueAssigned(ref Order order)
        {
            if (_count >= _items.Length) return false;
            EnsureOrderId(ref order);

            _items[_tail] = order;
            _tail = (_tail + 1) % _items.Length;
            _count++;
            return true;
        }

        public void EnsureOrderId(ref Order order)
        {
            if (order.OrderId == 0)
            {
                order.OrderId = _nextOrderId++;
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

        public void CollectSelectionContainers(HashSet<Entity> destination)
        {
            if (destination == null || _count <= 0)
            {
                return;
            }

            for (int i = 0; i < _count; i++)
            {
                Order order = _items[(_head + i) % _items.Length];
                if (order.Args.Selection.HasContainer)
                {
                    destination.Add(order.Args.Selection.Container);
                }
            }
        }
    }
}
