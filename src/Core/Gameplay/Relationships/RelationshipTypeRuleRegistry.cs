using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.Relationships.Config;

namespace Ludots.Core.Gameplay.Relationships
{
    public readonly struct RelationshipTypeRule
    {
        public RelationshipTypeRule(
            int maxIncoming = 0,
            int maxOutgoing = 0,
            RelationshipCapacityPolicy onFull = RelationshipCapacityPolicy.None,
            bool acyclic = false,
            int[]? blockedAnyTypeIds = null,
            int[]? removedTypeIds = null)
        {
            MaxIncoming = maxIncoming;
            MaxOutgoing = maxOutgoing;
            OnFull = onFull;
            Acyclic = acyclic;
            BlockedAnyTypeIds = blockedAnyTypeIds ?? Array.Empty<int>();
            RemovedTypeIds = removedTypeIds ?? Array.Empty<int>();
        }

        public int MaxIncoming { get; }
        public int MaxOutgoing { get; }
        public RelationshipCapacityPolicy OnFull { get; }
        public bool Acyclic { get; }
        public int[] BlockedAnyTypeIds { get; }
        public int[] RemovedTypeIds { get; }

        public bool IsEmpty =>
            MaxIncoming == 0 &&
            MaxOutgoing == 0 &&
            !Acyclic &&
            BlockedAnyTypeIds.Length == 0 &&
            RemovedTypeIds.Length == 0;
    }

    /// <summary>
    /// Per-type link constraints, compiled once from <c>Relationships/catalog.json</c> and read on every link.
    /// A type without a registered rule links without constraints.
    /// </summary>
    public sealed class RelationshipTypeRuleRegistry
    {
        private readonly RelationshipTypeRegistry _types;
        private RelationshipTypeRule[] _rules = Array.Empty<RelationshipTypeRule>();
        private bool[] _hasRule = Array.Empty<bool>();

        public RelationshipTypeRuleRegistry(RelationshipTypeRegistry types)
        {
            _types = types ?? throw new ArgumentNullException(nameof(types));
        }

        public void Register(int typeId, in RelationshipTypeRule rule)
        {
            RequireTypeId(typeId);
            Validate(typeId, in rule);
            EnsureCapacity(typeId + 1);
            _rules[typeId] = rule;
            _hasRule[typeId] = !rule.IsEmpty;
        }

        public void InstallFromCatalog(RelationshipCatalogConfig catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            var errors = new List<string>();
            for (int i = 0; i < catalog.Types.Count; i++)
            {
                RelationshipTypeConfig type = catalog.Types[i];
                if (type.Rules == null)
                {
                    continue;
                }

                try
                {
                    Register(_types.GetId(type.Id), Compile(type.Id, type.Rules));
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    errors.Add(ex.Message);
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Relationships/catalog.json has {errors.Count} invalid relationship rule(s):{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
            }
        }

        public bool TryGet(int typeId, out RelationshipTypeRule rule)
        {
            if ((uint)typeId < (uint)_hasRule.Length && _hasRule[typeId])
            {
                rule = _rules[typeId];
                return true;
            }

            rule = default;
            return false;
        }

        private RelationshipTypeRule Compile(string typeName, RelationshipTypeRulesConfig config)
        {
            return new RelationshipTypeRule(
                config.MaxIncoming,
                config.MaxOutgoing,
                config.OnFull,
                config.Acyclic,
                ResolveTypeList(typeName, "blockedAny", config.BlockedAny),
                ResolveTypeList(typeName, "removed", config.Removed));
        }

        private int[] ResolveTypeList(string typeName, string field, List<string> names)
        {
            if (names.Count == 0)
            {
                return Array.Empty<int>();
            }

            var ids = new int[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                if (!_types.TryGetId(names[i], out ids[i]))
                {
                    throw new InvalidOperationException(
                        $"Relationship type '{typeName}' rules.{field} references undeclared relationship type '{names[i]}'.");
                }
            }

            return ids;
        }

        private void Validate(int typeId, in RelationshipTypeRule rule)
        {
            ref readonly RelationshipTypeDefinition definition = ref _types.Get(typeId);
            string name = definition.Name;
            if (rule.MaxIncoming < 0 || rule.MaxOutgoing < 0)
            {
                throw new InvalidOperationException($"Relationship type '{name}' rules: maxIncoming / maxOutgoing must not be negative.");
            }

            bool hasCapacity = rule.MaxIncoming > 0 || rule.MaxOutgoing > 0;
            if (hasCapacity && rule.OnFull == RelationshipCapacityPolicy.None)
            {
                throw new InvalidOperationException($"Relationship type '{name}' rules: a maximum is set, so onFull must say Reject or Replace.");
            }

            if (!hasCapacity && rule.OnFull != RelationshipCapacityPolicy.None)
            {
                throw new InvalidOperationException($"Relationship type '{name}' rules: onFull is set but no maxIncoming / maxOutgoing is.");
            }

            if (rule.OnFull == RelationshipCapacityPolicy.Replace && (rule.MaxIncoming > 1 || rule.MaxOutgoing > 1))
            {
                throw new InvalidOperationException($"Relationship type '{name}' rules: onFull Replace needs a maximum of 1; with more links it is ambiguous which one to replace.");
            }

            if (definition.IsSymmetric && (hasCapacity || rule.Acyclic))
            {
                throw new InvalidOperationException($"Relationship type '{name}' is symmetric; maxIncoming / maxOutgoing / acyclic need a direction.");
            }

            for (int i = 0; i < rule.BlockedAnyTypeIds.Length; i++)
            {
                int other = rule.BlockedAnyTypeIds[i];
                RequireTypeId(other);
                if (other == typeId)
                {
                    throw new InvalidOperationException($"Relationship type '{name}' rules.blockedAny must not name the type itself.");
                }

                if (Array.IndexOf(rule.RemovedTypeIds, other) >= 0)
                {
                    throw new InvalidOperationException(
                        $"Relationship type '{name}' rules name '{_types.Get(other).Name}' in both blockedAny and removed.");
                }
            }

            for (int i = 0; i < rule.RemovedTypeIds.Length; i++)
            {
                int other = rule.RemovedTypeIds[i];
                RequireTypeId(other);
                if (other == typeId)
                {
                    throw new InvalidOperationException($"Relationship type '{name}' rules.removed must not name the type itself.");
                }
            }
        }

        private void RequireTypeId(int typeId)
        {
            if ((uint)typeId >= (uint)_types.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(typeId), typeId, "Relationship type id is not registered.");
            }
        }

        private void EnsureCapacity(int count)
        {
            if (_rules.Length >= count)
            {
                return;
            }

            int size = Math.Max(count, Math.Max(8, _rules.Length * 2));
            Array.Resize(ref _rules, size);
            Array.Resize(ref _hasRule, size);
        }
    }
}
