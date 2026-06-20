using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Technology.Registry;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.Technology.Config
{
    public sealed class TechnologyConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly TechnologyDefinitionRegistry _technologies;
        private readonly TechnologyRequirementRegistry _requirements;
        private readonly TechnologyScopeKeyRegistry _scopeKeys;

        public TechnologyConfigLoader(
            ConfigPipeline pipeline,
            TechnologyDefinitionRegistry technologies,
            TechnologyRequirementRegistry requirements,
            TechnologyScopeKeyRegistry scopeKeys)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _technologies = technologies ?? throw new ArgumentNullException(nameof(technologies));
            _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _scopeKeys = scopeKeys ?? throw new ArgumentNullException(nameof(scopeKeys));
        }

        public void Load(ConfigCatalog catalog, ConfigConflictReport report = null)
        {
            LoadScopes(catalog, report);
            LoadTechnologies(catalog, report);
            LoadRequirements(catalog, report);
        }

        private void LoadScopes(ConfigCatalog catalog, ConfigConflictReport report)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Technology/scopes.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var sorted = ToSortedEntries(merged);

            var errors = new List<string>();
            for (int i = 0; i < sorted.Count; i++)
            {
                try
                {
                    var cfg = sorted[i].Node.Deserialize<TechnologyScopeConfig>(Options)
                        ?? throw new InvalidOperationException("Failed to deserialize technology scope config.");
                    if (string.IsNullOrWhiteSpace(cfg.Id))
                    {
                        cfg.Id = sorted[i].Id;
                    }

                    if (!string.Equals(cfg.Id, sorted[i].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Technology scope id mismatch: '{sorted[i].Id}' vs '{cfg.Id}'.");
                    }

                    _scopeKeys.Register(cfg.Id);
                }
                catch (Exception ex)
                {
                    errors.Add($"Scope '{sorted[i].Id}': {ex.Message}");
                }
            }

            ThrowIfErrors(errors, "Technology/scopes.json");
        }

        private void LoadTechnologies(ConfigCatalog catalog, ConfigConflictReport report)
        {
            _technologies.Clear();
            TechnologyIdRegistry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, "Technology/technologies.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var sorted = ToSortedEntries(merged);

            for (int i = 0; i < sorted.Count; i++)
            {
                TechnologyIdRegistry.Register(sorted[i].Id);
            }

            var errors = new List<string>();
            for (int i = 0; i < sorted.Count; i++)
            {
                try
                {
                    var cfg = sorted[i].Node.Deserialize<TechnologyConfig>(Options)
                        ?? throw new InvalidOperationException("Failed to deserialize technology config.");
                    if (string.IsNullOrWhiteSpace(cfg.Id))
                    {
                        cfg.Id = sorted[i].Id;
                    }

                    if (!string.Equals(cfg.Id, sorted[i].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Technology id mismatch: '{sorted[i].Id}' vs '{cfg.Id}'.");
                    }

                    int technologyId = TechnologyIdRegistry.GetId(cfg.Id);
                    var definition = new TechnologyDefinition
                    {
                        TechnologyId = technologyId,
                        DefaultScope = ParseScope(cfg.Scope, defaultScope: TechnologyScopeSpec.Self, $"Technology '{cfg.Id}'")
                    };
                    _technologies.Register(technologyId, in definition);
                }
                catch (Exception ex)
                {
                    errors.Add($"Technology '{sorted[i].Id}': {ex.Message}");
                }
            }

            ThrowIfErrors(errors, "Technology/technologies.json");
        }

        private void LoadRequirements(ConfigCatalog catalog, ConfigConflictReport report)
        {
            _requirements.Clear();
            TechnologyRequirementIdRegistry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, "Technology/requirements.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var sorted = ToSortedEntries(merged);

            for (int i = 0; i < sorted.Count; i++)
            {
                TechnologyRequirementIdRegistry.Register(sorted[i].Id);
            }

            var errors = new List<string>();
            for (int i = 0; i < sorted.Count; i++)
            {
                try
                {
                    var cfg = sorted[i].Node.Deserialize<TechnologyRequirementConfig>(Options)
                        ?? throw new InvalidOperationException("Failed to deserialize technology requirement config.");
                    if (string.IsNullOrWhiteSpace(cfg.Id))
                    {
                        cfg.Id = sorted[i].Id;
                    }

                    if (!string.Equals(cfg.Id, sorted[i].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Technology requirement id mismatch: '{sorted[i].Id}' vs '{cfg.Id}'.");
                    }

                    var nodes = new List<TechnologyRequirementNode>(16);
                    var childIndices = new List<int>(16);
                    AddNode(nodes, childIndices, cfg.Root, cfg.Id, "root");
                    int requirementId = TechnologyRequirementIdRegistry.GetId(cfg.Id);
                    _requirements.Register(requirementId, new TechnologyRequirementDefinition(requirementId, nodes.ToArray(), childIndices.ToArray()));
                }
                catch (Exception ex)
                {
                    errors.Add($"Requirement '{sorted[i].Id}': {ex.Message}");
                }
            }

            ThrowIfErrors(errors, "Technology/requirements.json");
            TechnologyIdRegistry.Freeze();
            TechnologyRequirementIdRegistry.Freeze();
            _scopeKeys.Freeze();
        }

        private int AddNode(
            List<TechnologyRequirementNode> nodes,
            List<int> childIndices,
            TechnologyRequirementNodeConfig cfg,
            string ownerId,
            string path)
        {
            if (cfg == null)
            {
                throw new InvalidOperationException($"{path} must be an object.");
            }

            TechnologyRequirementNodeKind kind = ParseNodeKind(cfg.Kind, ownerId, path);
            int nodeIndex = nodes.Count;
            nodes.Add(default);

            int firstChild = childIndices.Count;
            int childCount = 0;
            if (kind is TechnologyRequirementNodeKind.All or TechnologyRequirementNodeKind.Any)
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
            else if (kind == TechnologyRequirementNodeKind.Not)
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

            int technologyId = 0;
            if (!string.IsNullOrWhiteSpace(cfg.Technology))
            {
                technologyId = TechnologyIdRegistry.GetId(cfg.Technology);
                if (technologyId <= 0)
                {
                    throw new InvalidOperationException($"{path}.technology references unknown technology '{cfg.Technology}'.");
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
            if (kind == TechnologyRequirementNodeKind.TechLevelAtLeast)
            {
                if (technologyId <= 0)
                {
                    throw new InvalidOperationException($"{path}.technology is required for kind TechLevelAtLeast.");
                }

                if (cfg.Level <= 0)
                {
                    throw new InvalidOperationException($"{path}.level must be greater than zero for kind TechLevelAtLeast.");
                }

                requiredCount = cfg.Level;
            }
            else if (kind == TechnologyRequirementNodeKind.TechCompleted && technologyId <= 0)
            {
                throw new InvalidOperationException($"{path}.technology is required for kind TechCompleted.");
            }

            nodes[nodeIndex] = new TechnologyRequirementNode(
                kind,
                ParseScope(cfg.Scope, ResolveDefaultScope(technologyId), $"{ownerId}.{path}.scope"),
                ParseEntitySource(cfg.EntitySource),
                firstChild,
                childCount,
                technologyId,
                requiredCount,
                graphProgramId,
                in requiredTags);

            return nodeIndex;
        }

        private static TechnologyRequirementNodeKind ParseNodeKind(string raw, string ownerId, string path)
        {
            return raw switch
            {
                "All" => TechnologyRequirementNodeKind.All,
                "Any" => TechnologyRequirementNodeKind.Any,
                "Not" => TechnologyRequirementNodeKind.Not,
                "TechCompleted" => TechnologyRequirementNodeKind.TechCompleted,
                "EntityCount" => TechnologyRequirementNodeKind.EntityCount,
                "TagAll" => TechnologyRequirementNodeKind.TagAll,
                "GraphValidation" => TechnologyRequirementNodeKind.GraphValidation,
                "TechLevelAtLeast" => TechnologyRequirementNodeKind.TechLevelAtLeast,
                _ => throw new InvalidOperationException($"Requirement '{ownerId}' {path}.kind has unsupported value '{raw}'.")
            };
        }

        private TechnologyScopeSpec ResolveDefaultScope(int technologyId)
        {
            return technologyId > 0 && _technologies.TryGet(technologyId, out var definition)
                ? definition.DefaultScope
                : TechnologyScopeSpec.Self;
        }

        private static TechnologyRequirementEntitySource ParseEntitySource(string? raw)
        {
            return raw switch
            {
                null or "" => TechnologyRequirementEntitySource.ScopeMembers,
                "ScopeMembers" => TechnologyRequirementEntitySource.ScopeMembers,
                "ScopeHost" => TechnologyRequirementEntitySource.ScopeHost,
                "Actor" => TechnologyRequirementEntitySource.Actor,
                "Subject" => TechnologyRequirementEntitySource.Subject,
                _ => throw new InvalidOperationException($"Unsupported technology requirement entitySource '{raw}'.")
            };
        }

        private TechnologyScopeSpec ParseScope(string? raw, TechnologyScopeSpec defaultScope, string context)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultScope;
            }

            string value = raw.Trim();
            return value switch
            {
                "self" => TechnologyScopeSpec.Self,
                "explicit" => new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                _ => _scopeKeys.TryGetId(value, out int scopeKeyId) && scopeKeyId > 0
                    ? new TechnologyScopeSpec(TechnologyScopeKind.Named, scopeKeyId)
                    : throw new InvalidOperationException($"{context} references unknown technology scope '{value}'. Declare it in Technology/scopes.json.")
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
                $"[TechnologyConfigLoader] {errors.Count} compilation error(s) in '{relativePath}'.",
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
