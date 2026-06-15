using System;
using Arch.Core;
using Ludots.Core.Registry;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public readonly record struct GraphOutputValueHandle(int Slot, uint Revision)
    {
        public bool IsValid => Slot >= 0 && Revision != 0;
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
        private uint[] _revisions;
        private byte[] _boolValues;
        private int[] _intValues;
        private float[] _floatValues;
        private Entity[] _entityValues;

        private int[] _bucketHeads;
        private int[] _entryNext;
        private int[] _entryOwnerIds;
        private int[] _entryOwnerWorldIds;
        private int[] _entryOwnerVersions;
        private int[] _entryKeyIds;
        private int[] _entrySlots;

        private int _slotCount;
        private int _entryCount;

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
            _revisions = new uint[initialCapacity];
            _boolValues = new byte[initialCapacity];
            _intValues = new int[initialCapacity];
            _floatValues = new float[initialCapacity];
            _entityValues = new Entity[initialCapacity];

            int bucketCount = NextPowerOfTwo(Math.Max(16, initialCapacity * 2));
            _bucketHeads = new int[bucketCount];
            Array.Fill(_bucketHeads, -1);
            _entryNext = new int[initialCapacity];
            _entryOwnerIds = new int[initialCapacity];
            _entryOwnerWorldIds = new int[initialCapacity];
            _entryOwnerVersions = new int[initialCapacity];
            _entryKeyIds = new int[initialCapacity];
            _entrySlots = new int[initialCapacity];
        }

        public StringIntRegistry KeyRegistry => _keyRegistry;

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
            return new GraphOutputValueHandle(slot, _revisions[slot]);
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
            return new GraphOutputValueHandle(slot, _revisions[slot]);
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
            return new GraphOutputValueHandle(slot, _revisions[slot]);
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
            return new GraphOutputValueHandle(slot, _revisions[slot]);
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

            handle = new GraphOutputValueHandle(slot, _revisions[slot]);
            return true;
        }

        public bool TryGetView(GraphOutputValueHandle handle, out GraphOutputValueView view)
        {
            view = default;
            if (!TryValidateSlot(handle.Slot))
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
            Array.Resize(ref _kinds, next);
            Array.Resize(ref _revisions, next);
            Array.Resize(ref _boolValues, next);
            Array.Resize(ref _intValues, next);
            Array.Resize(ref _floatValues, next);
            Array.Resize(ref _entityValues, next);
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
