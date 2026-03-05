using System;
using System.Collections.Generic;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Stores <see cref="PerformerDefinition"/> instances keyed by string.
    /// Uses <see cref="StringIntRegistry"/> for the string-to-int mapping;
    /// int IDs are auto-assigned and opaque.
    /// </summary>
    public sealed class PerformerDefinitionRegistry
    {
        private readonly StringIntRegistry _ids;
        private PerformerDefinition[] _items;
        private bool[] _has;

        public IReadOnlyList<int> RegisteredIds => _registeredIds;
        private readonly List<int> _registeredIds = new();

        public int Version { get; private set; }

        public PerformerDefinitionRegistry(int capacity = 1024)
        {
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0);
            _items = new PerformerDefinition[capacity];
            _has = new bool[capacity];
        }

        /// <summary>
        /// Register a definition by string key. Returns the auto-assigned int ID.
        /// Overwrites if the key was already registered.
        /// </summary>
        public int Register(string key, PerformerDefinition definition)
        {
            int id = _ids.Register(key);
            EnsureCapacity(id);
            definition.Id = id;
            definition.BuildBindingIndex();
            _items[id] = definition;
            if (!_has[id])
            {
                _has[id] = true;
                _registeredIds.Add(id);
            }
            Version++;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int id, out PerformerDefinition definition)
        {
            if (id >= 0 && id < _items.Length && _has[id])
            {
                definition = _items[id];
                return true;
            }
            definition = null!;
            return false;
        }

        public PerformerDefinition Get(int id)
        {
            if (!TryGet(id, out var def))
                throw new InvalidOperationException($"PerformerDefinition '{_ids.GetName(id)}' (id={id}) not registered.");
            return def;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _items.Length) return;
            int newLen = Math.Max(_items.Length * 2, id + 1);
            Array.Resize(ref _items, newLen);
            Array.Resize(ref _has, newLen);
        }
    }
}
