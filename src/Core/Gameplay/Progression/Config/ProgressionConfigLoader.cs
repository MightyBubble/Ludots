using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.Progression.Config
{
    public sealed class ProgressionConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly ProgressionDefinitionRegistry _progressions;
        private readonly ProgressionRequirementRegistry _requirements;
        private readonly ScopeKeyRegistry _scopeKeys;
        private readonly EntityCollectionStore? _entityCollections;
        private readonly RelationshipTypeRegistry? _relationshipTypes;

        public ProgressionConfigLoader(
            ConfigPipeline pipeline,
            ProgressionDefinitionRegistry progressions,
            ProgressionRequirementRegistry requirements,
            ScopeKeyRegistry scopeKeys,
            EntityCollectionStore? entityCollections = null,
            RelationshipTypeRegistry? relationshipTypes = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _progressions = progressions ?? throw new ArgumentNullException(nameof(progressions));
            _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _scopeKeys = scopeKeys ?? throw new ArgumentNullException(nameof(scopeKeys));
            _entityCollections = entityCollections;
            _relationshipTypes = relationshipTypes;
        }

        public void Load(ConfigCatalog catalog, ConfigConflictReport report = null)
        {
            LoadScopes(catalog, report);
            LoadProgressions(catalog, report);
            LoadRequirements(catalog, report);
        }

        private void LoadScopes(ConfigCatalog catalog, ConfigConflictReport report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Progression/scopes.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var sorted = ToSortedEntries(merged);

            var errors = new List<string>();
            for (int i = 0; i < sorted.Count; i++)
            {
                try
                {
                    var cfg = sorted[i].Node.Deserialize<ProgressionScopeConfig>(Options)
                        ?? throw new InvalidOperationException("Failed to deserialize progression scope config.");
                    if (string.IsNullOrWhiteSpace(cfg.Id))
                    {
                        cfg.Id = sorted[i].Id;
                    }

                    if (!string.Equals(cfg.Id, sorted[i].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Progression scope id mismatch: '{sorted[i].Id}' vs '{cfg.Id}'.");
                    }

                    RegisterScope(cfg);
                }
                catch (Exception ex)
                {
                    errors.Add($"Scope '{sorted[i].Id}': {ex.Message}");
                }
            }

            ThrowIfErrors(errors, "Progression/scopes.json");
        }

        private void RegisterScope(ProgressionScopeConfig cfg)
        {
            string memberSource = string.IsNullOrWhiteSpace(cfg.MemberSource)
                ? "ScopeBinding"
                : cfg.MemberSource.Trim();
            switch (memberSource)
            {
                case "ScopeBinding":
                    _scopeKeys.RegisterScopeBindingMembers(cfg.Id);
                    return;
                case "EntityCollection":
                    if (_entityCollections == null)
                    {
                        throw new InvalidOperationException($"Progression scope '{cfg.Id}' uses EntityCollection membership but EntityCollectionStore is not configured.");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.Collection))
                    {
                        throw new InvalidOperationException($"Progression scope '{cfg.Id}' EntityCollection membership requires collection.");
                    }

                    int collectionKeyId = _entityCollections.KeyRegistry.Register(cfg.Collection.Trim());
                    _scopeKeys.RegisterCollectionMembers(cfg.Id, collectionKeyId);
                    return;
                case "Relationship":
                    if (_relationshipTypes == null)
                    {
                        throw new InvalidOperationException($"Progression scope '{cfg.Id}' uses Relationship membership but RelationshipTypeRegistry is not configured.");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.RelationshipType))
                    {
                        throw new InvalidOperationException($"Progression scope '{cfg.Id}' Relationship membership requires relationshipType.");
                    }

                    int relationshipTypeId = _relationshipTypes.GetId(cfg.RelationshipType.Trim());
                    string direction = string.IsNullOrWhiteSpace(cfg.RelationshipDirection)
                        ? "Outgoing"
                        : cfg.RelationshipDirection.Trim();
                    switch (direction)
                    {
                        case "Outgoing":
                            _scopeKeys.RegisterRelationshipOutgoingMembers(cfg.Id, relationshipTypeId);
                            return;
                        case "Incoming":
                            _scopeKeys.RegisterRelationshipIncomingMembers(cfg.Id, relationshipTypeId);
                            return;
                        default:
                            throw new InvalidOperationException($"Progression scope '{cfg.Id}' Relationship membership has unsupported relationshipDirection '{cfg.RelationshipDirection}'. Use 'Outgoing' or 'Incoming'.");
                    }
                default:
                    throw new InvalidOperationException($"Progression scope '{cfg.Id}' has unsupported memberSource '{cfg.MemberSource}'. Use 'ScopeBinding', 'EntityCollection', or 'Relationship'.");
            }
        }

        private void LoadProgressions(ConfigCatalog catalog, ConfigConflictReport report)
        {
            _progressions.Clear();
            ProgressionIdRegistry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, "Progression/progressions.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var sorted = ToSortedEntries(merged);

            for (int i = 0; i < sorted.Count; i++)
            {
                ProgressionIdRegistry.Register(sorted[i].Id);
            }

            var errors = new List<string>();
            for (int i = 0; i < sorted.Count; i++)
            {
                try
                {
                    var cfg = sorted[i].Node.Deserialize<ProgressionConfig>(Options)
                        ?? throw new InvalidOperationException("Failed to deserialize progression config.");
                    if (string.IsNullOrWhiteSpace(cfg.Id))
                    {
                        cfg.Id = sorted[i].Id;
                    }

                    if (!string.Equals(cfg.Id, sorted[i].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Progression id mismatch: '{sorted[i].Id}' vs '{cfg.Id}'.");
                    }

                    int progressionId = ProgressionIdRegistry.GetId(cfg.Id);
                    var definition = new ProgressionDefinition
                    {
                        ProgressionId = progressionId,
                        DeclaredScope = ParseRequiredScope(cfg.Scope, $"Progression '{cfg.Id}'.scope")
                    };
                    _progressions.Register(progressionId, in definition);
                }
                catch (Exception ex)
                {
                    errors.Add($"Progression '{sorted[i].Id}': {ex.Message}");
                }
            }

            ThrowIfErrors(errors, "Progression/progressions.json");
        }

        private void LoadRequirements(ConfigCatalog catalog, ConfigConflictReport report)
        {
            _requirements.Clear();
            ProgressionRequirementIdRegistry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, "Progression/requirements.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var sorted = ToSortedEntries(merged);

            for (int i = 0; i < sorted.Count; i++)
            {
                ProgressionRequirementIdRegistry.Register(sorted[i].Id);
            }

            var errors = new List<string>();
            for (int i = 0; i < sorted.Count; i++)
            {
                try
                {
                    var cfg = sorted[i].Node.Deserialize<ProgressionRequirementConfig>(Options)
                        ?? throw new InvalidOperationException("Failed to deserialize progression requirement config.");
                    if (string.IsNullOrWhiteSpace(cfg.Id))
                    {
                        cfg.Id = sorted[i].Id;
                    }

                    if (!string.Equals(cfg.Id, sorted[i].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Progression requirement id mismatch: '{sorted[i].Id}' vs '{cfg.Id}'.");
                    }

                    var nodes = new List<ProgressionRequirementNode>(16);
                    var childIndices = new List<int>(16);
                    AddNode(nodes, childIndices, cfg.Root, cfg.Id, "root");
                    int requirementId = ProgressionRequirementIdRegistry.GetId(cfg.Id);
                    _requirements.Register(requirementId, new ProgressionRequirementDefinition(requirementId, nodes.ToArray(), childIndices.ToArray()));
                }
                catch (Exception ex)
                {
                    errors.Add($"Requirement '{sorted[i].Id}': {ex.Message}");
                }
            }

            ThrowIfErrors(errors, "Progression/requirements.json");
            ProgressionIdRegistry.Freeze();
            ProgressionRequirementIdRegistry.Freeze();
            _scopeKeys.Freeze();
        }

        private int AddNode(
            List<ProgressionRequirementNode> nodes,
            List<int> childIndices,
            ProgressionRequirementNodeConfig cfg,
            string ownerId,
            string path)
        {
            if (cfg == null)
            {
                throw new InvalidOperationException($"{path} must be an object.");
            }

            ProgressionRequirementNodeKind kind = ParseNodeKind(cfg.Kind, ownerId, path);
            int nodeIndex = nodes.Count;
            nodes.Add(default);

            int firstChild = childIndices.Count;
            int childCount = 0;
            if (kind is ProgressionRequirementNodeKind.All or ProgressionRequirementNodeKind.Any)
            {
                if (cfg.Children == null || cfg.Children.Count == 0)
                {
                    throw new InvalidOperationException($"{path}.children must declare at least one child.");
                }

                for (int i = 0; i < cfg.Children.Count; i++)
                {
                    childIndices.Add(AddNode(nodes, childIndices, cfg.Children[i], ownerId, $"{path}.children[{i}]"));
                    childCount++;
                }
            }
            else if (kind == ProgressionRequirementNodeKind.Not)
            {
                if (cfg.Child == null)
                {
                    throw new InvalidOperationException($"{path}.child is required for kind Not.");
                }

                childIndices.Add(AddNode(nodes, childIndices, cfg.Child, ownerId, $"{path}.child"));
                childCount = 1;
            }

            var requiredTags = default(GameplayTagContainer);
            if (cfg.Tags != null)
            {
                for (int i = 0; i < cfg.Tags.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(cfg.Tags[i]))
                    {
                        throw new InvalidOperationException($"{path}.tags[{i}] must not be empty.");
                    }

                    requiredTags.AddTag(TagRegistry.Register(cfg.Tags[i]));
                }
            }

            int progressionId = 0;
            if (!string.IsNullOrWhiteSpace(cfg.Progression))
            {
                progressionId = ProgressionIdRegistry.GetId(cfg.Progression);
                if (progressionId <= 0)
                {
                    throw new InvalidOperationException($"{path}.progression references unknown progression '{cfg.Progression}'.");
                }
            }

            int graphProgramId = 0;
            if (!string.IsNullOrWhiteSpace(cfg.Graph))
            {
                graphProgramId = GraphIdRegistry.GetId(cfg.Graph);
                if (graphProgramId <= 0)
                {
                    throw new InvalidOperationException($"{path}.graph references unknown graph '{cfg.Graph}'.");
                }
            }

            int requiredCount = cfg.Count;
            if (kind == ProgressionRequirementNodeKind.ProgressionLevelAtLeast)
            {
                if (progressionId <= 0)
                {
                    throw new InvalidOperationException($"{path}.progression is required for kind ProgressionLevelAtLeast.");
                }

                if (cfg.Level <= 0)
                {
                    throw new InvalidOperationException($"{path}.level must be greater than zero for kind ProgressionLevelAtLeast.");
                }

                requiredCount = cfg.Level;
            }
            else if (kind == ProgressionRequirementNodeKind.ProgressionCompleted && progressionId <= 0)
            {
                throw new InvalidOperationException($"{path}.progression is required for kind ProgressionCompleted.");
            }

            nodes[nodeIndex] = new ProgressionRequirementNode(
                kind,
                ParseRequiredScope(cfg.Scope, $"{ownerId}.{path}.scope"),
                ParseRequiredEntitySource(cfg.EntitySource, $"{ownerId}.{path}.entitySource"),
                firstChild,
                childCount,
                progressionId,
                requiredCount,
                graphProgramId,
                in requiredTags);

            return nodeIndex;
        }

        private static ProgressionRequirementNodeKind ParseNodeKind(string raw, string ownerId, string path)
        {
            return raw switch
            {
                "All" => ProgressionRequirementNodeKind.All,
                "Any" => ProgressionRequirementNodeKind.Any,
                "Not" => ProgressionRequirementNodeKind.Not,
                "ProgressionCompleted" => ProgressionRequirementNodeKind.ProgressionCompleted,
                "EntityCount" => ProgressionRequirementNodeKind.EntityCount,
                "TagAll" => ProgressionRequirementNodeKind.TagAll,
                "GraphValidation" => ProgressionRequirementNodeKind.GraphValidation,
                "ProgressionLevelAtLeast" => ProgressionRequirementNodeKind.ProgressionLevelAtLeast,
                _ => throw new InvalidOperationException($"Requirement '{ownerId}' {path}.kind has unsupported value '{raw}'.")
            };
        }

        private static RoleSlot ParseRequiredEntitySource(string? raw, string context)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException($"{context} is required. Use 'ScopeMembers', 'ScopeHost', 'Actor', or 'Subject'.");
            }

            return raw.Trim() switch
            {
                "ScopeMembers" => RoleSlot.ScopeMembers,
                "ScopeHost" => RoleSlot.ScopeHost,
                "Actor" => RoleSlot.Actor,
                "Subject" => RoleSlot.Subject,
                _ => throw new InvalidOperationException($"Unsupported progression requirement entitySource '{raw}'.")
            };
        }

        private ScopeKey ParseRequiredScope(string? raw, string context)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException($"{context} is required. Use 'self', 'explicit', or a named scope declared in Progression/scopes.json.");
            }

            string value = raw.Trim();
            return value switch
            {
                "self" => ScopeKey.Self,
                "explicit" => new ScopeKey(ScopeKind.Explicit),
                _ => _scopeKeys.TryGetId(value, out int scopeKeyId) && scopeKeyId > 0
                    ? new ScopeKey(ScopeKind.Named, scopeKeyId)
                    : throw new InvalidOperationException($"{context} references unknown progression scope '{value}'. Declare it in Progression/scopes.json.")
            };
        }

        private static List<(string Id, JsonObject Node)> ToSortedEntries(IReadOnlyList<MergedConfigEntry> merged)
        {
            var sorted = new List<(string Id, JsonObject Node)>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
            {
                sorted.Add((merged[i].Id, merged[i].Node));
            }

            sorted.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));
            return sorted;
        }

        private static void ThrowIfErrors(List<string> errors, string relativePath)
        {
            if (errors.Count == 0)
            {
                return;
            }

            throw new AggregateException(
                $"[ProgressionConfigLoader] {errors.Count} compilation error(s) in '{relativePath}'.",
                errors.ConvertAll(e => (Exception)new InvalidOperationException(e)));
        }

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };
    }
}
