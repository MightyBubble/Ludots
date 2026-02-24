using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// Order submission mode.
    /// </summary>
    public enum OrderSubmitMode : byte
    {
        /// <summary>
        /// Immediate mode: Check TagRuleSet for conflicts, may interrupt or queue based on policy.
        /// </summary>
        Immediate = 0,
        
        /// <summary>
        /// Queued mode (Shift+): Skip conflict check, append to queue end.
        /// </summary>
        Queued = 1
    }
    
    /// <summary>
    /// An order (command) issued to an entity.
    /// Orders are "intent declarations" - after submission, they are converted to Tags + Blackboard data.
    /// </summary>
    public struct Order
    {
        public int OrderId;
        public int OrderTagId;
        public int PlayerId;
        public Entity Actor;
        public Entity Target;
        public Entity TargetContext;
        public OrderArgs Args;
        public int SubmitStep;
        
        /// <summary>
        /// Submission mode: Immediate (normal) or Queued (Shift+).
        /// </summary>
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
            if (_count >= _items.Length) return false;
            var o = order;
            if (o.OrderId == 0) o.OrderId = _nextOrderId++;
            _items[_tail] = o;
            _tail = (_tail + 1) % _items.Length;
            _count++;
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
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}
