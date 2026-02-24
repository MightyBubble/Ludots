using Arch.Core;
 
namespace Ludots.Core.Gameplay.GAS.Orders
{
    public unsafe struct OrderRequest
    {
        public const int MaxAllowed = 16;
 
        public int RequestId;
        public int PromptTagId;
        public int PlayerId;
 
        public Entity Actor;
        public Entity Target;
        public Entity TargetContext;
 
        public int AllowedCount;
        public fixed int AllowedOrderTagIds[MaxAllowed];
 
        public void AddAllowed(int orderTagId)
        {
            if (AllowedCount >= MaxAllowed) return;
            AllowedOrderTagIds[AllowedCount++] = orderTagId;
        }
 
        public int GetAllowed(int index)
        {
            if ((uint)index >= (uint)AllowedCount) return 0;
            fixed (int* p = AllowedOrderTagIds) return p[index];
        }
    }
 
    public sealed class OrderRequestQueue
    {
        private readonly OrderRequest[] _items;
        private int _head;
        private int _tail;
        private int _count;
        private int _nextRequestId = 1;
 
        public OrderRequestQueue(int capacity = 256)
        {
            if (capacity < 16) capacity = 16;
            _items = new OrderRequest[capacity];
        }
 
        public int Count => _count;
        public int Capacity => _items.Length;
 
        public bool TryEnqueue(in OrderRequest request)
        {
            if (_count >= _items.Length) return false;
            var r = request;
            if (r.RequestId == 0) r.RequestId = _nextRequestId++;
            _items[_tail] = r;
            _tail = (_tail + 1) % _items.Length;
            _count++;
            return true;
        }
 
        public bool TryDequeue(out OrderRequest request)
        {
            if (_count == 0)
            {
                request = default;
                return false;
            }
 
            request = _items[_head];
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
