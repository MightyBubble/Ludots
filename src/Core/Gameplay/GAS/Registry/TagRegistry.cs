using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS.Capacity;
using Ludots.Core.Registry;

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

        /// <summary>Absolute registration ceiling (RFC-0066). Live session plan may be lower when frozen.</summary>
        public const int MaxTags = GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace;

        public static bool IsFrozen => _frozen;

        /// <summary>Usable registered tag count (ids <c>1 .. count</c>; id 0 reserved).</summary>
        public static int RegisteredCount => _nextId - 1;

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

            int ceiling = RegistrationCeiling();
            if (_nextId >= ceiling)
            {
                throw new InvalidOperationException(
                    $"Tag registration would exceed ceiling {ceiling} " +
                    (GasLoadTimeCapacitySession.IsFrozen
                        ? "(frozen GasLoadTimeCapacityPlan.TagIdSpace)."
                        : $"(AbsoluteMaxTagIdSpace={MaxTags})."));
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

        public static RegistryMapping[] SnapshotMappings()
        {
            return RegistryMappingSnapshot.FromNameToId(_nameToId);
        }

        private static int RegistrationCeiling()
        {
            if (GasLoadTimeCapacitySession.IsFrozen)
            {
                return GasLoadTimeCapacitySession.Plan.TagIdSpace;
            }

            return MaxTags;
        }
    }
}
