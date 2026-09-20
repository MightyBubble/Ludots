using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.EntityCollections;
using Ludots.Core.Registry;

namespace Ludots.Core.TypedCollections
{
    public sealed class IntIdCollectionStore
    {
        private readonly StringIntRegistry _keyRegistry;
        private readonly EntityKeyedSoaTable<IntIdCollectionPayload> _collections;

        private bool[] _active;
        private Entity[] _owners;
        private EntityCollectionSourceKind[] _sourceKinds;
        private EntityCollectionRoleKind[] _roles;
        private uint[] _revisions;
        private ulong[] _signatures;
        private int[] _rowStarts;
        private int[] _rowCounts;
        private int[] _rowCapacities;
        private string[] _titles;
        private string[] _summaries;

        private int[] _rowIds;

        private int _rowCursor;

        public IntIdCollectionStore(
            StringIntRegistry keyRegistry,
            int initialCollectionCapacity = 64,
            int initialRowCapacity = 1024)
        {
            _keyRegistry = keyRegistry ?? throw new ArgumentNullException(nameof(keyRegistry));
            _collections = new EntityKeyedSoaTable<IntIdCollectionPayload>(initialCollectionCapacity);
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
            _sourceKinds = new EntityCollectionSourceKind[initialCollectionCapacity];
            _roles = new EntityCollectionRoleKind[initialCollectionCapacity];
            _revisions = new uint[initialCollectionCapacity];
            _signatures = new ulong[initialCollectionCapacity];
            _rowStarts = new int[initialCollectionCapacity];
            _rowCounts = new int[initialCollectionCapacity];
            _rowCapacities = new int[initialCollectionCapacity];
            _titles = new string[initialCollectionCapacity];
            _summaries = new string[initialCollectionCapacity];

            _rowIds = new int[initialRowCapacity];
        }

        public StringIntRegistry KeyRegistry => _keyRegistry;
        public int CollectionCount => _collections.ActiveCount;
        public int RowCapacity => _rowIds.Length;

        public int CopyActiveHandles(Span<IntIdCollectionHandle> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            for (int slot = 0; slot < _active.Length && written < destination.Length; slot++)
            {
                if (!_active[slot])
                {
                    continue;
                }

                destination[written++] = new IntIdCollectionHandle(slot, _revisions[slot]);
            }

            return written;
        }

        /// <summary>Registers the descriptor key through the key registry on every call; load/init-time entry — per-frame writers resolve the key id once and use the keyId overloads.</summary>
        public IntIdCollectionHandle Replace(
            Entity owner,
            in IntIdCollectionDescriptor descriptor,
            ReadOnlySpan<int> ids)
        {
            if (owner == Entity.Null)
            {
                throw new ArgumentException("Int-id collection owner is required.", nameof(owner));
            }

            if (string.IsNullOrWhiteSpace(descriptor.Key))
            {
                throw new ArgumentException("Int-id collection descriptor key is required.", nameof(descriptor));
            }

            return ReplaceCore(owner, _keyRegistry.Register(descriptor.Key), in descriptor, ids);
        }

        /// <summary>Per-frame entry: <paramref name="keyId"/> must be registered in <see cref="KeyRegistry"/>; the descriptor key is authoring metadata and is not looked up.</summary>
        public IntIdCollectionHandle Replace(
            Entity owner,
            int keyId,
            in IntIdCollectionDescriptor descriptor,
            ReadOnlySpan<int> ids)
        {
            if (owner == Entity.Null)
            {
                throw new ArgumentException("Int-id collection owner is required.", nameof(owner));
            }

            if (keyId <= 0 || string.IsNullOrEmpty(_keyRegistry.GetName(keyId)))
            {
                throw new ArgumentOutOfRangeException(nameof(keyId), keyId, "Int-id collection key id must be registered in the key registry.");
            }

            return ReplaceCore(owner, keyId, in descriptor, ids);
        }

