using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Spawning
{
    public enum RuntimeEntitySpawnKind : byte
    {
        None = 0,
        UnitType = 1,
        Template = 2,
        Assembly = 3,
    }

    public readonly record struct RuntimeEntitySpawnComponentPatch(string ComponentName, JsonNode Data);

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
        public int TeamIdOverride;
        public int PlayerOwnerIdOverride;
        public RuntimeEntitySpawnComponentPatch[] ComponentPatches;
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

        internal readonly struct WriteCheckpoint
        {
            internal readonly int Head;
            internal readonly int Tail;
            internal readonly int Count;

            internal WriteCheckpoint(int head, int tail, int count)
            {
                Head = head;
                Tail = tail;
                Count = count;
            }
        }

        internal WriteCheckpoint CaptureWriteCheckpoint()
        {
            return new WriteCheckpoint(_head, _tail, _count);
        }

        internal void RollbackWrites(in WriteCheckpoint checkpoint)
        {
            if (_head != checkpoint.Head || _count < checkpoint.Count)
            {
                throw new InvalidOperationException("SPAWN.RUNTIME.ERR.InvalidWriteRollback");
            }

            _tail = checkpoint.Tail;
            _count = checkpoint.Count;
        }

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

        public int CountForReceiptChannel(int channelId)
        {
            int count = 0;
            for (int offset = 0; offset < _count; offset++)
            {
                int index = (_head + offset) % _items.Length;
                RuntimeEntitySpawnRequest request = _items[index];
                if (request.EmitReceipt != 0 && request.ReceiptChannelId == channelId)
                {
                    count++;
                }
            }

            return count;
        }

        public int RemoveForReceiptChannel(int channelId)
        {
            if (_count == 0)
            {
                return 0;
            }

            int originalCount = _count;
            int removed = 0;
            for (int i = 0; i < originalCount; i++)
            {
                if (!TryDequeue(out RuntimeEntitySpawnRequest request))
                {
                    break;
                }

                if (request.EmitReceipt != 0 && request.ReceiptChannelId == channelId)
                {
                    removed++;
                    continue;
                }

                if (!TryEnqueue(in request))
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnQueue failed to preserve request order while removing a receipt channel.");
                }
            }

            return removed;
        }

        public int RemoveForMap(MapId mapId)
        {
            if (_count == 0 || string.IsNullOrWhiteSpace(mapId.Value))
            {
                return 0;
            }

            int originalCount = _count;
            int removed = 0;
            for (int i = 0; i < originalCount; i++)
            {
                if (!TryDequeue(out RuntimeEntitySpawnRequest request))
                {
                    break;
                }

                if (request.MapId == mapId)
                {
                    removed++;
                    continue;
                }

                if (!TryEnqueue(in request))
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnQueue failed to preserve request order while removing a map.");
                }
            }

            return removed;
        }

        public int RemoveForMapAndTemplates(MapId mapId, ReadOnlySpan<string> templateIds)
        {
            if (_count == 0 || string.IsNullOrWhiteSpace(mapId.Value) || templateIds.Length <= 0)
            {
                return 0;
            }

            int originalCount = _count;
            int removed = 0;
            for (int i = 0; i < originalCount; i++)
            {
                if (!TryDequeue(out RuntimeEntitySpawnRequest request))
                {
                    break;
                }

                if (request.MapId == mapId && ContainsTemplate(templateIds, request.TemplateId))
                {
                    removed++;
                    continue;
                }

                if (!TryEnqueue(in request))
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnQueue failed to preserve request order while removing map templates.");
                }
            }

            return removed;
        }

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        private static bool ContainsTemplate(ReadOnlySpan<string> templateIds, string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return false;
            }

            for (int i = 0; i < templateIds.Length; i++)
            {
                if (string.Equals(templateIds[i], templateId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

    public sealed class RuntimeEntitySpawnReceiptChannelRegistry
    {
        private readonly StringIntRegistry _ids;

        public RuntimeEntitySpawnReceiptChannelRegistry(int capacity = 64)
        {
            _ids = new StringIntRegistry(
                capacity: Math.Max(16, capacity),
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
        }

        public int Register(string channelKey) => _ids.Register(channelKey);

        public bool TryGetId(string channelKey, out int channelId) => _ids.TryGetId(channelKey, out channelId);

        public int GetId(string channelKey) => _ids.GetId(channelKey);

        public string GetName(int channelId) => _ids.GetName(channelId);
    }
}
