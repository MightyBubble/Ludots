using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Fields.Config
{
    public sealed record FieldHierarchyRoster(string Parent, List<string> Children);

    /// <summary>
    /// Loads <c>Fields/hierarchies.json</c> (ArrayById, id field = parent): roster entries
    /// merge across Core + mod fragments by parent; the later fragment owns 'children'
    /// wholesale (standard ArrayById field-wise semantics). Every key is authored data;
    /// the engine never interprets group or region names.
    /// </summary>
    public sealed class FieldHierarchyConfigLoader
    {
        public const string DefaultRelativePath = "Fields/hierarchies.json";

        private readonly ConfigPipeline _pipeline;

        public FieldHierarchyConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Returns the merged rosters; empty when no asset exists.</summary>
        public List<FieldHierarchyRoster> Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = DefaultRelativePath)
        {
            var rosters = new List<FieldHierarchyRoster>();
            catalog ??= ConfigCatalogLoader.Load(_pipeline);
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "parent");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var options = StrictJsonOptions.CreateCamelCase();

            for (int i = 0; i < merged.Count; i++)
            {
                MergedConfigEntry item = merged[i];
                FieldHierarchyConfig cfg;
                try
                {
                    cfg = item.Node.Deserialize<FieldHierarchyConfig>(options)
                        ?? throw new InvalidOperationException(
                            $"Failed to deserialize hierarchy roster '{item.Id}' from {relativePath}.");
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Hierarchy roster '{item.Id}' in {relativePath}: {ex.Message}", ex);
                }

                if (!string.Equals(cfg.Parent, item.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Hierarchy roster id mismatch in {relativePath}: merged as '{item.Id}' but payload contains '{cfg.Parent}'.");
                }

                rosters.Add(new FieldHierarchyRoster(RequireCanonicalKey(cfg.Parent, relativePath), ParseChildren(cfg, relativePath)));
            }

            return rosters;
        }

        private static string RequireCanonicalKey(string key, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{relativePath}: roster parent '{key}' must be non-blank and carry no surrounding whitespace.");
            }

            return key;
        }

        private static List<string> ParseChildren(FieldHierarchyConfig cfg, string relativePath)
        {
            if (cfg.Children == null || cfg.Children.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Hierarchy roster '{cfg.Parent}' in {relativePath}: 'children' must list at least one region key.");
            }

            foreach (string child in cfg.Children)
            {
                if (string.IsNullOrWhiteSpace(child) || !string.Equals(child, child.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Hierarchy roster '{cfg.Parent}' in {relativePath}: child key '{child}' must be non-blank and carry no surrounding whitespace.");
                }
            }

            return cfg.Children;
        }
    }
}
