using System;
using System.Collections.Generic;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Knowledge;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Relationships
{
    public readonly struct RelationshipCallbackRule
    {
        public RelationshipCallbackRule(
            string id,
            int metricId,
            int? minimumValue,
            int? maximumValue,
            EventKey enterEventKey,
            EventKey exitEventKey,
            int[] addTagsToSource,
            int[] addTagsToTarget,
            int[] addTagsToSourceTeam,
            int[] addTagsToTargetTeam,
            int[] removeTagsFromSource,
            int[] removeTagsFromTarget,
            int[] removeTagsFromSourceTeam,
            int[] removeTagsFromTargetTeam)
            : this(
                id,
                typeId: 0,
                metricId,
                minimumValue,
                maximumValue,
                enterEventKey,
                exitEventKey,
                addTagsToSource,
                addTagsToTarget,
                addTagsToSourceTeam,
                addTagsToTargetTeam,
                removeTagsFromSource,
                removeTagsFromTarget,
                removeTagsFromSourceTeam,
                removeTagsFromTargetTeam)
        {
        }

        public RelationshipCallbackRule(
            string id,
            int typeId,
            int metricId,
            int? minimumValue,
            int? maximumValue,
            EventKey enterEventKey,
            EventKey exitEventKey,
            int[] addTagsToSource,
            int[] addTagsToTarget,
            int[] addTagsToSourceTeam,
            int[] addTagsToTargetTeam,
            int[] removeTagsFromSource,
            int[] removeTagsFromTarget,
            int[] removeTagsFromSourceTeam,
            int[] removeTagsFromTargetTeam)
        {
            Id = id;
            TypeId = typeId;
            MetricId = metricId;
            MinimumValue = minimumValue;
            MaximumValue = maximumValue;
            EnterEventKey = enterEventKey;
            ExitEventKey = exitEventKey;
            AddTagsToSource = addTagsToSource;
            AddTagsToTarget = addTagsToTarget;
            AddTagsToSourceTeam = addTagsToSourceTeam;
            AddTagsToTargetTeam = addTagsToTargetTeam;
            RemoveTagsFromSource = removeTagsFromSource;
            RemoveTagsFromTarget = removeTagsFromTarget;
            RemoveTagsFromSourceTeam = removeTagsFromSourceTeam;
            RemoveTagsFromTargetTeam = removeTagsFromTargetTeam;
        }

        public string Id { get; }
        public int TypeId { get; }
        public int MetricId { get; }
        public int? MinimumValue { get; }
        public int? MaximumValue { get; }
        public EventKey EnterEventKey { get; }
        public EventKey ExitEventKey { get; }
        public int[] AddTagsToSource { get; }
        public int[] AddTagsToTarget { get; }
        public int[] AddTagsToSourceTeam { get; }
        public int[] AddTagsToTargetTeam { get; }
        public int[] RemoveTagsFromSource { get; }
        public int[] RemoveTagsFromTarget { get; }
        public int[] RemoveTagsFromSourceTeam { get; }
        public int[] RemoveTagsFromTargetTeam { get; }

        public bool Matches(short value)
        {
            if (MinimumValue.HasValue && value < MinimumValue.Value)
            {
                return false;
            }

            if (MaximumValue.HasValue && value > MaximumValue.Value)
            {
                return false;
            }

            return true;
        }
    }

    public readonly struct RelationshipSynergyRule
    {
        public RelationshipSynergyRule(string id, int[] requiredTags, int minimumCount, int[] applyTagsToTeam, EventKey eventKey)
        {
            Id = id;
            RequiredTags = requiredTags;
            MinimumCount = minimumCount;
            ApplyTagsToTeam = applyTagsToTeam;
            EventKey = eventKey;
        }

        public string Id { get; }
        public int[] RequiredTags { get; }
        public int MinimumCount { get; }
        public int[] ApplyTagsToTeam { get; }
        public EventKey EventKey { get; }
        public int StateTagId => ApplyTagsToTeam.Length > 0 ? ApplyTagsToTeam[0] : 0;
    }

    public sealed class RelationshipCatalogRuntime
    {
        public List<RelationshipCallbackRule> Callbacks { get; } = new();
        public List<RelationshipSynergyRule> Synergies { get; } = new();
        private KnowledgeRelationCollectionGrant[] _knowledgeGrants = Array.Empty<KnowledgeRelationCollectionGrant>();

        public int KnowledgeGrantCount => _knowledgeGrants.Length;

        public static RelationshipCatalogRuntime Compile(
            RelationshipCatalogConfig catalog,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            EntityCollectionStore collections)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(collections);

            var runtime = new RelationshipCatalogRuntime();
            for (int i = 0; i < catalog.Callbacks.Count; i++)
            {
                RelationshipCallbackConfig config = catalog.Callbacks[i];
                if (string.IsNullOrWhiteSpace(config.MetricId))
                {
                    continue;
                }

                runtime.Callbacks.Add(new RelationshipCallbackRule(
                    config.Id,
                    types.GetId(config.TypeId),
                    metrics.GetId(config.MetricId),
                    config.MinimumValue,
                    config.MaximumValue,
                    new EventKey(config.EventKey ?? string.Empty),
                    new EventKey(config.ExitEventKey ?? string.Empty),
                    ResolveTags(config.AddTagsToSource),
                    ResolveTags(config.AddTagsToTarget),
                    ResolveTags(config.AddTagsToSourceTeam),
                    ResolveTags(config.AddTagsToTargetTeam),
                    ResolveTags(config.RemoveTagsFromSource),
                    ResolveTags(config.RemoveTagsFromTarget),
                    ResolveTags(config.RemoveTagsFromSourceTeam),
                    ResolveTags(config.RemoveTagsFromTargetTeam)));
            }

            for (int i = 0; i < catalog.Synergies.Count; i++)
            {
                RelationshipSynergyConfig config = catalog.Synergies[i];
                runtime.Synergies.Add(new RelationshipSynergyRule(
                    config.Id,
                    ResolveTags(config.RequireAllTags),
                    Math.Max(1, config.MinimumCount),
                    ResolveTags(config.ApplyTagsToTeam),
                    new EventKey(config.EventKey ?? string.Empty)));
            }

            runtime._knowledgeGrants = CompileKnowledgeGrants(catalog.KnowledgeGrants, types, collections);

            return runtime;
        }

        public bool TryGetKnowledgeGrantAt(int index, out KnowledgeRelationCollectionGrant grant)
        {
            grant = default;
            if ((uint)index >= (uint)_knowledgeGrants.Length)
            {
                return false;
            }

            grant = _knowledgeGrants[index];
            return true;
        }

        private static int[] ResolveTags(List<string>? names)
        {
            if (names == null || names.Count == 0)
            {
                return Array.Empty<int>();
            }

            var ids = new int[names.Count];
            int count = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string? name = names[i];
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                ids[count++] = TagRegistry.Register(name);
            }

            if (count == ids.Length)
            {
                return ids;
            }

            Array.Resize(ref ids, count);
            return ids;
        }

        private static KnowledgeRelationCollectionGrant[] CompileKnowledgeGrants(
            List<RelationshipKnowledgeGrantConfig>? configs,
            RelationshipTypeRegistry types,
            EntityCollectionStore collections)
        {
            if (configs == null || configs.Count == 0)
            {
                return Array.Empty<KnowledgeRelationCollectionGrant>();
            }

            var grants = new KnowledgeRelationCollectionGrant[configs.Count];
            int count = 0;
            for (int i = 0; i < configs.Count; i++)
            {
                RelationshipKnowledgeGrantConfig? config = configs[i];
                if (config == null)
                {
                    continue;
                }

                int relationshipTypeId = types.GetId(RequireNonEmpty(config.TypeId, $"relationship catalog knowledgeGrants[{i}].typeId"));
                int collectionKeyId = collections.KeyRegistry.Register(RequireNonEmpty(config.CollectionKey, $"relationship catalog knowledgeGrants[{i}].collectionKey"));
                grants[count++] = new KnowledgeRelationCollectionGrant(
                    relationshipTypeId,
                    collectionKeyId,
                    new KnowledgeDisclosureRecord(
                        config.Presence,
                        config.Position,
                        BuildMask(config.AttributeIds, config.Attributes, ResolveAttributeId, $"relationship catalog knowledgeGrants[{i}].attributes"),
                        BuildMask(config.RelationshipTypeIds, config.RelationshipTypes, types.GetId, $"relationship catalog knowledgeGrants[{i}].relationshipTypes"),
                        BuildMask(config.TagIds, config.Tags, ResolveTagId, $"relationship catalog knowledgeGrants[{i}].tags"),
                        source: default,
                        config.ObservedTick,
                        config.ExpiryTick,
                        config.ConfidencePermille <= 0 ? 1000 : config.ConfidencePermille,
                        revision: 0));
            }

            if (count == grants.Length)
            {
                return grants;
            }

            Array.Resize(ref grants, count);
            return grants;
        }

        private static KnowledgeIdMask256 BuildMask(
            List<int>? ids,
            List<string>? names,
            Func<string, int> resolveName,
            string context)
        {
            KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty;
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    mask = mask.WithId(ids[i]);
                }
            }

            if (names != null)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    int resolved = resolveName(RequireNonEmpty(names[i], $"{context}[{i}]"));
                    mask = mask.WithId(resolved);
                }
            }

            return mask;
        }

        private static int ResolveAttributeId(string name)
        {
            return AttributeRegistry.Register(name);
        }

        private static int ResolveTagId(string name)
        {
            return TagRegistry.Register(name);
        }

        private static string RequireNonEmpty(string? value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} requires a non-empty value.");
            }

            string trimmed = value.Trim();
            if (!string.Equals(trimmed, value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must be trimmed.");
            }

            return trimmed;
        }
    }
}
