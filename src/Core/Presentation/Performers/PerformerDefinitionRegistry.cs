using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Stores <see cref="PerformerDefinition"/> instances by integer ID.
    /// Populated at load time from configuration; read at runtime by
    /// PerformerRuleSystem and PerformerEmitSystem.
    /// </summary>
    public sealed class PerformerDefinitionRegistry
    {
        private PerformerDefinition[] _items;
        private bool[] _has;
        private readonly List<int> _registeredIds = new();

        /// <summary>All registered definition IDs (for enumeration).</summary>
        public IReadOnlyList<int> RegisteredIds => _registeredIds;

        public PerformerDefinitionRegistry(int capacity = 1024)
        {
            _items = new PerformerDefinition[capacity];
            _has = new bool[capacity];
        }

        /// <summary>Incremented on each registration; used by systems to detect stale caches.</summary>
        public int Version { get; private set; }

        /// <summary>
        /// Register a definition. Overwrites any existing definition at the same ID.
        /// Automatically builds the O(1) binding index.
        /// </summary>
        public void Register(int id, PerformerDefinition definition)
        {
            EnsureCapacity(id);
            definition.Id = id;
            definition.BuildBindingIndex();
            _items[id] = definition;
            if (!_has[id])
            {
                _has[id] = true;
                _registeredIds.Add(id);
            }
            else
            {
                _items[id] = definition;
            }
            Version++;
        }

        /// <summary>
        /// Try to retrieve a definition by ID.
        /// </summary>
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

        /// <summary>
        /// Retrieve a definition by ID. Throws if not found.
        /// </summary>
        public PerformerDefinition Get(int id)
        {
            if (!TryGet(id, out var def))
                throw new InvalidOperationException($"PerformerDefinition {id} not registered.");
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
