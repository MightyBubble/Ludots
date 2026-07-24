using System;
using System.Runtime.CompilerServices;
using Arch.Core;

namespace Ludots.Core.Networking.Replication
{
    public sealed class NetworkEntityTable
    {
        private const byte EmptyBucket = 0;
        private const byte OccupiedBucket = 1;
        private const byte RemovedBucket = 2;

        private readonly bool[] _active;
        private readonly Entity[] _entities;
        private readonly int[] _entityIds;
        private readonly int[] _entityWorldIds;
        private readonly int[] _entityVersions;
        private readonly uint[] _generations;
        private readonly int[] _freeSlots;

        private readonly byte[] _entityBucketStates;
        private readonly int[] _entityBucketIds;
        private readonly int[] _entityBucketWorldIds;
        private readonly int[] _entityBucketVersions;
        private readonly int[] _entityBucketSlots;

        private int _freeCount;
        private int _count;

        public NetworkEntityTable(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            int minimumBucketCount = checked(capacity * 2);
            int bucketCount = NextPowerOfTwo(minimumBucketCount);

            _active = new bool[capacity];
            _entities = new Entity[capacity];
            _entityIds = new int[capacity];
            _entityWorldIds = new int[capacity];
            _entityVersions = new int[capacity];
            _generations = new uint[capacity];
            _freeSlots = new int[capacity];

            _entityBucketStates = new byte[bucketCount];
            _entityBucketIds = new int[bucketCount];
            _entityBucketWorldIds = new int[bucketCount];
            _entityBucketVersions = new int[bucketCount];
            _entityBucketSlots = new int[bucketCount];

            for (int slot = 0; slot < capacity; slot++)
            {
                _generations[slot] = 1;
                _freeSlots[capacity - slot - 1] = slot;
            }

            _freeCount = capacity;
        }

        public int Capacity => _active.Length;

        public int Count => _count;

        public int AvailableCapacity => _freeCount;

        public bool TryAllocate(Entity entity, out NetworkEntityHandle handle)
        {
            handle = default;
            if (entity == Entity.Null ||
                TryFindEntityBucket(entity.Id, entity.WorldId, entity.Version, out _) ||
                _freeCount == 0)
            {
                return false;
            }

            int slot = _freeSlots[--_freeCount];
            if (!TryFindInsertionBucket(entity.Id, entity.WorldId, entity.Version, out int bucket))
            {
                _freeSlots[_freeCount++] = slot;
                return false;
            }

            _entities[slot] = entity;
            _entityIds[slot] = entity.Id;
            _entityWorldIds[slot] = entity.WorldId;
            _entityVersions[slot] = entity.Version;
            _active[slot] = true;

            _entityBucketIds[bucket] = entity.Id;
            _entityBucketWorldIds[bucket] = entity.WorldId;
            _entityBucketVersions[bucket] = entity.Version;
            _entityBucketSlots[bucket] = slot;
            _entityBucketStates[bucket] = OccupiedBucket;

            _count++;
            handle = new NetworkEntityHandle(slot, _generations[slot]);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryResolve(NetworkEntityHandle handle, out Entity entity)
        {
            if (!handle.IsValid ||
                (uint)handle.Slot >= (uint)_active.Length ||
                !_active[handle.Slot] ||
                _generations[handle.Slot] != handle.Generation)
            {
                entity = Entity.Null;
                return false;
            }

            entity = _entities[handle.Slot];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryResolve(Entity entity, out NetworkEntityHandle handle)
        {
            if (entity == Entity.Null ||
                !TryFindEntityBucket(entity.Id, entity.WorldId, entity.Version, out int bucket))
            {
                handle = default;
                return false;
            }

            int slot = _entityBucketSlots[bucket];
            if (!_active[slot])
            {
                handle = default;
                return false;
            }

            handle = new NetworkEntityHandle(slot, _generations[slot]);
            return true;
        }

        public bool TryRelease(NetworkEntityHandle handle)
        {
            if (!handle.IsValid ||
                (uint)handle.Slot >= (uint)_active.Length ||
                !_active[handle.Slot] ||
                _generations[handle.Slot] != handle.Generation)
            {
                return false;
            }

            int slot = handle.Slot;
            if (!TryFindEntityBucket(
                    _entityIds[slot],
                    _entityWorldIds[slot],
                    _entityVersions[slot],
                    out int bucket))
            {
                throw new InvalidOperationException("Network entity reverse index is inconsistent with the active slot table.");
            }

            _entityBucketStates[bucket] = RemovedBucket;
            _entityBucketIds[bucket] = 0;
            _entityBucketWorldIds[bucket] = 0;
            _entityBucketVersions[bucket] = 0;
            _entityBucketSlots[bucket] = 0;

            _active[slot] = false;
            _entities[slot] = Entity.Null;
            _entityIds[slot] = 0;
            _entityWorldIds[slot] = 0;
            _entityVersions[slot] = 0;
            _count--;

            uint nextGeneration = _generations[slot] + 1;
            if (nextGeneration == 0)
            {
                // Exhausted slots stay retired so generation can never wrap to an old valid handle.
                return true;
            }

            _generations[slot] = nextGeneration;
            _freeSlots[_freeCount++] = slot;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryFindEntityBucket(int id, int worldId, int version, out int bucket)
        {
            int mask = _entityBucketStates.Length - 1;
            int candidate = (int)(HashEntity(id, worldId, version) & (uint)mask);
            for (int probe = 0; probe < _entityBucketStates.Length; probe++)
            {
                byte state = _entityBucketStates[candidate];
                if (state == EmptyBucket)
                {
                    bucket = -1;
                    return false;
                }

                if (state == OccupiedBucket &&
                    _entityBucketIds[candidate] == id &&
                    _entityBucketWorldIds[candidate] == worldId &&
                    _entityBucketVersions[candidate] == version)
                {
                    bucket = candidate;
                    return true;
                }

                candidate = (candidate + 1) & mask;
            }

            bucket = -1;
            return false;
        }

        private bool TryFindInsertionBucket(int id, int worldId, int version, out int bucket)
        {
            int mask = _entityBucketStates.Length - 1;
            int candidate = (int)(HashEntity(id, worldId, version) & (uint)mask);
            int firstRemoved = -1;
            for (int probe = 0; probe < _entityBucketStates.Length; probe++)
            {
                byte state = _entityBucketStates[candidate];
                if (state == EmptyBucket)
                {
                    bucket = firstRemoved >= 0 ? firstRemoved : candidate;
                    return true;
                }

                if (state == RemovedBucket && firstRemoved < 0)
                {
                    firstRemoved = candidate;
                }

                candidate = (candidate + 1) & mask;
            }

            bucket = firstRemoved;
            return firstRemoved >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashEntity(int id, int worldId, int version)
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)id) * 16777619u;
            hash = (hash ^ (uint)worldId) * 16777619u;
            hash = (hash ^ (uint)version) * 16777619u;
            return hash;
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value > 1 << 30)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
    }
}
