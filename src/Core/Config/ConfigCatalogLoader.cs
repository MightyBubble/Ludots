using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public static class ConfigCatalogLoader
    {
        public static ConfigCatalog Load(ConfigPipeline pipeline, string relativePath = "config_catalog.json")
        {
            var entry = new ConfigCatalogEntry(relativePath, ConfigMergePolicy.ArrayById, idField: "Path");
            var fragments = pipeline.CollectFragmentsWithSources(in entry);
            ValidateNoDuplicateCatalogPaths(fragments, relativePath);

            var nodes = new List<JsonNode>(fragments.Count);
            for (int i = 0; i < fragments.Count; i++)
            {
                nodes.Add(fragments[i].Node);
            }

            var merged = ConfigMerger.MergeMany(nodes, in entry);

            var catalog = new ConfigCatalog();
            if (merged is not JsonArray arr)
            {
                throw new InvalidOperationException(
                    $"Config catalog '{relativePath}' must merge to a JSON array.");
            }

            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"Config catalog '{relativePath}' entry at index {i} must be a JSON object.");
                }

                ValidateKnownProperties(obj, relativePath, i);
                string path = ReadRequiredString(obj, "Path", relativePath, i);
                ValidateRelativeConfigPath(path, relativePath, i, "Path");
                string pol = ReadRequiredString(obj, "Policy", relativePath, i);
                ConfigMergePolicy policy = ParsePolicy(pol, path);

                string idField = "id";
                if (obj.TryGetPropertyValue("IdField", out _))
                {
                    idField = ReadRequiredString(obj, "IdField", relativePath, i);
                }

                string[] appendFields = Array.Empty<string>();
                if (obj.TryGetPropertyValue("ArrayAppendFields", out var ap))
                {
                    if (ap is not JsonArray apArr)
                    {
                        throw new InvalidOperationException(
                            $"Config catalog entry '{path}' ArrayAppendFields must be a JSON array.");
                    }

                    var tmp = new List<string>(apArr.Count);
                    for (int a = 0; a < apArr.Count; a++)
                    {
                        if (apArr[a] == null)
                        {
                            throw new InvalidOperationException(
                                $"Config catalog entry '{path}' ArrayAppendFields[{a}] must be a non-empty string.");
                        }

                        var s = apArr[a]!.ToString();
                        if (string.IsNullOrWhiteSpace(s))
                        {
                            throw new InvalidOperationException(
                                $"Config catalog entry '{path}' ArrayAppendFields[{a}] must be a non-empty string.");
                        }

                        tmp.Add(s);
                    }
                    appendFields = tmp.ToArray();
                }

                string[] shardDirectories = Array.Empty<string>();
                if (obj.TryGetPropertyValue("ShardDirectories", out var shardNode))
                {
                    shardDirectories = ReadStringArray(shardNode, path, "ShardDirectories");
                    for (int s = 0; s < shardDirectories.Length; s++)
                    {
                        ValidateRelativeConfigPath(shardDirectories[s], relativePath, i, $"ShardDirectories[{s}]");
                    }
                }

                bool allowEmpty = false;
                if (obj.TryGetPropertyValue("AllowEmpty", out var allowEmptyNode))
                {
                    if (allowEmptyNode == null || allowEmptyNode.GetValueKind() != System.Text.Json.JsonValueKind.True &&
                        allowEmptyNode.GetValueKind() != System.Text.Json.JsonValueKind.False)
                    {
                        throw new InvalidOperationException(
                            $"Config catalog entry '{path}' AllowEmpty must be a boolean.");
                    }

                    allowEmpty = allowEmptyNode.GetValue<bool>();
                }

                catalog.Add(new ConfigCatalogEntry(path, policy, idField, appendFields, shardDirectories, allowEmpty));
            }

            return catalog;
        }

        private static void ValidateNoDuplicateCatalogPaths(List<ConfigFragment> fragments, string relativePath)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                if (fragments[fragmentIndex].Node is not JsonArray arr)
                {
                    continue;
                }

                for (int entryIndex = 0; entryIndex < arr.Count; entryIndex++)
                {
                    if (arr[entryIndex] is not JsonObject obj)
                    {
                        continue;
                    }

                    if (!TryReadString(obj, "Path", out string path))
                    {
                        continue;
                    }

                    if (seen.TryGetValue(path, out string? firstSource))
                    {
                        throw new InvalidOperationException(
                            $"Config catalog '{relativePath}' declares duplicate Path '{path}' in '{firstSource}' and '{fragments[fragmentIndex].SourceUri}'.");
                    }

                    seen[path] = fragments[fragmentIndex].SourceUri;
                }
            }
        }

        private static ConfigMergePolicy ParsePolicy(string policy, string path)
        {
            if (string.Equals(policy, "Replace", StringComparison.Ordinal)) return ConfigMergePolicy.Replace;
            if (string.Equals(policy, "DeepObject", StringComparison.Ordinal)) return ConfigMergePolicy.DeepObject;
            if (string.Equals(policy, "ArrayReplace", StringComparison.Ordinal)) return ConfigMergePolicy.ArrayReplace;
            if (string.Equals(policy, "ArrayAppend", StringComparison.Ordinal)) return ConfigMergePolicy.ArrayAppend;
            if (string.Equals(policy, "ArrayById", StringComparison.Ordinal)) return ConfigMergePolicy.ArrayById;
            throw new InvalidOperationException($"Config catalog entry '{path}' has unknown merge policy '{policy}'.");
        }

        private static void ValidateKnownProperties(JsonObject obj, string relativePath, int index)
        {
            foreach (var pair in obj)
            {
                if (pair.Key is "Path" or "Policy" or "IdField" or "ArrayAppendFields" or "ShardDirectories" or "AllowEmpty")
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Config catalog '{relativePath}' entry at index {index} contains unknown property '{pair.Key}'.");
            }
        }

        private static bool TryReadString(JsonObject obj, string key, out string value)
        {
            value = string.Empty;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            value = node.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static string ReadRequiredString(JsonObject obj, string key, string relativePath, int index)
        {
            if (!TryReadString(obj, key, out string value))
            {
                throw new InvalidOperationException(
                    $"Config catalog '{relativePath}' entry at index {index} must declare non-empty '{key}'.");
            }

            return value;
        }

        private static string[] ReadStringArray(JsonNode? node, string path, string propertyName)
        {
            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException(
                    $"Config catalog entry '{path}' {propertyName} must be a JSON array.");
            }

            var values = new List<string>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] == null)
                {
                    throw new InvalidOperationException(
                        $"Config catalog entry '{path}' {propertyName}[{i}] must be a non-empty string.");
                }

                string value = arr[i]!.ToString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException(
                        $"Config catalog entry '{path}' {propertyName}[{i}] must be a non-empty string.");
                }

                values.Add(value);
            }

            return values.ToArray();
        }

        private static void ValidateRelativeConfigPath(string path, string catalogPath, int index, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    $"Config catalog '{catalogPath}' entry at index {index} {propertyName} must be non-empty.");
            }

            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.Contains(":", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Config catalog '{catalogPath}' entry at index {index} {propertyName} must be a relative path.");
            }

            string[] parts = normalized.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "." || parts[i] == "..")
                {
                    throw new InvalidOperationException(
                        $"Config catalog '{catalogPath}' entry at index {index} {propertyName} must not contain traversal segments.");
                }
            }
        }
    }
}
