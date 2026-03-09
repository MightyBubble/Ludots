using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    /// <summary>
    /// Maps gameplay tag strings (for gameplay status/effect/event tags only) to integer IDs.
    /// </summary>
    public static class TagRegistry
    {
        private static readonly Dictionary<string, int> _nameToId = new();
        private static readonly Dictionary<int, string> _idToName = new();
        private static int _nextId = 1;
        private static bool _frozen;

        public const int InvalidId = 0;
        public const int MaxTags = 256;

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
                throw new InvalidOperationException("TagRegistry is frozen.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Tag name cannot be null or whitespace.", nameof(name));
            }

            if (_nameToId.TryGetValue(name, out var id))
            {
                return id;
            }

            if (_nextId >= MaxTags)
            {
                throw new InvalidOperationException($"GameplayTagContainer supports up to {MaxTags - 1} tags (id 1..{MaxTags - 1}, id 0 reserved).");
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
