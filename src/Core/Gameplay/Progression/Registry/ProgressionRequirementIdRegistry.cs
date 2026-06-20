using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Progression.Registry
{
    public static class ProgressionRequirementIdRegistry
    {
        private static readonly Dictionary<string, int> NameToId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> IdToName = new();
        private static int _nextId = 1;
        private static bool _frozen;

        public const int InvalidId = 0;
        public const int MaxRequirements = 4095;

        public static bool IsFrozen => _frozen;

        public static void Clear()
        {
            NameToId.Clear();
            IdToName.Clear();
            _nextId = 1;
            _frozen = false;
        }

        public static int Register(string name)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("ProgressionRequirementIdRegistry is frozen.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Progression requirement id name cannot be null or whitespace.", nameof(name));
            }

            if (NameToId.TryGetValue(name, out int id))
            {
                return id;
            }

            if (_nextId > MaxRequirements)
            {
                throw new InvalidOperationException($"ProgressionRequirementIdRegistry supports up to {MaxRequirements} requirements.");
            }

            id = _nextId++;
            NameToId[name] = id;
            IdToName[id] = name;
            return id;
        }

        public static int GetId(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && NameToId.TryGetValue(name, out int id)
                ? id
                : InvalidId;
        }

        public static string GetName(int id)
        {
            return IdToName.TryGetValue(id, out string? name) ? name : string.Empty;
        }

        public static void Freeze()
        {
            _frozen = true;
        }
    }
}
