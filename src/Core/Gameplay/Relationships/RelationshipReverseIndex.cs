using System;
using Arch.Core;
using Ludots.Core.Association;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>Reverse adjacency index mapping (target, relationship type) to source entities so incoming queries cost O(in-degree) instead of a world scan.</summary>
    public sealed class RelationshipReverseIndex
    {
        private readonly World _world;
        private readonly EntityKeyedSoaTable<ReverseSlotPayload> _slots;

        private int[] _rowStarts;
        private int[] _rowCounts;
        private int[] _rowCapacities;
        private Entity[] _rowSources;
        private EntityKeyedSoaRow<ReverseSlotPayload>[] _slotScratch;
        private Entity[] _dedupEntities;
        private bool[] _dedupUsed;
        private int _rowCursor;
        private uint _revision;

        public RelationshipReverseIndex(World world, int initialSlotCapacity = 64, int initialRowCapacity = 256)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (initialSlotCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialSlotCapacity));
            }

            if (initialRowCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialRowCapacity));
            }

            _slots = new EntityKeyedSoaTable<ReverseSlotPayload>(initialSlotCapacity);
            _rowStarts = new int[initialSlotCapacity];
            _rowCounts = new int[initialSlotCapacity];
            _rowCapacities = new int[initialSlotCapacity];
            _rowSources = new Entity[initialRowCapacity];
            _slotScratch = new EntityKeyedSoaRow<ReverseSlotPayload>[16];
            _dedupEntities = new Entity[64];
            _dedupUsed = new bool[64];
        }

        /// <summary>Monotonically increasing change counter bumped on every mutation, including lazy dead-row reclamation.</summary>
        public uint Revision => _revision;

        /// <summary>Records a newly created (source → target) edge of the given type. Callers must guarantee the edge did not already exist.</summary>
        public void OnLinkAdded(Entity source, Entity target, int typeId)
        {
            EntityKeyedSoaKey key = CreateKey(target, typeId);
            _slots.Upsert(key, new ReverseSlotPayload(typeId), expiryTick: 0, payloadChanged: false, out int slot);
            EnsureSlotCapacity(slot + 1);
            EnsureSlotRowCapacity(slot, _rowCounts[slot] + 1);
            _rowSources[_rowStarts[slot] + _rowCounts[slot]] = source;
            _rowCounts[slot]++;
            _revision++;
        }

        /// <summary>Records the removal of a (source → target) edge of the given type.</summary>
        public void OnLinkRemoved(Entity source, Entity target, int typeId)
        {
            if (!TryGetActiveSlot(target, typeId, out int slot))
            {
                return;
            }

            int rowStart = _rowStarts[slot];
            for (int i = 0; i < _rowCounts[slot]; i++)
            {
                if (_rowSources[rowStart + i] == source)
                {
                    RemoveRowAt(slot, i);
                    return;
                }
            }
        }

        /// <summary>Copies live incoming sources for the target into the destination span; dead sources are skipped and reclaimed in place. Pass <see cref="RelationshipTypeRegistry.AnyTypeId"/> to merge all type slots with de-duplication.</summary>
        public int CopyIncoming(Entity target, int typeId, Span<Entity> destination)
        {
            if (destination.IsEmpty || !_world.IsAlive(target))
            {
                return 0;
            }

            if (typeId == RelationshipTypeRegistry.AnyTypeId)
            {
                return CopyIncomingAnyType(target, destination);
            }

            if (!TryGetActiveSlot(target, typeId, out int slot))
            {
                return 0;
            }

            return CopySlotRows(slot, alreadyWritten: 0, destination, deduplicate: false);
        }

        /// <summary>Returns true when a live (source → target) edge row of the given type exists; dead rows encountered while scanning are reclaimed in place. O(in-degree), buffer-free.</summary>
        public bool ContainsIncoming(Entity target, Entity source, int typeId)
        {
            if (!_world.IsAlive(target) || !_world.IsAlive(source) || !TryGetActiveSlot(target, typeId, out int slot))
            {
                return false;
            }

            int index = 0;
            while (index < _rowCounts[slot])
            {
                Entity candidate = _rowSources[_rowStarts[slot] + index];
                if (!_world.IsAlive(candidate))
                {
                    RemoveRowAt(slot, index);
                    continue;
                }

                if (candidate == source)
                {
                    return true;
                }

                index++;
            }

            return false;
        }

        /// <summary>Returns the first live incoming source for (target, type); dead rows encountered while scanning are reclaimed in place. Buffer-free companion of <see cref="CopyIncoming"/>.</summary>
        public bool TryGetFirstIncoming(Entity target, int typeId, out Entity source)
        {
            source = Entity.Null;
            if (!_world.IsAlive(target) || !TryGetActiveSlot(target, typeId, out int slot))
            {
                return false;
            }

            while (_rowCounts[slot] > 0)
            {
                Entity candidate = _rowSources[_rowStarts[slot]];
                if (!_world.IsAlive(candidate))
                {
                    RemoveRowAt(slot, localIndex: 0);
                    continue;
                }

                source = candidate;
                return true;
            }

            return false;
        }

        /// <summary>Removes rows referencing dead targets or sources and rebuilds the row pool tightly. Returns the number of reclaimed rows.</summary>
        public int Compact()
        {
            int reclaimed = 0;
            int slotCount = _slots.PhysicalSlotCount;
            for (int slot = 0; slot < slotCount; slot++)
            {
                if (!_slots.TryGetSlot(slot, out EntityKeyedSoaKey key, out _, out _))
                {
                    continue;
                }

                if (!_world.IsAlive(key.Primary))
                {
                    if (_rowCounts[slot] > 0)
                    {
                        reclaimed += _rowCounts[slot];
                        _rowCounts[slot] = 0;
                        _revision++;
                    }

                    _slots.Remove(key);
                    continue;
                }

                int index = 0;
                while (index < _rowCounts[slot])
                {
                    if (!_world.IsAlive(_rowSources[_rowStarts[slot] + index]))
                    {
                        RemoveRowAt(slot, index);
                        reclaimed++;
                        continue;
                    }

                    index++;
                }
            }

            RebuildRowPool();
            return reclaimed;
        }

        private int CopyIncomingAnyType(Entity target, Span<Entity> destination)
        {
            int slotCount = CopySlotsByTarget(target);
            int totalRows = 0;
            for (int i = 0; i < slotCount; i++)
            {
                totalRows += _rowCounts[_slotScratch[i].Slot];
            }

            PrepareDedupSet(totalRows);
            int written = 0;
            for (int i = 0; i < slotCount && written < destination.Length; i++)
            {
                written = CopySlotRows(_slotScratch[i].Slot, written, destination, deduplicate: true);
            }

            return written;
        }

        private int CopySlotsByTarget(Entity target)
        {
            while (true)
            {
                int copied = _slots.CopyByPrimary(target, currentTick: 0, _slotScratch);
                if (copied < _slotScratch.Length)
                {
                    return copied;
                }

                Array.Resize(ref _slotScratch, _slotScratch.Length * 2);
            }
        }

        private int CopySlotRows(int slot, int alreadyWritten, Span<Entity> destination, bool deduplicate)
        {
            int written = alreadyWritten;
            int index = 0;
            while (index < _rowCounts[slot] && written < destination.Length)
            {
                Entity source = _rowSources[_rowStarts[slot] + index];
                if (!_world.IsAlive(source))
                {
                    RemoveRowAt(slot, index);
                    continue;
                }

                if (!deduplicate || TryAddToDedupSet(source))
                {
                    destination[written++] = source;
                }

                index++;
            }

            return written;
        }

        /// <summary>
        /// Prepares the pooled open-addressed dedup set for at most <paramref name="expectedCount"/> insertions;
        /// the AnyType merge is O(total rows) instead of a quadratic destination scan.
        /// </summary>
        private void PrepareDedupSet(int expectedCount)
        {
            int required = Math.Max(4, expectedCount * 2);
            if (_dedupEntities.Length < required)
            {
                int next = _dedupEntities.Length;
                while (next < required)
                {
                    next *= 2;
                }

                _dedupEntities = new Entity[next];
                _dedupUsed = new bool[next];
            }
            else
            {
                Array.Clear(_dedupUsed, 0, _dedupUsed.Length);
            }
        }

        private bool TryAddToDedupSet(Entity candidate)
        {
            int mask = _dedupEntities.Length - 1;
            int slot = (int)((((uint)candidate.Id * 2654435761u) ^ (uint)candidate.WorldId) & (uint)mask);
            while (_dedupUsed[slot])
            {
                if (_dedupEntities[slot] == candidate)
                {
                    return false;
                }

                slot = (slot + 1) & mask;
            }

            _dedupEntities[slot] = candidate;
            _dedupUsed[slot] = true;
            return true;
        }

        private bool TryGetActiveSlot(Entity target, int typeId, out int slot)
        {
            return _slots.TryGet(CreateKey(target, typeId), currentTick: 0, out _, out _, out slot);
        }

        private static EntityKeyedSoaKey CreateKey(Entity target, int typeId)
        {
            return EntityKeyedSoaKey.ForEntityAndDiscriminator(target, typeId + 1);
        }

        private void RemoveRowAt(int slot, int localIndex)
        {
            int rowStart = _rowStarts[slot];
            int lastLocal = _rowCounts[slot] - 1;
            _rowSources[rowStart + localIndex] = _rowSources[rowStart + lastLocal];
            _rowSources[rowStart + lastLocal] = Entity.Null;
            _rowCounts[slot] = lastLocal;
            _revision++;
        }

        private void EnsureSlotCapacity(int required)
        {
            if (required <= _rowStarts.Length)
            {
                return;
            }

            int next = _rowStarts.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _rowStarts, next);
            Array.Resize(ref _rowCounts, next);
            Array.Resize(ref _rowCapacities, next);
        }

        private void EnsureSlotRowCapacity(int slot, int required)
        {
            if (required <= _rowCapacities[slot])
            {
                return;
            }

            int capacity = Math.Max(4, _rowCapacities[slot]);
            while (capacity < required)
            {
                capacity *= 2;
            }

            EnsureRowPoolCapacity(_rowCursor + capacity);
            int newStart = _rowCursor;
            if (_rowCapacities[slot] > 0 && _rowCounts[slot] > 0)
            {
                Array.Copy(_rowSources, _rowStarts[slot], _rowSources, newStart, _rowCounts[slot]);
            }

            _rowStarts[slot] = newStart;
            _rowCapacities[slot] = capacity;
            _rowCursor += capacity;
        }

        private void EnsureRowPoolCapacity(int required)
        {
            if (required <= _rowSources.Length)
            {
                return;
            }

            int next = _rowSources.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _rowSources, next);
        }

        private void RebuildRowPool()
        {
            int slotCount = _slots.PhysicalSlotCount;
            int required = 0;
            for (int slot = 0; slot < slotCount; slot++)
            {
                required += SegmentCapacity(_rowCounts[slot]);
            }

            var nextRows = new Entity[Math.Max(4, NextPowerOfTwo(required))];
            int cursor = 0;
            for (int slot = 0; slot < slotCount; slot++)
            {
                int count = _rowCounts[slot];
                int capacity = SegmentCapacity(count);
                if (count > 0)
                {
                    Array.Copy(_rowSources, _rowStarts[slot], nextRows, cursor, count);
                }

                _rowStarts[slot] = capacity > 0 ? cursor : 0;
                _rowCapacities[slot] = capacity;
                cursor += capacity;
            }

            _rowSources = nextRows;
            _rowCursor = cursor;
        }

        private static int SegmentCapacity(int count)
        {
            return count == 0 ? 0 : NextPowerOfTwo(Math.Max(4, count));
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }

        private readonly struct ReverseSlotPayload
        {
            public ReverseSlotPayload(int typeId)
            {
                TypeId = typeId;
            }

            public readonly int TypeId;
        }
    }
}
