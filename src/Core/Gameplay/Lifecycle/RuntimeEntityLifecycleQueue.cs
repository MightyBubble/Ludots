using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Lifecycle
{
    public struct RuntimeEntityLifecycleRequest
    {
        public Entity Source;
        public Entity Target;
        public Entity TargetContext;
        public int EffectTemplateId;
        public EffectConfigParams ConfigParams;
        public int ReceiptChannelId;
        public int ReceiptId;
        public byte EmitReceipt;
    }

    public struct RuntimeEntityLifecycleReceipt
    {
        public int ReceiptChannelId;
        public int ReceiptId;
        public Entity Source;
        public Entity Target;
        public int EffectTemplateId;
    }

    public sealed class RuntimeEntityLifecycleQueue
    {
        private readonly RuntimeEntityLifecycleRequest[] _items;
        private int _head;
        private int _tail;
        private int _count;

        public RuntimeEntityLifecycleQueue(int capacity = 4096)
        {
            if (capacity < 16)
            {
                capacity = 16;
            }

            _items = new RuntimeEntityLifecycleRequest[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int FreeCapacity => _items.Length - _count;

        public bool TryEnqueue(in RuntimeEntityLifecycleRequest request)
        {
            if (_count >= _items.Length)
            {
                return false;
            }

            _items[_tail] = request;
            _tail = (_tail + 1) % _items.Length;
            _count++;
            return true;
        }

        public bool TryDequeue(out RuntimeEntityLifecycleRequest request)
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

    public sealed class RuntimeEntityLifecycleReceiptQueue
    {
        private readonly RuntimeEntityLifecycleReceipt[] _items;
        private int _head;
        private int _tail;
        private int _count;

        public RuntimeEntityLifecycleReceiptQueue(int capacity = 4096)
        {
            if (capacity < 16)
            {
                capacity = 16;
            }

            _items = new RuntimeEntityLifecycleReceipt[capacity];
        }

        public int Count => _count;

        public bool TryEnqueue(in RuntimeEntityLifecycleReceipt receipt)
        {
            if (_count >= _items.Length)
            {
                return false;
            }

            _items[_tail] = receipt;
            _tail = (_tail + 1) % _items.Length;
            _count++;
            return true;
        }

        public bool TryDequeue(out RuntimeEntityLifecycleReceipt receipt)
        {
            if (_count == 0)
            {
                receipt = default;
                return false;
            }

            receipt = _items[_head];
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }
    }
}
