using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    public static class UnitTypeRegistry
    {
        private static readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _idToName = new();
        private static int _nextId = 1;

        public const int InvalidId = 0;
        public const int MaxUnitTypes = 4095;

        public static void Clear()
        {
            _nameToId.Clear();
            _idToName.Clear();
            _nextId = 1;
        }

        public static int Register(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return InvalidId;
            if (_nameToId.TryGetValue(name, out var id)) return id;

            if (_nextId > MaxUnitTypes)
            {
                throw new InvalidOperationException($"UnitTypeRegistry supports up to {MaxUnitTypes} unit types (1..{MaxUnitTypes}).");
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

