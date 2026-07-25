using System;
using Arch.Core;

namespace Ludots.Core.Association
{
    public readonly struct EntityKeyedSoaKey : IEquatable<EntityKeyedSoaKey>
    {
        private EntityKeyedSoaKey(Entity primary, Entity secondary, int discriminator, byte keyKind)
        {
            Primary = primary;
            Secondary = secondary;
            Discriminator = discriminator;
            KeyKind = keyKind;
        }

        public Entity Primary { get; }
        public Entity Secondary { get; }
        public int Discriminator { get; }
        public byte KeyKind { get; }

        public static EntityKeyedSoaKey ForPair(Entity primary, Entity secondary)
        {
            if (primary == Entity.Null)
            {
                throw new ArgumentException("Entity association primary entity is required.", nameof(primary));
            }

            if (secondary == Entity.Null)
            {
                throw new ArgumentException("Entity association secondary entity is required.", nameof(secondary));
            }

            return new EntityKeyedSoaKey(primary, secondary, discriminator: 0, keyKind: 1);
        }

        public static EntityKeyedSoaKey ForEntityAndDiscriminator(Entity primary, int discriminator)
        {
            if (primary == Entity.Null)
            {
                throw new ArgumentException("Entity association primary entity is required.", nameof(primary));
            }

            if (discriminator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(discriminator), "Entity association discriminator must be positive.");
            }

            return new EntityKeyedSoaKey(primary, Entity.Null, discriminator, keyKind: 2);
        }

        public bool Equals(EntityKeyedSoaKey other)
        {
            return Primary == other.Primary &&
                   Secondary == other.Secondary &&
                   Discriminator == other.Discriminator &&
                   KeyKind == other.KeyKind;
        }

