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
            if (merged is not JsonArray arr) return catalog;

            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj) continue;
                if (!TryReadString(obj, "Path", out string path)) continue;
                if (!TryReadString(obj, "Policy", out string pol)) continue;
                if (!TryParsePolicy(pol, out var policy)) continue;

                string idField = "Id";
                if (TryReadString(obj, "IdField", out string idf)) idField = idf;

                string[] appendFields = Array.Empty<string>();
                if (obj.TryGetPropertyValue("ArrayAppendFields", out var ap) && ap is JsonArray apArr)
                {
                    var tmp = new List<string>(apArr.Count);
                    for (int a = 0; a < apArr.Count; a++)
                    {
                        if (apArr[a] == null) continue;
                        var s = apArr[a]!.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) tmp.Add(s);
                    }
                    appendFields = tmp.ToArray();
                }

                catalog.Add(new ConfigCatalogEntry(path, policy, idField, appendFields));
            }

            return catalog;
        }

        private static bool TryParsePolicy(string policy, out ConfigMergePolicy result)
        {
            if (string.Equals(policy, "Replace", StringComparison.OrdinalIgnoreCase)) { result = ConfigMergePolicy.Replace; return true; }
            if (string.Equals(policy, "DeepObject", StringComparison.OrdinalIgnoreCase)) { result = ConfigMergePolicy.DeepObject; return true; }
            if (string.Equals(policy, "ArrayReplace", StringComparison.OrdinalIgnoreCase)) { result = ConfigMergePolicy.ArrayReplace; return true; }
            if (string.Equals(policy, "ArrayAppend", StringComparison.OrdinalIgnoreCase)) { result = ConfigMergePolicy.ArrayAppend; return true; }
            if (string.Equals(policy, "ArrayById", StringComparison.OrdinalIgnoreCase)) { result = ConfigMergePolicy.ArrayById; return true; }
            result = default;
            return false;
        }

        private static bool TryReadString(JsonObject obj, string key, out string value)
        {
            value = string.Empty;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            value = node.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}

