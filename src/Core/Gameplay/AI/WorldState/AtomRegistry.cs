using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.AI.WorldState
{
    public sealed class AtomRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.Ordinal);
        private readonly List<string> _idToName = new();
        private readonly int _capacity;

        public AtomRegistry(int capacity = 256)
        {
            _capacity = capacity > 0 ? capacity : 256;
        }

        public int Capacity => _capacity;

        public int Count => _idToName.Count;

        public string GetName(int atomId)
        {
            if ((uint)atomId >= (uint)_idToName.Count) return string.Empty;
            return _idToName[atomId];
        }

        public bool TryGetId(string name, out int atomId)
        {
            return _nameToId.TryGetValue(name, out atomId);
        }

        public int GetOrAdd(string name)
        {
            if (_nameToId.TryGetValue(name, out int id)) return id;
            if (_idToName.Count >= _capacity) throw new InvalidOperationException($"AtomRegistry capacity exceeded: {_capacity}");
            id = _idToName.Count;
            _idToName.Add(name);
            _nameToId.Add(name, id);
            return id;
        }
    }
}