        public override bool Equals(object? obj)
        {
            return obj is EntityKeyedSoaKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = HashCombine(hash, KeyKind);
                hash = HashCombine(hash, Primary.Id);
                hash = HashCombine(hash, Primary.WorldId);
                hash = HashCombine(hash, Primary.Version);
                hash = HashCombine(hash, Secondary.Id);
                hash = HashCombine(hash, Secondary.WorldId);
                hash = HashCombine(hash, Secondary.Version);
                hash = HashCombine(hash, Discriminator);
                return (int)hash;
            }
        }

        public static bool operator ==(EntityKeyedSoaKey left, EntityKeyedSoaKey right) => left.Equals(right);

        public static bool operator !=(EntityKeyedSoaKey left, EntityKeyedSoaKey right) => !left.Equals(right);

        private static uint HashCombine(uint hash, int value)
        {
            return (hash ^ (uint)value) * 16777619u;
        }
    }

    public readonly struct EntityKeyedSoaRow<TPayload>
        where TPayload : struct
    {
        public EntityKeyedSoaRow(in EntityKeyedSoaKey key, in TPayload payload, uint revision, int slot)
        {
            Key = key;
            Payload = payload;
            Revision = revision;
            Slot = slot;
        }

        public readonly EntityKeyedSoaKey Key;
        public readonly TPayload Payload;
        public readonly uint Revision;
        public readonly int Slot;
    }

    public sealed class EntityKeyedSoaTable<TPayload>
        where TPayload : struct
    {
        private const float LoadFactor = 0.72f;

        private bool[] _active;
        private Entity[] _primaryEntities;
        private int[] _primaryIds;
        private int[] _primaryWorldIds;
        private int[] _primaryVersions;
        private Entity[] _secondaryEntities;
        private int[] _secondaryIds;
        private int[] _secondaryWorldIds;
        private int[] _secondaryVersions;
        private int[] _discriminators;
        private byte[] _keyKinds;
        private TPayload[] _payloads;
        private int[] _expiryTicks;
        private uint[] _revisions;

        private int[] _bucketHeads;
        private int[] _entryNext;
        private int[] _entrySlots;
        private EntityKeyedSoaKey[] _entryKeys;
        private int[] _primaryBucketHeads;
        private int[] _primaryBucketTails;
        private int[] _primarySlotNext;

        private int _slotCount;
        private int _entryCount;
        private int _activeCount;

        public EntityKeyedSoaTable(int initialCapacity = 64)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _active = new bool[initialCapacity];
            _primaryEntities = new Entity[initialCapacity];
            _primaryIds = new int[initialCapacity];
            _primaryWorldIds = new int[initialCapacity];
            _primaryVersions = new int[initialCapacity];
            _secondaryEntities = new Entity[initialCapacity];
            _secondaryIds = new int[initialCapacity];
            _secondaryWorldIds = new int[initialCapacity];
            _secondaryVersions = new int[initialCapacity];
            _discriminators = new int[initialCapacity];
            _keyKinds = new byte[initialCapacity];
            _payloads = new TPayload[initialCapacity];
            _expiryTicks = new int[initialCapacity];
            _revisions = new uint[initialCapacity];

            int bucketCount = NextPowerOfTwo(Math.Max(16, initialCapacity * 2));
            _bucketHeads = new int[bucketCount];
            Array.Fill(_bucketHeads, -1);
            _entryNext = new int[initialCapacity];
            _entrySlots = new int[initialCapacity];
            _entryKeys = new EntityKeyedSoaKey[initialCapacity];
            _primaryBucketHeads = new int[bucketCount];
            _primaryBucketTails = new int[bucketCount];
            Array.Fill(_primaryBucketHeads, -1);
            Array.Fill(_primaryBucketTails, -1);
            _primarySlotNext = new int[initialCapacity];
            Array.Fill(_primarySlotNext, -1);
        }

        public int ActiveCount => _activeCount;
        public int PhysicalSlotCount => _slotCount;
        public int SlotCapacity => _active.Length;

        public void Reserve(int requiredCapacity)
        {
            if (requiredCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredCapacity));
            }

            EnsureSlotCapacity(requiredCapacity);
            EnsureEntryCapacity(requiredCapacity);
            int requiredBuckets = NextPowerOfTwo(Math.Max(
                16,
                checked((int)Math.Ceiling(requiredCapacity / (double)LoadFactor))));
            if (_bucketHeads.Length < requiredBuckets)
            {
                Rehash(requiredBuckets);
            }

            if (_primaryBucketHeads.Length < requiredBuckets)
            {
                ResizePrimaryBuckets(requiredBuckets);
            }
        }

        public int EnsureSlot(in EntityKeyedSoaKey key)
        {
            return GetOrCreateSlot(in key);
        }

        public uint Upsert(in EntityKeyedSoaKey key, in TPayload payload, int expiryTick, bool payloadChanged, out int slot)
        {
            slot = GetOrCreateSlot(in key);
            bool wasActive = _active[slot];
            if (!wasActive)
            {
                _activeCount++;
            }

            _active[slot] = true;
            WriteKeyColumns(slot, in key);
            _payloads[slot] = payload;
            _expiryTicks[slot] = expiryTick;

            if (!wasActive || payloadChanged)
            {
                BumpRevision(slot);
            }
            else if (_revisions[slot] == 0)
            {
                _revisions[slot] = 1;
            }

            return _revisions[slot];
        }

        public uint PreviewUpsertRevision(
            in EntityKeyedSoaKey key,
            bool payloadChanged,
            out bool requiresPhysicalSlot)
        {
            if (!TryFindSlot(in key, out int slot))
            {
                requiresPhysicalSlot = true;
                return 1;
            }

            requiresPhysicalSlot = false;
            uint revision = _revisions[slot];
            if (!_active[slot] || payloadChanged)
            {
                revision++;
                return revision == 0 ? 1 : revision;
            }

            return revision == 0 ? 1 : revision;
        }

        public bool Remove(in EntityKeyedSoaKey key)
        {
            if (!TryFindSlot(in key, out int slot) || !_active[slot])
            {
                return false;
            }

            DeactivateSlot(slot);
            return true;
        }

        public int RemoveByPrimary(Entity primary)
        {
            if (primary == Entity.Null)
            {
                return 0;
            }

            int removed = 0;
            int bucket = PrimaryBucketIndex(primary, _primaryBucketHeads.Length);
            for (int slot = _primaryBucketHeads[bucket]; slot >= 0; slot = _primarySlotNext[slot])
            {
                if (_active[slot] && SlotMatchesPrimary(slot, primary))
                {
                    DeactivateSlot(slot);
                    removed++;
                }
            }

            return removed;
        }

        public int Expire(int currentTick)
        {
            int expired = 0;
            for (int slot = 0; slot < _slotCount; slot++)
            {
                if (_active[slot] &&
                    _expiryTicks[slot] > 0 &&
                    currentTick >= _expiryTicks[slot])
                {
                    DeactivateSlot(slot);
                    expired++;
                }
            }

            return expired;
        }

        public bool TryGet(
            in EntityKeyedSoaKey key,
            int currentTick,
            out TPayload payload,
            out uint revision,
            out int slot)
        {
            payload = default;
            revision = 0;
            if (!TryFindSlot(in key, out slot) || !IsActiveAt(slot, currentTick))
            {
                slot = -1;
                return false;
            }

            payload = _payloads[slot];
            revision = _revisions[slot];
            return true;
        }

        public int CopyByPrimary(Entity primary, int currentTick, Span<EntityKeyedSoaRow<TPayload>> destination)
        {
            if (destination.IsEmpty || primary == Entity.Null)
            {
                return 0;
            }

            int written = 0;
            int bucket = PrimaryBucketIndex(primary, _primaryBucketHeads.Length);
            for (int slot = _primaryBucketHeads[bucket]; slot >= 0 && written < destination.Length; slot = _primarySlotNext[slot])
            {
                if (IsActiveAt(slot, currentTick) && SlotMatchesPrimary(slot, primary))
                {
                    destination[written++] = new EntityKeyedSoaRow<TPayload>(
                        CreateKey(slot),
                        _payloads[slot],
                        _revisions[slot],
                        slot);
                }
            }

            return written;
        }

        public int CopySecondaryByPrimary(Entity primary, int currentTick, Span<Entity> destination)
        {
            if (destination.IsEmpty || primary == Entity.Null)
            {
                return 0;
            }

            int written = 0;
            int bucket = PrimaryBucketIndex(primary, _primaryBucketHeads.Length);
            for (int slot = _primaryBucketHeads[bucket]; slot >= 0 && written < destination.Length; slot = _primarySlotNext[slot])
            {
                if (IsActiveAt(slot, currentTick) && SlotMatchesPrimary(slot, primary))
                {
                    destination[written++] = _secondaryEntities[slot];
                }
            }

            return written;
        }

        public int CopyPayloadsByPrimary(
            Entity primary,
            int currentTick,
            Span<Entity> secondaries,
            Span<TPayload> payloads,
            Span<uint> revisions)
        {
            return CopyPayloadsByPrimary(primary, currentTick, startIndex: 0, secondaries, payloads, revisions);
        }

        public int CopyPayloadsByPrimary(
            Entity primary,
            int currentTick,
            int startIndex,
            Span<Entity> secondaries,
            Span<TPayload> payloads,
            Span<uint> revisions)
        {
            if (secondaries.IsEmpty || payloads.IsEmpty || primary == Entity.Null)
            {
                return 0;
            }

            if (startIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            int limit = Math.Min(secondaries.Length, payloads.Length);
            if (!revisions.IsEmpty)
            {
                limit = Math.Min(limit, revisions.Length);
            }

            int written = 0;
            int skipped = 0;
            int bucket = PrimaryBucketIndex(primary, _primaryBucketHeads.Length);
            for (int slot = _primaryBucketHeads[bucket]; slot >= 0 && written < limit; slot = _primarySlotNext[slot])
            {
                if (IsActiveAt(slot, currentTick) && SlotMatchesPrimary(slot, primary))
                {
                    if (skipped < startIndex)
                    {
                        skipped++;
                        continue;
                    }

                    secondaries[written] = _secondaryEntities[slot];
                    payloads[written] = _payloads[slot];
                    if (!revisions.IsEmpty)
                    {
                        revisions[written] = _revisions[slot];
                    }

                    written++;
                }
            }

            return written;
        }

        public bool TryGetSlot(int slot, out EntityKeyedSoaKey key, out TPayload payload, out uint revision)
        {
            key = default;
            payload = default;
            revision = 0;
            if ((uint)slot >= (uint)_slotCount || !_active[slot])
            {
                return false;
            }

            key = CreateKey(slot);
            payload = _payloads[slot];
            revision = _revisions[slot];
            return true;
        }

        public int Compact()
        {
            if (_activeCount == _slotCount)
            {
                return 0;
            }

            int write = 0;
            int removed = _slotCount - _activeCount;
            for (int read = 0; read < _slotCount; read++)
            {
                if (!_active[read])
                {
                    continue;
                }

                if (write != read)
                {
                    CopySlot(read, write);
                }

                write++;
            }

            ClearSlots(write, _slotCount);
            _slotCount = write;
            RebuildEntries();
            RebuildPrimaryIndex();
            ShrinkSlotArraysIfNeeded();
            return removed;
        }

        public int CompactPreservingCapacity()
        {
            if (_activeCount == _slotCount)
            {
                return 0;
            }

            int write = 0;
            int removed = _slotCount - _activeCount;
            for (int read = 0; read < _slotCount; read++)
            {
                if (!_active[read])
                {
                    continue;
                }

                if (write != read)
                {
                    CopySlot(read, write);
                }

                write++;
            }

            ClearSlots(write, _slotCount);
            _slotCount = write;
            RebuildEntriesPreservingCapacity();
            RebuildPrimaryIndexPreservingCapacity();
            return removed;
        }

        private int GetOrCreateSlot(in EntityKeyedSoaKey key)
        {
            if (TryFindSlot(in key, out int existing))
            {
                return existing;
            }

            EnsureSlotCapacity(_slotCount + 1);
            EnsureEntryCapacity(_entryCount + 1);
            if ((_entryCount + 1) > (int)(_bucketHeads.Length * LoadFactor))
            {
                Rehash(_bucketHeads.Length * 2);
            }

            if ((_slotCount + 1) > (int)(_primaryBucketHeads.Length * LoadFactor))
            {
                ResizePrimaryBuckets(_primaryBucketHeads.Length * 2);
            }

            int slot = _slotCount++;
            int entry = _entryCount++;
            _entryKeys[entry] = key;
            _entrySlots[entry] = slot;
            WriteKeyColumns(slot, in key);
            LinkPrimarySlot(slot, key.Primary);
            int bucket = BucketIndex(in key, _bucketHeads.Length);
            _entryNext[entry] = _bucketHeads[bucket];
            _bucketHeads[bucket] = entry;
            return slot;
        }

        private bool TryFindSlot(in EntityKeyedSoaKey key, out int slot)
        {
            int bucket = BucketIndex(in key, _bucketHeads.Length);
            for (int entry = _bucketHeads[bucket]; entry >= 0; entry = _entryNext[entry])
            {
                if (_entryKeys[entry] == key)
                {
                    slot = _entrySlots[entry];
                    return true;
                }
            }

            slot = -1;
            return false;
        }

        private void DeactivateSlot(int slot)
        {
            _active[slot] = false;
            _activeCount--;
            BumpRevision(slot);
        }

        private bool IsActiveAt(int slot, int currentTick)
        {
            return _active[slot] && (_expiryTicks[slot] <= 0 || currentTick < _expiryTicks[slot]);
        }

        private bool SlotMatchesPrimary(int slot, Entity primary)
        {
            return _primaryIds[slot] == primary.Id &&
                   _primaryWorldIds[slot] == primary.WorldId &&
                   _primaryVersions[slot] == primary.Version;
        }

        private void WriteKeyColumns(int slot, in EntityKeyedSoaKey key)
        {
            _primaryEntities[slot] = key.Primary;
            _primaryIds[slot] = key.Primary.Id;
            _primaryWorldIds[slot] = key.Primary.WorldId;
            _primaryVersions[slot] = key.Primary.Version;
            _secondaryEntities[slot] = key.Secondary;
            _secondaryIds[slot] = key.Secondary.Id;
            _secondaryWorldIds[slot] = key.Secondary.WorldId;
            _secondaryVersions[slot] = key.Secondary.Version;
            _discriminators[slot] = key.Discriminator;
            _keyKinds[slot] = key.KeyKind;
        }

        private EntityKeyedSoaKey CreateKey(int slot)
        {
            return _keyKinds[slot] == 1
                ? EntityKeyedSoaKey.ForPair(_primaryEntities[slot], _secondaryEntities[slot])
                : EntityKeyedSoaKey.ForEntityAndDiscriminator(_primaryEntities[slot], _discriminators[slot]);
        }

        private void BumpRevision(int slot)
        {
            _revisions[slot]++;
            if (_revisions[slot] == 0)
            {
                _revisions[slot] = 1;
            }
        }

        private void EnsureSlotCapacity(int required)
        {
            if (required <= _active.Length)
            {
                return;
            }

            int next = _active.Length;
            while (next < required)
            {
                next *= 2;
            }

            ResizeSlots(next);
        }

        private void EnsureEntryCapacity(int required)
        {
            if (required <= _entryNext.Length)
            {
                return;
            }

            int next = _entryNext.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _entryNext, next);
            Array.Resize(ref _entrySlots, next);
            Array.Resize(ref _entryKeys, next);
        }

        private void Rehash(int bucketCount)
        {
            int nextBucketCount = NextPowerOfTwo(Math.Max(16, bucketCount));
            Array.Resize(ref _bucketHeads, nextBucketCount);
            Array.Fill(_bucketHeads, -1);
            for (int entry = 0; entry < _entryCount; entry++)
            {
                int bucket = BucketIndex(in _entryKeys[entry], _bucketHeads.Length);
                _entryNext[entry] = _bucketHeads[bucket];
                _bucketHeads[bucket] = entry;
            }
        }

        private void LinkPrimarySlot(int slot, Entity primary)
        {
            int bucket = PrimaryBucketIndex(primary, _primaryBucketHeads.Length);
            _primarySlotNext[slot] = -1;
            int tail = _primaryBucketTails[bucket];
            if (tail >= 0)
            {
                _primarySlotNext[tail] = slot;
            }
            else
            {
                _primaryBucketHeads[bucket] = slot;
            }

            _primaryBucketTails[bucket] = slot;
        }

        private void RebuildEntries()
        {
            _entryCount = 0;
            int entryCapacity = Math.Max(1, _entryKeys.Length);
            if (_slotCount > entryCapacity)
            {
                EnsureEntryCapacity(_slotCount);
            }

            int bucketCount = NextPowerOfTwo(Math.Max(16, _slotCount * 2));
            if (_bucketHeads.Length != bucketCount)
            {
                Array.Resize(ref _bucketHeads, bucketCount);
            }

            Array.Fill(_bucketHeads, -1);
            for (int slot = 0; slot < _slotCount; slot++)
            {
                int entry = _entryCount++;
                EntityKeyedSoaKey key = CreateKey(slot);
                _entryKeys[entry] = key;
                _entrySlots[entry] = slot;
                int bucket = BucketIndex(in key, _bucketHeads.Length);
                _entryNext[entry] = _bucketHeads[bucket];
                _bucketHeads[bucket] = entry;
            }
        }

        private void RebuildEntriesPreservingCapacity()
        {
            _entryCount = 0;
            Array.Fill(_bucketHeads, -1);
            for (int slot = 0; slot < _slotCount; slot++)
            {
                int entry = _entryCount++;
                EntityKeyedSoaKey key = CreateKey(slot);
                _entryKeys[entry] = key;
                _entrySlots[entry] = slot;
                int bucket = BucketIndex(in key, _bucketHeads.Length);
                _entryNext[entry] = _bucketHeads[bucket];
                _bucketHeads[bucket] = entry;
            }
        }

        private void RebuildPrimaryIndex()
        {
            int bucketCount = NextPowerOfTwo(Math.Max(16, _slotCount * 2));
            ResizePrimaryBuckets(bucketCount);
        }

        private void RebuildPrimaryIndexPreservingCapacity()
        {
            Array.Fill(_primaryBucketHeads, -1);
            Array.Fill(_primaryBucketTails, -1);
            for (int slot = 0; slot < _slotCount; slot++)
            {
                _primarySlotNext[slot] = -1;
            }

            for (int slot = 0; slot < _slotCount; slot++)
            {
                LinkPrimarySlot(slot, _primaryEntities[slot]);
            }
        }

        private void ResizePrimaryBuckets(int bucketCount)
        {
            int nextBucketCount = NextPowerOfTwo(Math.Max(16, bucketCount));
            if (_primaryBucketHeads.Length != nextBucketCount)
            {
                Array.Resize(ref _primaryBucketHeads, nextBucketCount);
                Array.Resize(ref _primaryBucketTails, nextBucketCount);
            }

            Array.Fill(_primaryBucketHeads, -1);
            Array.Fill(_primaryBucketTails, -1);
            for (int slot = 0; slot < _slotCount; slot++)
            {
                _primarySlotNext[slot] = -1;
            }

            for (int slot = 0; slot < _slotCount; slot++)
            {
                LinkPrimarySlot(slot, _primaryEntities[slot]);
            }
        }

        private void ShrinkSlotArraysIfNeeded()
        {
            int target = NextPowerOfTwo(Math.Max(1, Math.Max(_slotCount, _activeCount)));
            target = Math.Max(4, target);
            if (target < _active.Length)
            {
                ResizeSlots(target);
            }

            int entryTarget = Math.Max(4, target);
            if (entryTarget < _entryKeys.Length)
            {
                Array.Resize(ref _entryNext, entryTarget);
                Array.Resize(ref _entrySlots, entryTarget);
                Array.Resize(ref _entryKeys, entryTarget);
            }
        }

        private void ResizeSlots(int next)
        {
            Array.Resize(ref _active, next);
            Array.Resize(ref _primaryEntities, next);
            Array.Resize(ref _primaryIds, next);
            Array.Resize(ref _primaryWorldIds, next);
            Array.Resize(ref _primaryVersions, next);
            Array.Resize(ref _secondaryEntities, next);
            Array.Resize(ref _secondaryIds, next);
            Array.Resize(ref _secondaryWorldIds, next);
            Array.Resize(ref _secondaryVersions, next);
            Array.Resize(ref _discriminators, next);
            Array.Resize(ref _keyKinds, next);
            Array.Resize(ref _payloads, next);
            Array.Resize(ref _expiryTicks, next);
            Array.Resize(ref _revisions, next);
            int oldPrimaryNextLength = _primarySlotNext.Length;
            Array.Resize(ref _primarySlotNext, next);
            for (int slot = oldPrimaryNextLength; slot < _primarySlotNext.Length; slot++)
            {
                _primarySlotNext[slot] = -1;
            }
        }

        private void CopySlot(int source, int destination)
        {
            _active[destination] = _active[source];
            _primaryEntities[destination] = _primaryEntities[source];
            _primaryIds[destination] = _primaryIds[source];
            _primaryWorldIds[destination] = _primaryWorldIds[source];
            _primaryVersions[destination] = _primaryVersions[source];
            _secondaryEntities[destination] = _secondaryEntities[source];
            _secondaryIds[destination] = _secondaryIds[source];
            _secondaryWorldIds[destination] = _secondaryWorldIds[source];
            _secondaryVersions[destination] = _secondaryVersions[source];
            _discriminators[destination] = _discriminators[source];
            _keyKinds[destination] = _keyKinds[source];
            _payloads[destination] = _payloads[source];
            _expiryTicks[destination] = _expiryTicks[source];
            _revisions[destination] = _revisions[source];
        }

        private void ClearSlots(int start, int end)
        {
            for (int slot = start; slot < end; slot++)
            {
                _active[slot] = false;
                _primaryEntities[slot] = Entity.Null;
                _primaryIds[slot] = 0;
                _primaryWorldIds[slot] = 0;
                _primaryVersions[slot] = 0;
                _secondaryEntities[slot] = Entity.Null;
                _secondaryIds[slot] = 0;
                _secondaryWorldIds[slot] = 0;
                _secondaryVersions[slot] = 0;
                _discriminators[slot] = 0;
                _keyKinds[slot] = 0;
                _payloads[slot] = default;
                _expiryTicks[slot] = 0;
                _revisions[slot] = 0;
                _primarySlotNext[slot] = -1;
            }
        }

        private static int BucketIndex(in EntityKeyedSoaKey key, int bucketCount)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = HashCombine(hash, key.KeyKind);
                hash = HashCombine(hash, key.Primary.Id);
                hash = HashCombine(hash, key.Primary.WorldId);
                hash = HashCombine(hash, key.Primary.Version);
                hash = HashCombine(hash, key.Secondary.Id);
                hash = HashCombine(hash, key.Secondary.WorldId);
                hash = HashCombine(hash, key.Secondary.Version);
                hash = HashCombine(hash, key.Discriminator);
                return (int)(hash & (uint)(bucketCount - 1));
            }
        }

        private static int PrimaryBucketIndex(Entity primary, int bucketCount)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = HashCombine(hash, primary.Id);
                hash = HashCombine(hash, primary.WorldId);
                hash = HashCombine(hash, primary.Version);
                return (int)(hash & (uint)(bucketCount - 1));
            }
        }

        private static uint HashCombine(uint hash, int value)
        {
            return (hash ^ (uint)value) * 16777619u;
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
    }
}