        private IntIdCollectionHandle ReplaceCore(
            Entity owner,
            int keyId,
            in IntIdCollectionDescriptor descriptor,
            ReadOnlySpan<int> ids)
        {
            EntityKeyedSoaKey tableKey = EntityKeyedSoaKey.ForEntityAndDiscriminator(owner, keyId);
            int slot = _collections.EnsureSlot(tableKey);
            EnsureSlotCapacity(slot + 1);
            ulong nextSignature = ComputeSignature(in descriptor, ids);

            bool changed = !_active[slot] ||
                           _sourceKinds[slot] != descriptor.SourceKind ||
                           _roles[slot] != descriptor.Role ||
                           !string.Equals(_titles[slot], descriptor.Title ?? string.Empty, StringComparison.Ordinal) ||
                           !string.Equals(_summaries[slot], descriptor.Summary ?? string.Empty, StringComparison.Ordinal) ||
                           _rowCounts[slot] != ids.Length ||
                           _signatures[slot] != nextSignature;

            EnsureSlotRowCapacity(slot, ids.Length);
            int rowStart = _rowStarts[slot];
            for (int i = 0; i < ids.Length; i++)
            {
                int rowIndex = rowStart + i;
                int id = ids[i];
                if (!changed && _rowIds[rowIndex] != id)
                {
                    changed = true;
                }

                _rowIds[rowIndex] = id;
            }

            for (int i = ids.Length; i < _rowCounts[slot]; i++)
            {
                _rowIds[rowStart + i] = 0;
            }

            _active[slot] = true;
            _owners[slot] = owner;
            _sourceKinds[slot] = descriptor.SourceKind;
            _roles[slot] = descriptor.Role;
            _rowCounts[slot] = ids.Length;
            _signatures[slot] = nextSignature;
            _titles[slot] = descriptor.Title ?? string.Empty;
            _summaries[slot] = descriptor.Summary ?? string.Empty;

            if (changed)
            {
                _revisions[slot] = _collections.Upsert(
                    tableKey,
                    new IntIdCollectionPayload(keyId),
                    expiryTick: 0,
                    payloadChanged: true,
                    out _);
            }
            else if (_revisions[slot] == 0)
            {
                _revisions[slot] = _collections.Upsert(
                    tableKey,
                    new IntIdCollectionPayload(keyId),
                    expiryTick: 0,
                    payloadChanged: false,
                    out _);
            }

            return new IntIdCollectionHandle(slot, _revisions[slot]);
        }

        /// <summary>String-key removal; resolve the key id once on per-frame paths and call the keyId overload.</summary>
        public bool Remove(Entity owner, string key)
        {
            return !string.IsNullOrWhiteSpace(key) &&
                   _keyRegistry.TryGetId(key, out int keyId) &&
                   Remove(owner, keyId);
        }

        public bool Remove(Entity owner, int keyId)
        {
            if (owner == Entity.Null ||
                keyId <= 0 ||
                !TryFindSlot(owner, keyId, out int slot))
            {
                return false;
            }

            _active[slot] = false;
            _owners[slot] = Entity.Null;
            _rowCounts[slot] = 0;
            _titles[slot] = string.Empty;
            _summaries[slot] = string.Empty;
            _collections.Remove(EntityKeyedSoaKey.ForEntityAndDiscriminator(owner, keyId));
            _revisions[slot] = 1;

            return true;
        }

        /// <summary>String-key lookup; resolve the key id once on per-frame paths and call the keyId overload.</summary>
        public bool TryGet(Entity owner, string key, out IntIdCollectionHandle handle)
        {
            handle = IntIdCollectionHandle.Invalid;
            return !string.IsNullOrWhiteSpace(key) &&
                   _keyRegistry.TryGetId(key, out int keyId) &&
                   TryGet(owner, keyId, out handle);
        }

        public bool TryGet(Entity owner, int keyId, out IntIdCollectionHandle handle)
        {
            handle = IntIdCollectionHandle.Invalid;
            if (owner == Entity.Null ||
                keyId <= 0 ||
                !TryFindSlot(owner, keyId, out int slot) ||
                !_active[slot])
            {
                return false;
            }

            handle = new IntIdCollectionHandle(slot, _revisions[slot]);
            return true;
        }

        /// <summary>String-key view lookup; resolve the key id once on per-frame paths and call the handle overload.</summary>
        public bool TryGetView(Entity owner, string key, out IntIdCollectionView view)
        {
            view = default;
            if (!TryGet(owner, key, out IntIdCollectionHandle handle))
            {
                return false;
            }

            return TryGetView(handle, out view);
        }

