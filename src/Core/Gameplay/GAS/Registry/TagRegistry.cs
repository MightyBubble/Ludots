using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    /// <summary>
    /// Maps GameplayTag strings (e.g., "Status.Stun") to integer IDs.
    /// IDs 100-127 are reserved for OrderStateTags and skipped during dynamic allocation.
    /// </summary>
    public static class TagRegistry
    {
        private static readonly Dictionary<string, int> _nameToId = new();
        private static readonly Dictionary<int, string> _idToName = new();
        private static int _nextId = 1;
        private static bool _frozen;

        public const int InvalidId = 0;
        public const int MaxTags = 256;

        /// <summary>
        /// Reserved range for OrderStateTags (hardcoded IDs).
        /// Dynamic allocation skips this range to prevent ID collision.
        /// </summary>
        public const int ReservedRangeStart = 100;
        public const int ReservedRangeEnd = 127;

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
                throw new System.InvalidOperationException("TagRegistry is frozen.");
            }

            if (_nameToId.TryGetValue(name, out var id))
            {
                return id;
            }

            // Skip the reserved range used by OrderStateTags
            if (_nextId == ReservedRangeStart)
            {
                _nextId = ReservedRangeEnd + 1;
            }

            if (_nextId >= MaxTags)
            {
                throw new System.InvalidOperationException($"GameplayTagContainer supports up to {MaxTags - 1} tags (id 1..{MaxTags - 1}, id 0 reserved). Reserved range {ReservedRangeStart}-{ReservedRangeEnd} excluded.");
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
