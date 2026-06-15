using System;
using Arch.Core;
using Ludots.Core.Registry;

namespace Ludots.Core.EntityCollections
{
    public sealed class EntityCollectionStore
    {
        private const float LoadFactor = 0.72f;

        private readonly StringIntRegistry _keyRegistry;

        private bool[] _active;
        private Entity[] _owners;
        private int[] _ownerIds;
        private int[] _ownerWorldIds;
        private int[] _ownerVersions;
        private int[] _keyIds;
        private EntityCollectionSourceKind[] _sourceKinds;
        private EntityCollectionRoleKind[] _roles;
        private Entity[] _contextEntities;
        private Entity[] _primaryEntities;
        private uint[] _revisions;
        private ulong[] _signatures;
        private int[] _rowStarts;
        private int[] _rowCounts;
        private int[] _rowCapacities;
        private string[] _titles;
        private string[] _summaries;

        private Entity[] _rowEntities;
        private int[] _rowOrdinals;
        private int[] _rowRoleIds;
        private EntityCollectionRowFlags[] _rowFlags;

        private int[] _bucketHeads;
        private int[] _entryNext;
        private int[] _entryOwnerIds;
        private int[] _entryOwnerWorldIds;
        private int[] _entryOwnerVersions;
        private int[] _entryKeyIds;
        private int[] _entrySlots;

        private int _slotCount;
        private int _entryCount;
        private int _rowCursor;

        public EntityCollectionStore(
            StringIntRegistry keyRegistry,
            int initialCollectionCapacity = 64,
            int initialRowCapacity = 1024)
        {
            _keyRegistry = keyRegistry ?? throw new ArgumentNullException(nameof(keyRegistry));
            if (initialCollectionCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCollectionCapacity));
            }

