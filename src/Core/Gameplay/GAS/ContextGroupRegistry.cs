using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS
{
    public readonly struct ContextGroupCandidate
    {
        public ContextGroupCandidate(
            int abilityId,
            int preconditionGraphId,
            int scoreGraphId,
            float basePriority,
            int maxDistanceCm,
            float distanceWeight,
            int maxAngleDeg,
            float angleWeight,
            float hoveredBiasScore,
            bool requiresTarget)
        {
            AbilityId = abilityId;
            PreconditionGraphId = preconditionGraphId;
            ScoreGraphId = scoreGraphId;
            BasePriority = basePriority;
            MaxDistanceCm = maxDistanceCm;
            DistanceWeight = distanceWeight;
            MaxAngleDeg = maxAngleDeg;
            AngleWeight = angleWeight;
            HoveredBiasScore = hoveredBiasScore;
            RequiresTarget = requiresTarget;
        }

        public int AbilityId { get; }
        public int PreconditionGraphId { get; }
        public int ScoreGraphId { get; }
        public float BasePriority { get; }
        public int MaxDistanceCm { get; }
        public float DistanceWeight { get; }
        public int MaxAngleDeg { get; }
        public float AngleWeight { get; }
        public float HoveredBiasScore { get; }
        public bool RequiresTarget { get; }
    }

    public readonly struct ContextGroupDefinition
    {
        public ContextGroupDefinition(int searchRadiusCm, ContextGroupCandidate[] candidates)
        {
            SearchRadiusCm = searchRadiusCm;
            Candidates = candidates ?? Array.Empty<ContextGroupCandidate>();
        }

        public int SearchRadiusCm { get; }
        public IReadOnlyList<ContextGroupCandidate> Candidates { get; }
    }

    public static class ContextGroupIdRegistry
    {
        private static readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _idToName = new();
        private static int _nextId = 1;

        public static void Clear()
        {
            _nameToId.Clear();
            _idToName.Clear();
            _nextId = 1;
        }

        public static int Register(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Context group name is required.", nameof(name));
            }

            if (_nameToId.TryGetValue(name, out var existing))
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
            return _nameToId.TryGetValue(name, out var id) ? id : 0;
        }

        public static string GetName(int id)
        {
            return _idToName.TryGetValue(id, out var name) ? name : string.Empty;
        }
    }

    public sealed class ContextGroupRegistry
    {
        private readonly Dictionary<int, ContextGroupDefinition> _groups = new();
        private readonly Dictionary<int, int> _rootAbilityToGroup = new();

        public void Clear()
        {
            _groups.Clear();
            _rootAbilityToGroup.Clear();
        }

        public void Register(int groupId, int rootAbilityId, in ContextGroupDefinition definition)
        {
            if (groupId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(groupId), "Context group id must be positive.");
            }

            if (rootAbilityId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rootAbilityId), "Root ability id must be positive.");
            }

            _groups[groupId] = definition;
            _rootAbilityToGroup[rootAbilityId] = groupId;
        }

        public bool TryGet(int groupId, out ContextGroupDefinition definition)
        {
            return _groups.TryGetValue(groupId, out definition);
        }

        public bool TryGetByRootAbility(int rootAbilityId, out ContextGroupDefinition definition)
        {
            definition = default;
            return _rootAbilityToGroup.TryGetValue(rootAbilityId, out int groupId) &&
                   _groups.TryGetValue(groupId, out definition);
        }
    }
}
