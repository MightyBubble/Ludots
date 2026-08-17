using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Presentation.Config
{
    internal static class PresentationAssetConfigIdGuard
    {
        public static IReadOnlyList<ConfigFragment> CollectUniqueArrayByIdFragments(
            ConfigPipeline configs,
            in ConfigCatalogEntry entry)
        {
            if (configs == null)
            {
                throw new ArgumentNullException(nameof(configs));
            }

            var fragments = configs.CollectFragmentsWithSources(entry.RelativePath);
            ValidateUniqueLiveIds(fragments, in entry);
            return fragments;
        }

        private static void ValidateUniqueLiveIds(
            IReadOnlyList<ConfigFragment> fragments,
            in ConfigCatalogEntry entry)
        {
            var liveSources = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                ConfigFragment fragment = fragments[fragmentIndex];
                if (fragment.Node is not JsonArray rows)
                {
                    throw new InvalidOperationException(
                        $"Config '{entry.RelativePath}' fragment '{fragment.SourceUri}' must be a JSON array for ArrayById merge.");
                }

                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    if (rows[rowIndex] is not JsonObject obj)
                    {
                        throw new InvalidOperationException(
                            $"Config '{entry.RelativePath}' fragment '{fragment.SourceUri}' item[{rowIndex}] must be a JSON object.");
                    }

                    string id = ReadRequiredId(obj, entry, fragment.SourceUri, rowIndex);
                    if (IsDeleted(obj))
                    {
                        liveSources.Remove(id);
                        continue;
                    }

                    if (liveSources.TryGetValue(id, out string firstSource))
                    {
                        throw new InvalidOperationException(
                            $"Presentation asset config '{entry.RelativePath}' defines duplicate id '{id}'. First source: '{firstSource}'. Duplicate source: '{fragment.SourceUri}'. Use a unique id or delete the previous row before redefining it.");
                    }

                    liveSources[id] = fragment.SourceUri;
                }
            }
        }

        private static string ReadRequiredId(
            JsonObject obj,
            in ConfigCatalogEntry entry,
            string sourceUri,
            int rowIndex)
        {
            if (!obj.TryGetPropertyValue(entry.IdField, out JsonNode? node) ||
                node == null ||
                node is not JsonValue value ||
                !value.TryGetValue(out string? id) ||
                string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    $"Config '{entry.RelativePath}' fragment '{sourceUri}' item[{rowIndex}] must define non-empty string field '{entry.IdField}'.");
            }

            return id;
        }

        private static bool IsDeleted(JsonObject obj)
        {
            return TryReadBool(obj, "__delete", out bool deleted) && deleted ||
                   TryReadBool(obj, "Disabled", out bool disabled) && disabled;
        }

        private static bool TryReadBool(JsonObject obj, string key, out bool value)
        {
            value = false;
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node == null)
            {
                return false;
            }

            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue(out bool boolValue))
                {
                    value = boolValue;
                    return true;
                }

                if (jsonValue.TryGetValue(out string? stringValue) &&
                    bool.TryParse(stringValue, out bool parsed))
                {
                    value = parsed;
                    return true;
                }
            }

            return bool.TryParse(node.ToString(), out value);
        }
    }
}
