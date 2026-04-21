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
}
