using System;
using System.Collections.Generic;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public static class GraphIdRegistry
    {
        private static readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _idToName = new();
        private static int _nextId = 1;
        private static bool _frozen;

        public const int InvalidId = 0;
        public const int MaxGraphs = 4095;

        public static bool IsFrozen => _frozen;

        public static void Freeze() => _frozen = true;

        public static void Clear()
        {
            _nameToId.Clear();
            _idToName.Clear();
            _nextId = 1;
            _frozen = false;
        }

        public static int Register(string name)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("GraphIdRegistry is frozen.");
            }

            if (_nameToId.TryGetValue(name, out var id))
            {
                return id;
            }

            if (_nextId > MaxGraphs)
            {
                throw new InvalidOperationException($"GraphIdRegistry supports up to {MaxGraphs} graphs (1..{MaxGraphs}).");
            }

            id = _nextId++;
            _nameToId[name] = id;
            _idToName[id] = name;
            return id;
        }

        public static int GetId(string name)
        {
            return _nameToId.TryGetValue(name, out var id) ? id : InvalidId;
        }

        public static string GetName(int id)
        {
            return _idToName.TryGetValue(id, out var name) ? name : string.Empty;
        }
    }
}

