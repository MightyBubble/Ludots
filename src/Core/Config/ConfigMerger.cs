using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public static class ConfigMerger
    {
        public static JsonNode? MergeMany(IReadOnlyList<JsonNode> fragments, in ConfigCatalogEntry entry)
        {
            if (fragments == null || fragments.Count == 0) return null;

            switch (entry.MergePolicy)
            {
                case ConfigMergePolicy.Replace:
                    return fragments[^1].DeepClone();
                case ConfigMergePolicy.DeepObject:
                    return MergeDeepObject(fragments);
                case ConfigMergePolicy.ArrayReplace:
                    return MergeArrayReplace(fragments);
                case ConfigMergePolicy.ArrayAppend:
                    return MergeArrayAppend(fragments);
                case ConfigMergePolicy.ArrayById:
                    return MergeArrayById(fragments, entry.IdField, entry.ArrayAppendFields);
                default:
                    return fragments[^1].DeepClone();
            }
        }

        public static JsonNode? MergeManyWithReport(IReadOnlyList<ConfigFragment> fragments, in ConfigCatalogEntry entry, ConfigConflictReport report)
        {
            if (fragments == null || fragments.Count == 0) return null;

            for (int i = 0; i < fragments.Count; i++)
            {
                report.RecordFragment(entry.RelativePath, fragments[i].SourceUri);
            }

            if (entry.MergePolicy == ConfigMergePolicy.ArrayById)
            {
                return MergeArrayByIdWithReport(fragments, entry, report);
            }

            var nodes = new JsonNode[fragments.Count];
            for (int i = 0; i < fragments.Count; i++) nodes[i] = fragments[i].Node;
            return MergeMany(nodes, in entry);
        }

        private static JsonNode MergeDeepObject(IReadOnlyList<JsonNode> fragments)
        {
            var merged = new JsonObject();
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i] is JsonObject obj) ConfigPipeline.DeepMerge(merged, obj);
            }
            return merged;
        }

        private static JsonNode? MergeArrayReplace(IReadOnlyList<JsonNode> fragments)
        {
            for (int i = fragments.Count - 1; i >= 0; i--)
            {
                if (fragments[i] is JsonArray arr) return arr.DeepClone();
            }
            return null;
        }

        private static JsonNode MergeArrayAppend(IReadOnlyList<JsonNode> fragments)
        {
            var merged = new JsonArray();
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i] is not JsonArray arr) continue;
                foreach (var item in arr) merged.Add(item?.DeepClone());
            }
            return merged;
        }

        private static JsonNode MergeArrayById(IReadOnlyList<JsonNode> fragments, string idField, string[] arrayAppendFields)
        {
            var orderedIds = new List<string>(capacity: 256);
            var mergedNodes = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i] is not JsonArray arr) continue;

                foreach (var node in arr)
                {
                    if (node is not JsonObject obj) continue;
                    if (!TryReadId(obj, idField, out string id)) continue;

                    if (IsDeleted(obj))
                    {
                        mergedNodes.Remove(id);
                        for (int oi = orderedIds.Count - 1; oi >= 0; oi--)
                        {
                            if (string.Equals(orderedIds[oi], id, StringComparison.OrdinalIgnoreCase))
                            {
                                orderedIds.RemoveAt(oi);
                                break;
                            }
                        }
                        continue;
                    }

                    if (!mergedNodes.TryGetValue(id, out var existing))
                    {
                        mergedNodes[id] = obj.DeepClone();
                        orderedIds.Add(id);
                        continue;
                    }

                    MergeObject(existing, obj, arrayAppendFields);
                }
            }

            var result = new JsonArray();
            for (int i = 0; i < orderedIds.Count; i++)
            {
                if (mergedNodes.TryGetValue(orderedIds[i], out var n)) result.Add(n);
            }
            return result;
        }

        private static bool IsDeleted(JsonObject obj)
        {
            if (TryReadBool(obj, "__delete", out bool del) && del) return true;
            if (TryReadBool(obj, "Disabled", out bool disabled) && disabled) return true;
            return false;
        }

        private static bool TryReadBool(JsonObject obj, string key, out bool value)
        {
            value = default;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out bool b)) { value = b; return true; }
                if (v.TryGetValue(out string s) && bool.TryParse(s, out bool p)) { value = p; return true; }
            }
            return bool.TryParse(node.ToString(), out value);
        }

        private static void MergeObject(JsonNode target, JsonObject source, string[] arrayAppendFields)
        {
            if (target is not JsonObject tObj) return;

            foreach (var kvp in source)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                if (!tObj.TryGetPropertyValue(key, out var existing) || existing == null || value == null)
                {
                    tObj[key] = value?.DeepClone();
                    continue;
                }

                if (existing is JsonObject exObj && value is JsonObject vObj)
                {
                    MergeObject(exObj, vObj, arrayAppendFields);
                    continue;
                }

                if (existing is JsonArray exArr && value is JsonArray vArr)
                {
                    if (ShouldAppendArrayField(key, arrayAppendFields))
                    {
                        foreach (var item in vArr) exArr.Add(item?.DeepClone());
                    }
                    else
                    {
                        tObj[key] = vArr.DeepClone();
                    }
                    continue;
                }

                tObj[key] = value.DeepClone();
            }
        }

        private static bool ShouldAppendArrayField(string fieldName, string[] arrayAppendFields)
        {
            for (int i = 0; i < arrayAppendFields.Length; i++)
            {
                if (string.Equals(arrayAppendFields[i], fieldName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool TryReadId(JsonObject obj, string idField, out string id)
        {
            id = string.Empty;
            if (obj.TryGetPropertyValue(idField, out var idNode) && idNode != null)
            {
                id = idNode.ToString();
                return !string.IsNullOrWhiteSpace(id);
            }

            foreach (var kvp in obj)
            {
                if (!string.Equals(kvp.Key, idField, StringComparison.OrdinalIgnoreCase)) continue;
                if (kvp.Value == null) return false;
                id = kvp.Value.ToString();
                return !string.IsNullOrWhiteSpace(id);
            }

            return false;
        }

        private static JsonNode MergeArrayByIdWithReport(IReadOnlyList<ConfigFragment> fragments, in ConfigCatalogEntry entry, ConfigConflictReport report)
        {
            var orderedIds = new List<string>(capacity: 256);
            var mergedNodes = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].Node is not JsonArray arr) continue;
                string src = fragments[i].SourceUri;

                foreach (var node in arr)
                {
                    if (node is not JsonObject obj) continue;
                    if (!TryReadId(obj, entry.IdField, out string id)) continue;

                    if (IsDeleted(obj))
                    {
                        mergedNodes.Remove(id);
                        report.RecordDeleted(entry.RelativePath, id, src);
                        for (int oi = orderedIds.Count - 1; oi >= 0; oi--)
                        {
                            if (string.Equals(orderedIds[oi], id, StringComparison.OrdinalIgnoreCase))
                            {
                                orderedIds.RemoveAt(oi);
                                break;
                            }
                        }
                        continue;
                    }

                    if (!mergedNodes.TryGetValue(id, out var existing))
                    {
                        mergedNodes[id] = obj.DeepClone();
                        orderedIds.Add(id);
                        report.RecordWinner(entry.RelativePath, id, src);
                        continue;
                    }

                    MergeObject(existing, obj, entry.ArrayAppendFields);
                    report.RecordWinner(entry.RelativePath, id, src);
                }
            }

            var result = new JsonArray();
            for (int i = 0; i < orderedIds.Count; i++)
            {
                if (mergedNodes.TryGetValue(orderedIds[i], out var n)) result.Add(n);
            }
            return result;
        }
    }
}
