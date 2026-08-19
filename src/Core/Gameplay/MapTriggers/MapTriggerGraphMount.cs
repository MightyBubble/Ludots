using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Gameplay.MapTriggers
{
    public sealed class MapTriggerGraphMount
    {
        public const string FieldName = "MapTriggerGraphs";
        private const string GraphField = "graph";
        private const string ScopeInstanceIdField = "scopeInstanceId";

        public string Graph { get; }
        public string ScopeInstanceId { get; }

        private MapTriggerGraphMount(string graph, string scopeInstanceId)
        {
            Graph = graph;
            ScopeInstanceId = scopeInstanceId;
        }

        public static List<MapTriggerGraphMount> ParseList(JsonNode? node, string mapId)
        {
            var mounts = new List<MapTriggerGraphMount>();
            if (node == null)
            {
                return mounts;
            }

            if (node is not JsonArray array)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' {FieldName} must be an array of mount objects.");
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' {FieldName}[{i}] must be an object.");
                }

                mounts.Add(ParseObject(obj, $"Map '{mapId}' {FieldName}[{i}]"));
            }

            return mounts;
        }

        public static MapTriggerGraphMount ParseObject(JsonObject obj, string context)
        {
            foreach (var kvp in obj)
            {
                if (!string.Equals(kvp.Key, GraphField, StringComparison.Ordinal) &&
                    !string.Equals(kvp.Key, ScopeInstanceIdField, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{context} has unknown field '{kvp.Key}'. Allowed fields: '{GraphField}', '{ScopeInstanceIdField}'.");
                }
            }

            string graph = ReadRequiredTrimmedString(obj, GraphField, context);
            string? scopeInstanceId = null;
            if (obj.TryGetPropertyValue(ScopeInstanceIdField, out JsonNode? scopeNode) && scopeNode != null)
            {
                scopeInstanceId = ReadRequiredTrimmedString(obj, ScopeInstanceIdField, context);
            }

            return new MapTriggerGraphMount(graph, scopeInstanceId);
        }

        private static string ReadRequiredTrimmedString(JsonObject obj, string field, string context)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node) ||
                node is not JsonValue value ||
                !value.TryGetValue<string>(out string? text))
            {
                throw new InvalidOperationException(
                    $"{context} requires field '{field}' to be a string.");
            }

            if (string.IsNullOrWhiteSpace(text) || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{context} field '{field}' must be a trimmed non-empty string.");
            }

            return text;
        }
    }
}