        public bool TryGetView(IntIdCollectionHandle handle, out IntIdCollectionView view)
        {
            view = default;
            if (!TryValidateSlot(handle.Slot))
            {
                return false;
            }

            int slot = handle.Slot;
            if (!_collections.TryGetSlot(slot, out _, out IntIdCollectionPayload payload, out _))
            {
                return false;
            }

            int keyId = payload.KeyId;
            view = new IntIdCollectionView(
                _owners[slot],
                keyId,
                _keyRegistry.GetName(keyId),
                _sourceKinds[slot],
                _roles[slot],
                _revisions[slot],
                _signatures[slot],
                _rowCounts[slot],
                _titles[slot] ?? string.Empty,
                _summaries[slot] ?? string.Empty);
            return true;
        }

        public bool TryGetIdAt(IntIdCollectionHandle handle, int index, out int id)
        {
            id = 0;
            if (!TryValidateSlot(handle.Slot) || index < 0 || index >= _rowCounts[handle.Slot])
            {
                return false;
            }

            id = _rowIds[_rowStarts[handle.Slot] + index];
            return true;
        }

        /// <summary>String-key id copy; resolve the key id once on per-frame paths and call the keyId overload.</summary>
        public int CopyIds(Entity owner, string key, Span<int> destination)
        {
            return TryGet(owner, key, out IntIdCollectionHandle handle)
                ? CopyIds(handle, 0, destination)
                : 0;
        }

        public int CopyIds(Entity owner, int keyId, Span<int> destination)
        {
            return TryGet(owner, keyId, out IntIdCollectionHandle handle)
                ? CopyIds(handle, 0, destination)
                : 0;
        }

        public int CopyIds(IntIdCollectionHandle handle, int startIndex, Span<int> destination)
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
                destination[i] = _rowIds[_rowStarts[slot] + startIndex + i];
            }

            return written;
        }

        private bool TryFindSlot(Entity owner, int keyId, out int slot)
        {
            return _collections.TryGet(
                EntityKeyedSoaKey.ForEntityAndDiscriminator(owner, keyId),
                currentTick: 0,
                out _,
                out _,
                out slot);
        }

        private bool TryValidateSlot(int slot)
        {
            return (uint)slot < (uint)_active.Length && _active[slot];
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
            Array.Resize(ref _sourceKinds, next);
            Array.Resize(ref _roles, next);
            Array.Resize(ref _revisions, next);
            Array.Resize(ref _signatures, next);
            Array.Resize(ref _rowStarts, next);
            Array.Resize(ref _rowCounts, next);
            Array.Resize(ref _rowCapacities, next);
            Array.Resize(ref _titles, next);
            Array.Resize(ref _summaries, next);
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
                Array.Copy(_rowIds, _rowStarts[slot], _rowIds, newStart, copyCount);
            }

            _rowStarts[slot] = newStart;
            _rowCapacities[slot] = capacity;
            _rowCursor += capacity;
        }

        private void EnsureRowCapacity(int required)
        {
            if (required <= _rowIds.Length)
            {
                return;
            }

            int next = _rowIds.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _rowIds, next);
        }

        private static ulong ComputeSignature(in IntIdCollectionDescriptor descriptor, ReadOnlySpan<int> ids)
        {
            ulong hash = 14695981039346656037UL;
            hash = HashCombine(hash, (uint)descriptor.SourceKind);
            hash = HashCombine(hash, (uint)descriptor.Role);
            hash = HashString(hash, descriptor.Title ?? string.Empty);
            hash = HashString(hash, descriptor.Summary ?? string.Empty);
            hash = HashCombine(hash, (uint)ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                hash = HashCombine(hash, (uint)ids[i]);
                hash = HashCombine(hash, (uint)i);
            }

            return hash == 0 ? 1UL : hash;
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

        private readonly struct IntIdCollectionPayload
        {
            public IntIdCollectionPayload(int keyId)
            {
                KeyId = keyId;
            }

            public readonly int KeyId;
        }
    }
}
