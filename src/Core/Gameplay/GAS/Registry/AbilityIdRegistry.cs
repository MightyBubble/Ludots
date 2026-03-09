using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    public static class AbilityIdRegistry
    {
        private static readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _idToName = new();
        private static int _nextId = 1;
        private static bool _frozen;

        public const int InvalidId = 0;
        public const int MaxAbilities = 4095;

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
                throw new InvalidOperationException("AbilityIdRegistry is frozen.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Ability id name cannot be null or whitespace.", nameof(name));
            }

            if (_nameToId.TryGetValue(name, out var id))
            {
                return id;
            }

            if (_nextId > MaxAbilities)
            {
                throw new InvalidOperationException($"AbilityIdRegistry supports up to {MaxAbilities} abilities (1..{MaxAbilities}).");
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
