using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.Morph
{
    public struct RuntimeEntityMorphRequest
    {
        public Entity Source;
        public Entity EffectContextSource;
        public Entity EffectContextTarget;
        public Entity EffectContextTargetContext;
        public EffectConfigParams EffectConfigParams;
        public string TargetTemplateId;
        public int MorphProfileId;
        public int OnMorphEffectTemplateId;
        public Fix64Vec2 PlacementOverrideCm;
        public byte HasPlacementOverride;
        public float FacingOverrideRad;
        public byte HasFacingOverride;
        public int ReceiptChannelId;
        public int ReceiptId;
        public byte EmitReceipt;
    }

    public struct RuntimeEntityMorphReceipt
    {
        public int ReceiptChannelId;
        public int ReceiptId;
        public Entity Source;
        public Entity Target;
        public string TargetTemplateId;
    }

    public sealed class RuntimeEntityMorphQueue
    {
        private readonly RuntimeEntityMorphRequest[] _items;
        private int _head;
        private int _tail;
        private int _count;

        public RuntimeEntityMorphQueue(int capacity = 4096)
        {
            if (capacity < 16)
            {
                capacity = 16;
            }

            _items = new RuntimeEntityMorphRequest[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int FreeCapacity => _items.Length - _count;

        public bool TryEnqueue(in RuntimeEntityMorphRequest request)
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

        public bool TryDequeue(out RuntimeEntityMorphRequest request)
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

    public sealed class RuntimeEntityMorphReceiptQueue
    {
        private readonly RuntimeEntityMorphReceipt[] _items;
        private int _head;
        private int _tail;
        private int _count;

        public RuntimeEntityMorphReceiptQueue(int capacity = 4096)
        {
            if (capacity < 16)
            {
                capacity = 16;
            }

            _items = new RuntimeEntityMorphReceipt[capacity];
        }

        public int Count => _count;

        public bool TryEnqueue(in RuntimeEntityMorphReceipt receipt)
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

        public bool TryDequeue(out RuntimeEntityMorphReceipt receipt)
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
