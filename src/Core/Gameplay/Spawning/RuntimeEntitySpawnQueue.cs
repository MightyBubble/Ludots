using Arch.Core;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.Spawning
{
    public enum RuntimeEntitySpawnKind : byte
    {
        None = 0,
        UnitType = 1,
        Template = 2,
    }

    public struct RuntimeEntitySpawnRequest
    {
        public RuntimeEntitySpawnKind Kind;
        public Entity Source;
        public Entity TargetContext;
        public Fix64Vec2 WorldPositionCm;
        public int UnitTypeId;
        public string TemplateId;
        public int OnSpawnEffectTemplateId;
        public MapId MapId;
        public byte CopySourceTeam;
    }

    public sealed class RuntimeEntitySpawnQueue
    {
        private readonly RuntimeEntitySpawnRequest[] _items;
        private int _head;
        private int _tail;
        private int _count;

        public RuntimeEntitySpawnQueue(int capacity = 1024)
        {
            if (capacity < 16) capacity = 16;
            _items = new RuntimeEntitySpawnRequest[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;

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

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}
