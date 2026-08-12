using System;
using System.Collections.Generic;
using Ludots.Core.Registry;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public readonly struct AbilityFormSlotOverride
    {
        public AbilityFormSlotOverride(int slotIndex, int abilityId)
        {
            SlotIndex = slotIndex;
            AbilityId = abilityId;
        }

        public int SlotIndex { get; }
        public int AbilityId { get; }
    }

    public readonly struct AbilityFormRouteDefinition
    {
        public AbilityFormRouteDefinition(
            GameplayTagBitSet requiredAll,
            GameplayTagBitSet blockedAny,
            int priority,
            AbilityFormSlotOverride[] slotOverrides)
        {
            RequiredAll = requiredAll;
            BlockedAny = blockedAny;
            Priority = priority;
            SlotOverrides = slotOverrides ?? Array.Empty<AbilityFormSlotOverride>();
        }

        public GameplayTagBitSet RequiredAll { get; }
        public GameplayTagBitSet BlockedAny { get; }
        public int Priority { get; }
        public IReadOnlyList<AbilityFormSlotOverride> SlotOverrides { get; }
    }

    public readonly struct AbilityFormSetDefinition
    {
        public AbilityFormSetDefinition(AbilityFormRouteDefinition[] routes)
        {
            Routes = routes ?? Array.Empty<AbilityFormRouteDefinition>();
        }

        public IReadOnlyList<AbilityFormRouteDefinition> Routes { get; }
    }

    public static class AbilityFormSetIdRegistry
    {
        private static readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _idToName = new();
        private static int _nextId = 1;
        private static bool _frozen;

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
                throw new InvalidOperationException("AbilityFormSetIdRegistry is frozen.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Ability form set name is required.", nameof(name));
            }

            if (_nameToId.TryGetValue(name, out int existing))
            {
                return existing;
            }

            int id = _nextId++;
            _nameToId[name] = id;
            _idToName[id] = name;
            return id;
        }

        public static int GetId(string name)
        {
            return _nameToId.TryGetValue(name, out int id) ? id : 0;
        }

        public static string GetName(int id)
        {
            return _idToName.TryGetValue(id, out string? name) ? name : string.Empty;
        }

        public static RegistryMapping[] SnapshotMappings()
        {
            return RegistryMappingSnapshot.FromNameToId(_nameToId);
        }
    }

    public sealed class AbilityFormSetRegistry
    {
        private readonly Dictionary<int, AbilityFormSetDefinition> _sets = new();

        public void Clear()
        {
            _sets.Clear();
        }

        public void Register(int formSetId, in AbilityFormSetDefinition definition)
        {
            if (formSetId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(formSetId), "Ability form set id must be positive.");
            }

            _sets[formSetId] = definition;
        }

        public bool TryGet(int formSetId, out AbilityFormSetDefinition definition)
        {
            return _sets.TryGetValue(formSetId, out definition);
        }
    }
}
