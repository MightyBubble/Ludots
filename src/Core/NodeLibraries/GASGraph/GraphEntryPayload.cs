using System;
using Arch.Core;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Fixed-capacity, allocation-free table of the named payload values a TriggerGraph
    /// entry captured at run start from the firing ScriptContext (one slot per schema
    /// param, keyed by payload key string). LoadEntryPayload* ops resolve their symbol id
    /// to the key string and read here; a key the entry event did not carry fails closed
    /// at first read. Keys are captured regardless of whether any graph reads them, so
    /// capture never touches the symbol registry and stays deterministic.
    /// </summary>
    public sealed class GraphEntryPayloadTable
    {
        public const int Capacity = 16;

        private enum SlotType : byte
        {
            None = 0,
            Entity = 1,
            Int = 2,
            Float = 3,
        }

        private readonly string?[] _keys = new string?[Capacity];
        private readonly SlotType[] _types = new SlotType[Capacity];
        private readonly Entity[] _entities = new Entity[Capacity];
        private readonly int[] _ints = new int[Capacity];
        private readonly float[] _floats = new float[Capacity];

        public int Count { get; private set; }

        public void Clear()
        {
            Array.Clear(_keys, 0, _keys.Length);
            Array.Clear(_types, 0, _types.Length);
            Count = 0;
        }

        public void SetEntity(string key, Entity value) => Set(key, SlotType.Entity, entity: value);
        public void SetInt(string key, int value) => Set(key, SlotType.Int, intValue: value);
        public void SetFloat(string key, float value) => Set(key, SlotType.Float, floatValue: value);

        /// <summary>
        /// StoreArg* staging semantics: overwriting the same key replaces the slot in place
        /// (Set appends, which is correct for one-shot schema capture but would exhaust the
        /// fixed capacity when a loop re-stages the same argument).
        /// </summary>
        public void UpsertEntity(string key, Entity value) => Upsert(key, SlotType.Entity, entity: value);
        public void UpsertInt(string key, int value) => Upsert(key, SlotType.Int, intValue: value);
        public void UpsertFloat(string key, float value) => Upsert(key, SlotType.Float, floatValue: value);

        private void Upsert(string key, SlotType type, Entity entity = default, int intValue = 0, float floatValue = 0f)
        {
            for (int i = 0; i < Count; i++)
            {
                if (string.Equals(_keys[i], key, StringComparison.Ordinal))
                {
                    if (_types[i] != type)
                    {
                        throw new InvalidOperationException(
                            $"GAS.GRAPH.ERR.EntryPayloadTypeMismatch: payload key '{key}' was staged as {_types[i]} but re-staged as {type}.");
                    }

                    _types[i] = type;
                    _entities[i] = entity;
                    _ints[i] = intValue;
                    _floats[i] = floatValue;
                    return;
                }
            }

            Set(key, type, entity, intValue, floatValue);
        }

        public bool TryGetEntity(string key, out Entity value)
        {
            value = Entity.Null;
            if (!TryLocate(key, SlotType.Entity, out int slot))
            {
                return false;
            }

            value = _entities[slot];
            return true;
        }

        public bool TryGetInt(string key, out int value)
        {
            value = 0;
            if (!TryLocate(key, SlotType.Int, out int slot))
            {
                return false;
            }

            value = _ints[slot];
            return true;
        }

        public bool TryGetFloat(string key, out float value)
        {
            value = 0f;
            if (!TryLocate(key, SlotType.Float, out int slot))
            {
                return false;
            }

            value = _floats[slot];
            return true;
        }

        private void Set(string key, SlotType type, Entity entity = default, int intValue = 0, float floatValue = 0f)
        {
            if (Count >= Capacity)
            {
                throw new InvalidOperationException(
                    $"Entry payload capture exceeded its {Capacity}-slot capacity (key '{key}').");
            }

            _keys[Count] = key;
            _types[Count] = type;
            _entities[Count] = entity;
            _ints[Count] = intValue;
            _floats[Count] = floatValue;
            Count++;
        }

        private bool TryLocate(string key, SlotType expected, out int slot)
        {
            for (int i = 0; i < Count; i++)
            {
                if (!string.Equals(_keys[i], key, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_types[i] != expected)
                {
                    throw new InvalidOperationException(
                        $"GAS.GRAPH.ERR.EntryPayloadTypeMismatch: payload key '{key}' was captured as {_types[i]} but read as {expected}.");
                }

                slot = i;
                return true;
            }

            slot = -1;
            return false;
        }
    }
}
