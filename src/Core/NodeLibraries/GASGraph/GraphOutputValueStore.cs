using System;
using Arch.Core;
using Ludots.Core.Registry;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public readonly record struct GraphOutputValueHandle(int Slot, uint Generation)
    {
        public bool IsValid => Slot >= 0 && Generation != 0;
        public static GraphOutputValueHandle Invalid { get; } = new(-1, 0);
    }

    public readonly record struct GraphOutputValueView(
        Entity Owner,
        int KeyId,
        string Key,
        GraphOutputValueKind Kind,
        uint Revision,
        bool BoolValue,
        int IntValue,
        float FloatValue,
        Entity EntityValue);

    public sealed class GraphOutputValueStore
    {
        private const float LoadFactor = 0.72f;

        private readonly StringIntRegistry _keyRegistry;

        private bool[] _active;
        private Entity[] _owners;
        private int[] _ownerIds;
        private int[] _ownerWorldIds;
        private int[] _ownerVersions;
        private int[] _keyIds;
        private GraphOutputValueKind[] _kinds;
        private uint[] _handleGenerations;
        private uint[] _revisions;
        private byte[] _boolValues;
        private int[] _intValues;
        private float[] _floatValues;
        private Entity[] _entityValues;
        private int[] _freeSlots;
        private int _freeSlotCount;

        private int[] _bucketHeads;
        private int[] _entryNext;
        private int[] _entryOwnerIds;
        private int[] _entryOwnerWorldIds;
        private int[] _entryOwnerVersions;
        private int[] _entryKeyIds;
        private int[] _entrySlots;
        private bool[] _entryActive;
        private int[] _slotEntries;
        private int _freeEntryHead = -1;

        private int[] _ownerBucketHeads;
        private int[] _ownerEntryNext;
        private int[] _ownerEntryIds;
        private int[] _ownerEntryWorldIds;
        private int[] _ownerEntryVersions;
        private int[] _ownerEntryHeadSlots;
        private bool[] _ownerEntryActive;
        private bool[] _ownerRetirementPending;
        private int[] _slotOwnerEntries;
        private int[] _slotOwnerNext;
        private Entity[] _pendingRetiredOwners;
        private int _pendingRetiredOwnerCount;
        private int _freeOwnerEntryHead = -1;

        private int _slotCount;
        private int _entryCount;
        private int _activeEntryCount;
        private int _ownerEntryCount;
        private int _activeOwnerEntryCount;

        public GraphOutputValueStore(StringIntRegistry keyRegistry, int initialCapacity = 64)
        {
            _keyRegistry = keyRegistry ?? throw new ArgumentNullException(nameof(keyRegistry));
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _active = new bool[initialCapacity];
            _owners = new Entity[initialCapacity];
            _ownerIds = new int[initialCapacity];
            _ownerWorldIds = new int[initialCapacity];
            _ownerVersions = new int[initialCapacity];
            _keyIds = new int[initialCapacity];
            _kinds = new GraphOutputValueKind[initialCapacity];
            _handleGenerations = new uint[initialCapacity];
            _revisions = new uint[initialCapacity];
            _boolValues = new byte[initialCapacity];
            _intValues = new int[initialCapacity];
            _floatValues = new float[initialCapacity];
            _entityValues = new Entity[initialCapacity];
            _freeSlots = new int[initialCapacity];

            int bucketCount = NextPowerOfTwo(Math.Max(16, initialCapacity * 2));
            _bucketHeads = new int[bucketCount];
            Array.Fill(_bucketHeads, -1);
            _entryNext = new int[initialCapacity];
            _entryOwnerIds = new int[initialCapacity];
            _entryOwnerWorldIds = new int[initialCapacity];
            _entryOwnerVersions = new int[initialCapacity];
            _entryKeyIds = new int[initialCapacity];
            _entrySlots = new int[initialCapacity];
            _entryActive = new bool[initialCapacity];
            _slotEntries = new int[initialCapacity];
            Array.Fill(_slotEntries, -1);

            _ownerBucketHeads = new int[bucketCount];
            Array.Fill(_ownerBucketHeads, -1);
            _ownerEntryNext = new int[initialCapacity];
            _ownerEntryIds = new int[initialCapacity];
            _ownerEntryWorldIds = new int[initialCapacity];
            _ownerEntryVersions = new int[initialCapacity];
            _ownerEntryHeadSlots = new int[initialCapacity];
            Array.Fill(_ownerEntryHeadSlots, -1);
            _ownerEntryActive = new bool[initialCapacity];
            _ownerRetirementPending = new bool[initialCapacity];
            _slotOwnerEntries = new int[initialCapacity];
            Array.Fill(_slotOwnerEntries, -1);
            _slotOwnerNext = new int[initialCapacity];
            Array.Fill(_slotOwnerNext, -1);
            _pendingRetiredOwners = new Entity[initialCapacity];
        }

        public StringIntRegistry KeyRegistry => _keyRegistry;
        public int ActiveCount { get; private set; }

        public GraphOutputValueHandle SetBool(Entity owner, string key, bool value)
        {
            int keyId = RequireKey(key);
            return SetBool(owner, keyId, value);
        }

        public GraphOutputValueHandle SetBool(Entity owner, int keyId, bool value)
        {
            int slot = PrepareSlot(owner, keyId, GraphOutputValueKind.Bool, out bool kindChanged);
            bool changed = kindChanged || _boolValues[slot] != (value ? (byte)1 : (byte)0);
            _boolValues[slot] = value ? (byte)1 : (byte)0;
            Commit(slot, changed);
            return new GraphOutputValueHandle(slot, _handleGenerations[slot]);
        }

        public GraphOutputValueHandle SetInt(Entity owner, string key, int value)
        {
            int keyId = RequireKey(key);
            return SetInt(owner, keyId, value);
        }

        public GraphOutputValueHandle SetInt(Entity owner, int keyId, int value)
        {
            int slot = PrepareSlot(owner, keyId, GraphOutputValueKind.Int, out bool kindChanged);
            bool changed = kindChanged || _intValues[slot] != value;
            _intValues[slot] = value;
            Commit(slot, changed);
            return new GraphOutputValueHandle(slot, _handleGenerations[slot]);
        }

        public GraphOutputValueHandle SetFloat(Entity owner, string key, float value)
        {
            int keyId = RequireKey(key);
            return SetFloat(owner, keyId, value);
        }

        public GraphOutputValueHandle SetFloat(Entity owner, int keyId, float value)
        {
            int slot = PrepareSlot(owner, keyId, GraphOutputValueKind.Float, out bool kindChanged);
            bool changed = kindChanged || _floatValues[slot] != value;
            _floatValues[slot] = value;
            Commit(slot, changed);
            return new GraphOutputValueHandle(slot, _handleGenerations[slot]);
        }

        public GraphOutputValueHandle SetEntity(Entity owner, string key, Entity value)
        {
            int keyId = RequireKey(key);
            return SetEntity(owner, keyId, value);
        }

        public GraphOutputValueHandle SetEntity(Entity owner, int keyId, Entity value)
        {
            int slot = PrepareSlot(owner, keyId, GraphOutputValueKind.Entity, out bool kindChanged);
            bool changed = kindChanged || _entityValues[slot] != value;
            _entityValues[slot] = value;
            Commit(slot, changed);
            return new GraphOutputValueHandle(slot, _handleGenerations[slot]);
        }

        public bool TryGet(Entity owner, string key, out GraphOutputValueHandle handle)
        {
            handle = GraphOutputValueHandle.Invalid;
            if (owner == Entity.Null ||
                string.IsNullOrWhiteSpace(key) ||
                !_keyRegistry.TryGetId(key, out int keyId) ||
                keyId <= 0 ||
                !TryFindSlot(owner, keyId, out int slot) ||
                !_active[slot])
            {
                return false;
            }

            handle = new GraphOutputValueHandle(slot, _handleGenerations[slot]);
            return true;
        }

        public bool TryGetView(GraphOutputValueHandle handle, out GraphOutputValueView view)
        {
            view = default;
            if (!TryValidateSlot(handle.Slot) || _handleGenerations[handle.Slot] != handle.Generation)
            {
                return false;
            }

            int slot = handle.Slot;
            int keyId = _keyIds[slot];
            view = new GraphOutputValueView(
                _owners[slot],
                keyId,
                _keyRegistry.GetName(keyId),
                _kinds[slot],
                _revisions[slot],
                _boolValues[slot] != 0,
                _intValues[slot],
                _floatValues[slot],
                _entityValues[slot]);
            return true;
        }

        public int RemoveOwner(Entity owner)
        {
            if (owner == Entity.Null || !TryFindOwnerEntry(owner, out int ownerEntry))
            {
                return 0;
            }

            int removed = 0;
            int slot = _ownerEntryHeadSlots[ownerEntry];
            RemoveOwnerEntry(ownerEntry);
            while (slot >= 0)
            {
                int next = _slotOwnerNext[slot];
                ReleaseSlot(slot, unlinkOwner: false);
                removed++;
                slot = next;
            }

            return removed;
        }

        public bool QueueOwnerRetirement(Entity owner)
        {
            if (owner == Entity.Null ||
                !TryFindOwnerEntry(owner, out int ownerEntry) ||
                _ownerRetirementPending[ownerEntry])
            {
                return false;
            }

            if (_pendingRetiredOwnerCount >= _pendingRetiredOwners.Length)
            {
                throw new InvalidOperationException("Graph output owner retirement queue capacity exceeded.");
            }

            _ownerRetirementPending[ownerEntry] = true;
            _pendingRetiredOwners[_pendingRetiredOwnerCount++] = owner;
            return true;
        }

        public int ReleaseQueuedOwners(out int retiredOwnersProcessed)
        {
            retiredOwnersProcessed = _pendingRetiredOwnerCount;
            int removed = 0;
            for (int i = 0; i < _pendingRetiredOwnerCount; i++)
            {
                Entity owner = _pendingRetiredOwners[i];
                _pendingRetiredOwners[i] = Entity.Null;
                removed += RemoveOwner(owner);
            }

            _pendingRetiredOwnerCount = 0;
            return removed;
        }

        private int PrepareSlot(Entity owner, int keyId, GraphOutputValueKind kind, out bool kindChanged)
        {
            if (owner == Entity.Null)
            {
                throw new ArgumentException("Graph output owner is required.", nameof(owner));
            }

            if (keyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(keyId));
            }

            int slot = GetOrCreateSlot(owner, keyId);
            kindChanged = !_active[slot] || _kinds[slot] != kind;
            _active[slot] = true;
            _owners[slot] = owner;
            _ownerIds[slot] = owner.Id;
            _ownerWorldIds[slot] = owner.WorldId;
            _ownerVersions[slot] = owner.Version;
            _keyIds[slot] = keyId;
            _kinds[slot] = kind;
            if (_handleGenerations[slot] == 0)
            {
                _handleGenerations[slot] = 1;
            }
            if (kindChanged)
            {
                _boolValues[slot] = 0;
                _intValues[slot] = 0;
                _floatValues[slot] = 0f;
                _entityValues[slot] = Entity.Null;
            }

            return slot;
        }

        private void Commit(int slot, bool changed)
        {
            if (changed || _revisions[slot] == 0)
            {
                _revisions[slot]++;
                if (_revisions[slot] == 0)
                {
                    _revisions[slot] = 1;
                }
            }
        }

        private int RequireKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Graph output key is required.", nameof(key));
            }

            return _keyRegistry.Register(key.Trim());
        }

        private int GetOrCreateSlot(Entity owner, int keyId)
        {
            if (TryFindSlot(owner, keyId, out int existing))
            {
                return existing;
            }

            EnsureSlotCapacity(_slotCount + 1);
            if ((_activeEntryCount + 1) > (int)(_bucketHeads.Length * LoadFactor))
            {
                Rehash(_bucketHeads.Length * 2);
            }
            if ((_activeOwnerEntryCount + 1) > (int)(_ownerBucketHeads.Length * LoadFactor))
            {
                RehashOwners(_ownerBucketHeads.Length * 2);
            }

            int slot;
            if (_freeSlotCount > 0)
            {
                slot = _freeSlots[--_freeSlotCount];
            }
            else
            {
                slot = _slotCount++;
            }

            int ownerEntry = GetOrCreateOwnerEntry(owner);
            _slotOwnerEntries[slot] = ownerEntry;
            _slotOwnerNext[slot] = _ownerEntryHeadSlots[ownerEntry];
            _ownerEntryHeadSlots[ownerEntry] = slot;

            int entry = AllocateEntry();
            _entryOwnerIds[entry] = owner.Id;
            _entryOwnerWorldIds[entry] = owner.WorldId;
            _entryOwnerVersions[entry] = owner.Version;
            _entryKeyIds[entry] = keyId;
            _entrySlots[entry] = slot;
            _slotEntries[slot] = entry;
            int bucket = BucketIndex(owner.Id, owner.WorldId, owner.Version, keyId, _bucketHeads.Length);
            _entryNext[entry] = _bucketHeads[bucket];
            _bucketHeads[bucket] = entry;
            ActiveCount++;
            return slot;
        }

        private void ReleaseSlot(int slot, bool unlinkOwner)
        {
            RemoveEntry(_slotEntries[slot]);
            if (unlinkOwner)
            {
                UnlinkSlotFromOwner(_slotOwnerEntries[slot], slot);
            }

            _active[slot] = false;
            _owners[slot] = Entity.Null;
            _ownerIds[slot] = 0;
            _ownerWorldIds[slot] = 0;
            _ownerVersions[slot] = 0;
            _keyIds[slot] = 0;
            _kinds[slot] = default;
            _boolValues[slot] = 0;
            _intValues[slot] = 0;
            _floatValues[slot] = 0f;
            _entityValues[slot] = Entity.Null;
            _slotEntries[slot] = -1;
            _slotOwnerEntries[slot] = -1;
            _slotOwnerNext[slot] = -1;
            _handleGenerations[slot]++;
            if (_handleGenerations[slot] == 0)
            {
                _handleGenerations[slot] = 1;
            }

            _freeSlots[_freeSlotCount++] = slot;
            ActiveCount--;
        }

        private int AllocateEntry()
        {
            int entry;
            if (_freeEntryHead >= 0)
            {
                entry = _freeEntryHead;
                _freeEntryHead = _entryNext[entry];
            }
            else
            {
                EnsureEntryCapacity(_entryCount + 1);
                entry = _entryCount++;
            }

            _entryActive[entry] = true;
            _activeEntryCount++;
            return entry;
        }

        private void RemoveEntry(int entry)
        {
            if (entry < 0 || !_entryActive[entry])
            {
                throw new InvalidOperationException("Graph output key entry is missing for an active slot.");
            }

            int bucket = BucketIndex(
                _entryOwnerIds[entry],
                _entryOwnerWorldIds[entry],
                _entryOwnerVersions[entry],
                _entryKeyIds[entry],
                _bucketHeads.Length);
            int previous = -1;
            for (int current = _bucketHeads[bucket]; current >= 0; current = _entryNext[current])
            {
                if (current != entry)
                {
                    previous = current;
                    continue;
                }

                if (previous < 0)
                {
                    _bucketHeads[bucket] = _entryNext[current];
                }
                else
                {
                    _entryNext[previous] = _entryNext[current];
                }

                _entryActive[entry] = false;
                _entryOwnerIds[entry] = 0;
                _entryOwnerWorldIds[entry] = 0;
                _entryOwnerVersions[entry] = 0;
                _entryKeyIds[entry] = 0;
                _entrySlots[entry] = -1;
                _entryNext[entry] = _freeEntryHead;
                _freeEntryHead = entry;
                _activeEntryCount--;
                return;
            }

            throw new InvalidOperationException("Graph output key entry is not linked from its hash bucket.");
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

        private int GetOrCreateOwnerEntry(Entity owner)
        {
            if (TryFindOwnerEntry(owner, out int existing))
            {
                return existing;
            }

            int entry;
            if (_freeOwnerEntryHead >= 0)
            {
                entry = _freeOwnerEntryHead;
                _freeOwnerEntryHead = _ownerEntryNext[entry];
            }
            else
            {
                EnsureOwnerEntryCapacity(_ownerEntryCount + 1);
                entry = _ownerEntryCount++;
            }

            _ownerEntryActive[entry] = true;
            _ownerEntryIds[entry] = owner.Id;
            _ownerEntryWorldIds[entry] = owner.WorldId;
            _ownerEntryVersions[entry] = owner.Version;
            _ownerEntryHeadSlots[entry] = -1;
            _ownerRetirementPending[entry] = false;
            int bucket = OwnerBucketIndex(owner.Id, owner.WorldId, owner.Version, _ownerBucketHeads.Length);
            _ownerEntryNext[entry] = _ownerBucketHeads[bucket];
            _ownerBucketHeads[bucket] = entry;
            _activeOwnerEntryCount++;
            return entry;
        }

        private bool TryFindOwnerEntry(Entity owner, out int entry)
        {
            int bucket = OwnerBucketIndex(owner.Id, owner.WorldId, owner.Version, _ownerBucketHeads.Length);
            for (int current = _ownerBucketHeads[bucket]; current >= 0; current = _ownerEntryNext[current])
            {
                if (_ownerEntryIds[current] == owner.Id &&
                    _ownerEntryWorldIds[current] == owner.WorldId &&
                    _ownerEntryVersions[current] == owner.Version)
                {
                    entry = current;
                    return true;
                }
            }

            entry = -1;
            return false;
        }

        private void RemoveOwnerEntry(int entry)
        {
            int bucket = OwnerBucketIndex(
                _ownerEntryIds[entry],
                _ownerEntryWorldIds[entry],
                _ownerEntryVersions[entry],
                _ownerBucketHeads.Length);
            int previous = -1;
            for (int current = _ownerBucketHeads[bucket]; current >= 0; current = _ownerEntryNext[current])
            {
                if (current != entry)
                {
                    previous = current;
                    continue;
                }

                if (previous < 0)
                {
                    _ownerBucketHeads[bucket] = _ownerEntryNext[current];
                }
                else
                {
                    _ownerEntryNext[previous] = _ownerEntryNext[current];
                }

                _ownerEntryActive[entry] = false;
                _ownerEntryIds[entry] = 0;
                _ownerEntryWorldIds[entry] = 0;
                _ownerEntryVersions[entry] = 0;
                _ownerEntryHeadSlots[entry] = -1;
                _ownerRetirementPending[entry] = false;
                _ownerEntryNext[entry] = _freeOwnerEntryHead;
                _freeOwnerEntryHead = entry;
                _activeOwnerEntryCount--;
                return;
            }

            throw new InvalidOperationException("Graph output owner entry is not linked from its hash bucket.");
        }

        private void UnlinkSlotFromOwner(int ownerEntry, int slot)
        {
            int previous = -1;
            for (int current = _ownerEntryHeadSlots[ownerEntry]; current >= 0; current = _slotOwnerNext[current])
            {
                if (current != slot)
                {
                    previous = current;
                    continue;
                }

                if (previous < 0)
                {
                    _ownerEntryHeadSlots[ownerEntry] = _slotOwnerNext[current];
                }
                else
                {
                    _slotOwnerNext[previous] = _slotOwnerNext[current];
                }

                if (_ownerEntryHeadSlots[ownerEntry] < 0)
                {
                    RemoveOwnerEntry(ownerEntry);
                }
                return;
            }

            throw new InvalidOperationException("Graph output slot is not linked from its owner entry.");
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
            Array.Resize(ref _kinds, next);
            Array.Resize(ref _handleGenerations, next);
            Array.Resize(ref _revisions, next);
            Array.Resize(ref _boolValues, next);
            Array.Resize(ref _intValues, next);
            Array.Resize(ref _floatValues, next);
            Array.Resize(ref _entityValues, next);
            Array.Resize(ref _freeSlots, next);
            int oldSlotEntryLength = _slotEntries.Length;
            Array.Resize(ref _slotEntries, next);
            Array.Fill(_slotEntries, -1, oldSlotEntryLength, next - oldSlotEntryLength);
            int oldOwnerEntryLength = _slotOwnerEntries.Length;
            Array.Resize(ref _slotOwnerEntries, next);
            Array.Fill(_slotOwnerEntries, -1, oldOwnerEntryLength, next - oldOwnerEntryLength);
            int oldOwnerNextLength = _slotOwnerNext.Length;
            Array.Resize(ref _slotOwnerNext, next);
            Array.Fill(_slotOwnerNext, -1, oldOwnerNextLength, next - oldOwnerNextLength);
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
            Array.Resize(ref _entryActive, next);
        }

        private void EnsureOwnerEntryCapacity(int required)
        {
            if (required <= _ownerEntryNext.Length)
            {
                return;
            }

            int next = _ownerEntryNext.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _ownerEntryNext, next);
            Array.Resize(ref _ownerEntryIds, next);
            Array.Resize(ref _ownerEntryWorldIds, next);
            Array.Resize(ref _ownerEntryVersions, next);
            int oldHeadLength = _ownerEntryHeadSlots.Length;
            Array.Resize(ref _ownerEntryHeadSlots, next);
            Array.Fill(_ownerEntryHeadSlots, -1, oldHeadLength, next - oldHeadLength);
            Array.Resize(ref _ownerEntryActive, next);
            Array.Resize(ref _ownerRetirementPending, next);
            Array.Resize(ref _pendingRetiredOwners, next);
        }

        private void Rehash(int bucketCount)
        {
            int nextBucketCount = NextPowerOfTwo(Math.Max(16, bucketCount));
            Array.Resize(ref _bucketHeads, nextBucketCount);
            Array.Fill(_bucketHeads, -1);
            for (int entry = 0; entry < _entryCount; entry++)
            {
                if (!_entryActive[entry])
                {
                    continue;
                }

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

        private void RehashOwners(int bucketCount)
        {
            int nextBucketCount = NextPowerOfTwo(Math.Max(16, bucketCount));
            Array.Resize(ref _ownerBucketHeads, nextBucketCount);
            Array.Fill(_ownerBucketHeads, -1);
            for (int entry = 0; entry < _ownerEntryCount; entry++)
            {
                if (!_ownerEntryActive[entry])
                {
                    continue;
                }

                int bucket = OwnerBucketIndex(
                    _ownerEntryIds[entry],
                    _ownerEntryWorldIds[entry],
                    _ownerEntryVersions[entry],
                    _ownerBucketHeads.Length);
                _ownerEntryNext[entry] = _ownerBucketHeads[bucket];
                _ownerBucketHeads[bucket] = entry;
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

        private static int OwnerBucketIndex(int ownerId, int ownerWorldId, int ownerVersion, int bucketCount)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)ownerId) * 16777619u;
                hash = (hash ^ (uint)ownerWorldId) * 16777619u;
                hash = (hash ^ (uint)ownerVersion) * 16777619u;
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