            if (initialRowCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialRowCapacity));
            }

            _active = new bool[initialCollectionCapacity];
            _owners = new Entity[initialCollectionCapacity];
            _ownerIds = new int[initialCollectionCapacity];
            _ownerWorldIds = new int[initialCollectionCapacity];
            _ownerVersions = new int[initialCollectionCapacity];
            _keyIds = new int[initialCollectionCapacity];
            _sourceKinds = new EntityCollectionSourceKind[initialCollectionCapacity];
            _roles = new EntityCollectionRoleKind[initialCollectionCapacity];
            _contextEntities = new Entity[initialCollectionCapacity];
            _primaryEntities = new Entity[initialCollectionCapacity];
            _revisions = new uint[initialCollectionCapacity];
            _signatures = new ulong[initialCollectionCapacity];
            _rowStarts = new int[initialCollectionCapacity];
            _rowCounts = new int[initialCollectionCapacity];
            _rowCapacities = new int[initialCollectionCapacity];
            _titles = new string[initialCollectionCapacity];
            _summaries = new string[initialCollectionCapacity];

            _rowEntities = new Entity[initialRowCapacity];
            _rowOrdinals = new int[initialRowCapacity];
            _rowRoleIds = new int[initialRowCapacity];
            _rowFlags = new EntityCollectionRowFlags[initialRowCapacity];

            int bucketCount = NextPowerOfTwo(Math.Max(16, initialCollectionCapacity * 2));
            _bucketHeads = new int[bucketCount];
            Array.Fill(_bucketHeads, -1);
            _entryNext = new int[initialCollectionCapacity];
            _entryOwnerIds = new int[initialCollectionCapacity];
            _entryOwnerWorldIds = new int[initialCollectionCapacity];
            _entryOwnerVersions = new int[initialCollectionCapacity];
            _entryKeyIds = new int[initialCollectionCapacity];
            _entrySlots = new int[initialCollectionCapacity];
        }

        public StringIntRegistry KeyRegistry => _keyRegistry;
        public int CollectionCount => _slotCount;
        public int RowCapacity => _rowEntities.Length;

        public EntityCollectionHandle Replace(
            Entity owner,
            in EntityCollectionDescriptor descriptor,
            ReadOnlySpan<Entity> entities)
        {
            return Replace(owner, descriptor, entities, default, default);
        }

        public EntityCollectionHandle Replace(
            Entity owner,
            in EntityCollectionDescriptor descriptor,
            ReadOnlySpan<Entity> entities,
            ReadOnlySpan<int> rowRoleIds,
            ReadOnlySpan<EntityCollectionRowFlags> rowFlags)
        {
            if (owner == Entity.Null)
            {
                throw new ArgumentException("Entity collection owner is required.", nameof(owner));
            }

            if (string.IsNullOrWhiteSpace(descriptor.Key))
            {
                throw new ArgumentException("Entity collection descriptor key is required.", nameof(descriptor));
            }

            if (!rowRoleIds.IsEmpty && rowRoleIds.Length < entities.Length)
            {
                throw new ArgumentException("Row role id span must be empty or cover every entity row.", nameof(rowRoleIds));
            }

            if (!rowFlags.IsEmpty && rowFlags.Length < entities.Length)
            {
                throw new ArgumentException("Row flag span must be empty or cover every entity row.", nameof(rowFlags));
            }

            int keyId = _keyRegistry.Register(descriptor.Key);
            int slot = GetOrCreateSlot(owner, keyId);
            ulong nextSignature = ComputeSignature(in descriptor, entities, rowRoleIds, rowFlags);

            bool changed = !_active[slot] ||
                           _sourceKinds[slot] != descriptor.SourceKind ||
                           _roles[slot] != descriptor.Role ||
                           _contextEntities[slot] != descriptor.ContextEntity ||
                           _primaryEntities[slot] != descriptor.PrimaryEntity ||
                           !string.Equals(_titles[slot], descriptor.Title ?? string.Empty, StringComparison.Ordinal) ||
                           !string.Equals(_summaries[slot], descriptor.Summary ?? string.Empty, StringComparison.Ordinal) ||
                           _rowCounts[slot] != entities.Length ||
                           _signatures[slot] != nextSignature;

            EnsureSlotRowCapacity(slot, entities.Length);
            int rowStart = _rowStarts[slot];
            for (int i = 0; i < entities.Length; i++)
            {
                int rowIndex = rowStart + i;
                Entity entity = entities[i];
                int roleId = rowRoleIds.IsEmpty ? 0 : rowRoleIds[i];
                EntityCollectionRowFlags flags = rowFlags.IsEmpty ? EntityCollectionRowFlags.None : rowFlags[i];
                if (!changed &&
                    (_rowEntities[rowIndex] != entity ||
                     _rowOrdinals[rowIndex] != i ||
                     _rowRoleIds[rowIndex] != roleId ||
                     _rowFlags[rowIndex] != flags))
                {
                    changed = true;
                }

                _rowEntities[rowIndex] = entity;
                _rowOrdinals[rowIndex] = i;
                _rowRoleIds[rowIndex] = roleId;
                _rowFlags[rowIndex] = flags;
            }

            for (int i = entities.Length; i < _rowCounts[slot]; i++)
            {
                int rowIndex = rowStart + i;
                _rowEntities[rowIndex] = Entity.Null;
                _rowOrdinals[rowIndex] = 0;
                _rowRoleIds[rowIndex] = 0;
                _rowFlags[rowIndex] = EntityCollectionRowFlags.None;
            }

            _active[slot] = true;
            _owners[slot] = owner;
            _ownerIds[slot] = owner.Id;
            _ownerWorldIds[slot] = owner.WorldId;
            _ownerVersions[slot] = owner.Version;
            _keyIds[slot] = keyId;
            _sourceKinds[slot] = descriptor.SourceKind;
            _roles[slot] = descriptor.Role;
            _contextEntities[slot] = descriptor.ContextEntity;
            _primaryEntities[slot] = descriptor.PrimaryEntity;
            _rowCounts[slot] = entities.Length;
            _signatures[slot] = nextSignature;
            _titles[slot] = descriptor.Title ?? string.Empty;
            _summaries[slot] = descriptor.Summary ?? string.Empty;

            if (changed)
            {
                _revisions[slot]++;
                if (_revisions[slot] == 0)
                {
                    _revisions[slot] = 1;
                }
            }
            else if (_revisions[slot] == 0)
            {
                _revisions[slot] = 1;
            }

            return new EntityCollectionHandle(slot, _revisions[slot]);
        }

        public bool Remove(Entity owner, string key)
        {
            if (owner == Entity.Null ||
                string.IsNullOrWhiteSpace(key) ||
                !_keyRegistry.TryGetId(key, out int keyId) ||
                keyId <= 0 ||
                !TryFindSlot(owner, keyId, out int slot))
            {
                return false;
            }

            _active[slot] = false;
            _rowCounts[slot] = 0;
            _titles[slot] = string.Empty;
            _summaries[slot] = string.Empty;
            _revisions[slot]++;
            if (_revisions[slot] == 0)
            {
                _revisions[slot] = 1;
            }

            return true;
        }

        public bool TryGet(Entity owner, string key, out EntityCollectionHandle handle)
        {
            handle = EntityCollectionHandle.Invalid;
            if (owner == Entity.Null ||
                string.IsNullOrWhiteSpace(key) ||
                !_keyRegistry.TryGetId(key, out int keyId) ||
                keyId <= 0 ||
                !TryFindSlot(owner, keyId, out int slot) ||
                !_active[slot])
            {
                return false;
            }

            handle = new EntityCollectionHandle(slot, _revisions[slot]);
            return true;
        }

        public bool TryGetView(Entity owner, string key, out EntityCollectionView view)
        {
            view = default;
            if (!TryGet(owner, key, out EntityCollectionHandle handle))
            {
                return false;
            }

            return TryGetView(handle, out view);
        }

        public bool TryGetView(EntityCollectionHandle handle, out EntityCollectionView view)
        {
            view = default;
            if (!TryValidateSlot(handle.Slot))
            {
                return false;
            }

            int slot = handle.Slot;
            int keyId = _keyIds[slot];
            view = new EntityCollectionView(
                _owners[slot],
                keyId,
                _keyRegistry.GetName(keyId),
                _sourceKinds[slot],
                _roles[slot],
                _contextEntities[slot],
                _primaryEntities[slot],
                _revisions[slot],
                _signatures[slot],
                _rowCounts[slot],
                _titles[slot] ?? string.Empty,
                _summaries[slot] ?? string.Empty);
            return true;
        }

        public int CopyEntities(Entity owner, string key, Span<Entity> destination)
        {
            return TryGet(owner, key, out EntityCollectionHandle handle)
                ? CopyEntities(handle, 0, destination)
                : 0;
        }

        public int CopyEntities(EntityCollectionHandle handle, int startIndex, Span<Entity> destination)
        {
            if (!TryValidateSlot(handle.Slot) || destination.IsEmpty || startIndex < 0)
            {
                return 0;
            }

            int slot = handle.Slot;
            int count = _rowCounts[slot];
            if (startIndex >= count)
            {
                return 0;
            }

            int written = Math.Min(destination.Length, count - startIndex);
            for (int i = 0; i < written; i++)
            {
                destination[i] = _rowEntities[_rowStarts[slot] + startIndex + i];
            }

            return written;
        }

        public bool TryGetEntityAt(EntityCollectionHandle handle, int index, out Entity entity)
        {
            entity = default;
            if (!TryValidateSlot(handle.Slot) || index < 0 || index >= _rowCounts[handle.Slot])
            {
                return false;
            }

            entity = _rowEntities[_rowStarts[handle.Slot] + index];
            return entity != Entity.Null;
        }

        public bool TryGetRowAt(
            EntityCollectionHandle handle,
            int index,
            out Entity entity,
            out int ordinal,
            out int roleId,
            out EntityCollectionRowFlags flags)
        {
            entity = default;
            ordinal = 0;
            roleId = 0;
            flags = EntityCollectionRowFlags.None;
            if (!TryValidateSlot(handle.Slot) || index < 0 || index >= _rowCounts[handle.Slot])
            {
                return false;
            }

            int rowIndex = _rowStarts[handle.Slot] + index;
            entity = _rowEntities[rowIndex];
            ordinal = _rowOrdinals[rowIndex];
            roleId = _rowRoleIds[rowIndex];
            flags = _rowFlags[rowIndex];
            return entity != Entity.Null;
        }

        public int CopyWindow(
            EntityCollectionHandle handle,
            int startIndex,
            Span<Entity> entities,
            Span<int> ordinals,
            Span<int> roleIds,
            Span<EntityCollectionRowFlags> flags)
        {
            if (!TryValidateSlot(handle.Slot) || entities.IsEmpty || startIndex < 0)
            {
                return 0;
            }

            if ((!ordinals.IsEmpty && ordinals.Length < entities.Length) ||
                (!roleIds.IsEmpty && roleIds.Length < entities.Length) ||
                (!flags.IsEmpty && flags.Length < entities.Length))
            {
                throw new ArgumentException("Optional output spans must be empty or at least as long as the entity destination.");
            }

            int slot = handle.Slot;
            int count = _rowCounts[slot];
            if (startIndex >= count)
            {
                return 0;
            }

            int written = Math.Min(entities.Length, count - startIndex);
            int rowStart = _rowStarts[slot] + startIndex;
            for (int i = 0; i < written; i++)
            {
                int rowIndex = rowStart + i;
                entities[i] = _rowEntities[rowIndex];
                if (!ordinals.IsEmpty)
                {
                    ordinals[i] = _rowOrdinals[rowIndex];
                }

                if (!roleIds.IsEmpty)
                {
                    roleIds[i] = _rowRoleIds[rowIndex];
                }

                if (!flags.IsEmpty)
                {
                    flags[i] = _rowFlags[rowIndex];
                }
            }

            return written;
        }

        private int GetOrCreateSlot(Entity owner, int keyId)
        {
            if (TryFindSlot(owner, keyId, out int existing))
            {
                return existing;
            }

            EnsureSlotCapacity(_slotCount + 1);
            EnsureEntryCapacity(_entryCount + 1);
            if ((_entryCount + 1) > (int)(_bucketHeads.Length * LoadFactor))
            {
                Rehash(_bucketHeads.Length * 2);
            }

            int slot = _slotCount++;
            int entry = _entryCount++;
            _entryOwnerIds[entry] = owner.Id;
            _entryOwnerWorldIds[entry] = owner.WorldId;
            _entryOwnerVersions[entry] = owner.Version;
            _entryKeyIds[entry] = keyId;
            _entrySlots[entry] = slot;
            int bucket = BucketIndex(owner.Id, owner.WorldId, owner.Version, keyId, _bucketHeads.Length);
            _entryNext[entry] = _bucketHeads[bucket];
            _bucketHeads[bucket] = entry;
            return slot;
        }

        private bool TryFindSlot(Entity owner, int keyId, out int slot)
        {
            int bucket = BucketIndex(owner.Id, owner.WorldId, owner.Version, keyId, _bucketHeads.Length);
            for (int entry = _bucketHeads[bucket]; entry >= 0; entry = _entryNext[entry])
            {
                if (_entryOwnerIds[entry] == owner.Id &&
                    _entryOwnerWorldIds[entry] == owner.WorldId &&
                    _entryOwnerVersions[entry] == owner.Version &&
                    _entryKeyIds[entry] == keyId)
                {
                    slot = _entrySlots[entry];
                    return true;
                }
            }

            slot = -1;
            return false;
        }

        private bool TryValidateSlot(int slot)
        {
            return (uint)slot < (uint)_slotCount && _active[slot];
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

            Array.Resize(ref _active, next);
            Array.Resize(ref _owners, next);
            Array.Resize(ref _ownerIds, next);
            Array.Resize(ref _ownerWorldIds, next);
            Array.Resize(ref _ownerVersions, next);
            Array.Resize(ref _keyIds, next);
            Array.Resize(ref _sourceKinds, next);
            Array.Resize(ref _roles, next);
            Array.Resize(ref _contextEntities, next);
            Array.Resize(ref _primaryEntities, next);
            Array.Resize(ref _revisions, next);
            Array.Resize(ref _signatures, next);
            Array.Resize(ref _rowStarts, next);
            Array.Resize(ref _rowCounts, next);
            Array.Resize(ref _rowCapacities, next);
            Array.Resize(ref _titles, next);
            Array.Resize(ref _summaries, next);
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
            Array.Resize(ref _entryOwnerIds, next);
            Array.Resize(ref _entryOwnerWorldIds, next);
            Array.Resize(ref _entryOwnerVersions, next);
            Array.Resize(ref _entryKeyIds, next);
            Array.Resize(ref _entrySlots, next);
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

            EnsureRowCapacity(_rowCursor + capacity);
            int newStart = _rowCursor;
            if (_rowCapacities[slot] > 0 && _rowCounts[slot] > 0)
            {
                int copyCount = Math.Min(_rowCounts[slot], required);
                Array.Copy(_rowEntities, _rowStarts[slot], _rowEntities, newStart, copyCount);
                Array.Copy(_rowOrdinals, _rowStarts[slot], _rowOrdinals, newStart, copyCount);
                Array.Copy(_rowRoleIds, _rowStarts[slot], _rowRoleIds, newStart, copyCount);
                Array.Copy(_rowFlags, _rowStarts[slot], _rowFlags, newStart, copyCount);
            }

            _rowStarts[slot] = newStart;
            _rowCapacities[slot] = capacity;
            _rowCursor += capacity;
        }

        private void EnsureRowCapacity(int required)
        {
            if (required <= _rowEntities.Length)
            {
                return;
            }

            int next = _rowEntities.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _rowEntities, next);
            Array.Resize(ref _rowOrdinals, next);
            Array.Resize(ref _rowRoleIds, next);
            Array.Resize(ref _rowFlags, next);
        }

        private void Rehash(int bucketCount)
        {
            int nextBucketCount = NextPowerOfTwo(Math.Max(16, bucketCount));
            Array.Resize(ref _bucketHeads, nextBucketCount);
            Array.Fill(_bucketHeads, -1);
            for (int entry = 0; entry < _entryCount; entry++)
            {
                int bucket = BucketIndex(
                    _entryOwnerIds[entry],
                    _entryOwnerWorldIds[entry],
                    _entryOwnerVersions[entry],
                    _entryKeyIds[entry],
                    _bucketHeads.Length);
                _entryNext[entry] = _bucketHeads[bucket];
                _bucketHeads[bucket] = entry;
            }
        }

        private static ulong ComputeSignature(
            in EntityCollectionDescriptor descriptor,
            ReadOnlySpan<Entity> entities,
            ReadOnlySpan<int> rowRoleIds,
            ReadOnlySpan<EntityCollectionRowFlags> rowFlags)
        {
            ulong hash = 14695981039346656037UL;
            hash = HashCombine(hash, (uint)descriptor.SourceKind);
            hash = HashCombine(hash, (uint)descriptor.Role);
            hash = HashEntity(hash, descriptor.ContextEntity);
            hash = HashEntity(hash, descriptor.PrimaryEntity);
            hash = HashString(hash, descriptor.Title ?? string.Empty);
            hash = HashString(hash, descriptor.Summary ?? string.Empty);
            hash = HashCombine(hash, (uint)entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                hash = HashEntity(hash, entities[i]);
                hash = HashCombine(hash, (uint)i);
                hash = HashCombine(hash, rowRoleIds.IsEmpty ? 0u : (uint)rowRoleIds[i]);
                hash = HashCombine(hash, rowFlags.IsEmpty ? 0u : (uint)rowFlags[i]);
            }

            return hash == 0 ? 1UL : hash;
        }

        private static ulong HashEntity(ulong hash, Entity entity)
        {
            hash = HashCombine(hash, (uint)entity.Id);
            hash = HashCombine(hash, (uint)entity.WorldId);
            hash = HashCombine(hash, (uint)entity.Version);
            return hash;
        }

        private static ulong HashString(ulong hash, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                hash = HashCombine(hash, value[i]);
            }

            return hash;
        }

        private static ulong HashCombine(ulong hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                return hash * 1099511628211UL;
            }
        }

        private static int BucketIndex(int ownerId, int ownerWorldId, int ownerVersion, int keyId, int bucketCount)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)ownerId) * 16777619u;
                hash = (hash ^ (uint)ownerWorldId) * 16777619u;
                hash = (hash ^ (uint)ownerVersion) * 16777619u;
                hash = (hash ^ (uint)keyId) * 16777619u;
                return (int)(hash & (uint)(bucketCount - 1));
            }
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
