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
            var merged = pipeline.MergeFromCatalog(in entry);

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

                catalog.Add(new ConfigCatalogEntry(path, policy, idField, appendFields));
            }

            return catalog;
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
                if (pair.Key is "Path" or "Policy" or "IdField" or "ArrayAppendFields")
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
    }
}

