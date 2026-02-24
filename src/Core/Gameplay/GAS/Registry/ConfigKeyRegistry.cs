using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    /// <summary>
    /// Maps config parameter key strings (e.g., "_ep.durationTicks", "myMod.customParam")
    /// to integer IDs. Separates config key namespace from GameplayTag namespace.
    /// </summary>
    public static class ConfigKeyRegistry
    {
        private static readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _idToName = new();
        private static int _nextId = 1;
        private static bool _frozen;

        public const int InvalidId = 0;
        public const int MaxKeys = 4095;

        public static bool IsFrozen => _frozen;

        public static void Freeze()
        {
            _frozen = true;
        }

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
                throw new InvalidOperationException("ConfigKeyRegistry is frozen.");
            }

            if (_nameToId.TryGetValue(name, out var id))
            {
                return id;
            }

            if (_nextId > MaxKeys)
            {
                throw new InvalidOperationException($"ConfigKeyRegistry supports up to {MaxKeys} keys (1..{MaxKeys}).");
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
