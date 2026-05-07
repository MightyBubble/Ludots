using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.Spawning
{
    public enum RuntimeEntitySpawnKind : byte
    {
        None = 0,
        UnitType = 1,
        Template = 2,
        Assembly = 3,
    }

    public struct RuntimeEntitySpawnRequest
    {
        public RuntimeEntitySpawnKind Kind;
        public Entity Source;
        public Entity TargetContext;
        public Fix64Vec2 WorldPositionCm;
        public byte HasWorldPosition;
        public float FacingAngleRad;
        public byte HasFacing;
        public int UnitTypeId;
        public string TemplateId;
        public int OnSpawnEffectTemplateId;
        public MapId MapId;
        public byte CopySourceTeam;
        public byte CopySourcePlayerOwner;
        public Entity Parent;
        public byte LinkSourceAsParent;
        public ProjectileState Projectile;
        public byte HasProjectileState;
        public int ReceiptChannelId;
        public int ReceiptId;
        public byte EmitReceipt;
    }

    public struct RuntimeEntitySpawnReceipt
    {
        public int ReceiptChannelId;
        public int ReceiptId;
        public RuntimeEntitySpawnKind Kind;
        public Entity Entity;
        public string TemplateId;
        public MapId MapId;
    }

    public sealed class RuntimeEntitySpawnQueue
    {
        private readonly RuntimeEntitySpawnRequest[] _items;
        private int _head;
        private int _tail;
        private int _count;

        public RuntimeEntitySpawnQueue(int capacity = 32768)
        {
            if (capacity < 16) capacity = 16;
            _items = new RuntimeEntitySpawnRequest[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int FreeCapacity => _items.Length - _count;

        public bool TryEnqueue(in RuntimeEntitySpawnRequest request)
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

        public int EnqueueMany(ReadOnlySpan<RuntimeEntitySpawnRequest> requests)
        {
            int writable = requests.Length < FreeCapacity ? requests.Length : FreeCapacity;
            if (writable <= 0)
            {
                return 0;
            }

            int firstCopy = Math.Min(writable, _items.Length - _tail);
            requests.Slice(0, firstCopy).CopyTo(_items.AsSpan(_tail, firstCopy));
            int remaining = writable - firstCopy;
            if (remaining > 0)
            {
                requests.Slice(firstCopy, remaining).CopyTo(_items.AsSpan(0, remaining));
            }

            _tail = (_tail + writable) % _items.Length;
            _count += writable;
            return writable;
        }

        public bool TryDequeue(out RuntimeEntitySpawnRequest request)
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

        public bool TryPeek(out RuntimeEntitySpawnRequest request)
        {
            if (_count == 0)
            {
                request = default;
                return false;
            }

            request = _items[_head];
            return true;
        }

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }

    public sealed class RuntimeEntitySpawnReceiptQueue
    {
        private readonly RuntimeEntitySpawnReceipt[] _items;
        private int _head;
        private int _tail;
        private int _count;

        public RuntimeEntitySpawnReceiptQueue(int capacity = 32768)
        {
            if (capacity < 16) capacity = 16;
            _items = new RuntimeEntitySpawnReceipt[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int FreeCapacity => _items.Length - _count;

        public bool TryEnqueue(in RuntimeEntitySpawnReceipt receipt)
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

        public bool TryDequeue(out RuntimeEntitySpawnReceipt receipt)
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

        public bool TryDequeueForChannel(int channelId, out RuntimeEntitySpawnReceipt receipt)
        {
            if (_count == 0)
            {
                receipt = default;
                return false;
            }

            for (int offset = 0; offset < _count; offset++)
            {
                int index = (_head + offset) % _items.Length;
                if (_items[index].ReceiptChannelId != channelId)
                {
                    continue;
                }

                receipt = _items[index];
                RemoveAt(index);
                return true;
            }

            receipt = default;
            return false;
        }

        public int CountForChannel(int channelId)
        {
            int count = 0;
            for (int offset = 0; offset < _count; offset++)
            {
                int index = (_head + offset) % _items.Length;
                if (_items[index].ReceiptChannelId == channelId)
                {
                    count++;
                }
            }

            return count;
        }

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        private void RemoveAt(int index)
        {
            if (_count <= 0)
            {
                return;
            }

            int current = index;
            while (current != _head)
            {
                int previous = current == 0 ? _items.Length - 1 : current - 1;
                _items[current] = _items[previous];
                current = previous;
            }

            _head = (_head + 1) % _items.Length;
            _count--;
        }
    }
}
