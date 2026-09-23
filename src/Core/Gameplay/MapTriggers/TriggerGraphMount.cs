using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Gameplay.MapTriggers
{
    public enum TriggerGraphMountDomain
    {
        Map = 0,
        Entity = 1,
        Mod = 2,
    }

    public sealed class TriggerGraphMount
    {
        public const string FieldName = "TriggerGraphs";
        private const string GraphField = "graph";
        private const string ScopeInstanceIdField = "scopeInstanceId";
        private const string DomainField = "domain";
        private const string IdField = "id";
        private const string PriorityField = "priority";
        private const string EnabledField = "enabled";
        private const string ReplacesField = "replaces";
        private const string MapField = "map";

        public string Graph { get; }
        public string ScopeInstanceId { get; }

        /// <summary>Mount domain; "map" unless authored otherwise.</summary>
        public TriggerGraphMountDomain Domain { get; }

        /// <summary>Mount key id within the owning mod (GAS/map_trigger_mounts.json family only).</summary>
        public string Id { get; }

        /// <summary>Arbitration priority; lower executes first. Default 0.</summary>
        public int Priority { get; }

        /// <summary>False drops the mount at table resolution; default true.</summary>
        public bool Enabled { get; }

        /// <summary>
        /// Mount key of the base mount this mount replaces ("{owner}.{id}"). The base is
        /// dropped from the resolved table; explicit graph-level override, never silent.
        /// </summary>
        public string Replaces { get; }

        /// <summary>Target map id for mod-contributed mounts; null = all maps (mod domain only).</summary>
        public string MapFilter { get; }

        private TriggerGraphMount(
            string graph,
            string scopeInstanceId,
            TriggerGraphMountDomain domain,
            string id,
            int priority,
            bool enabled,
            string replaces,
            string mapFilter)
        {
            Graph = graph;
            ScopeInstanceId = scopeInstanceId;
            Domain = domain;
            Id = id;
            Priority = priority;
            Enabled = enabled;
            Replaces = replaces;
            MapFilter = mapFilter;
        }

        public static List<TriggerGraphMount> ParseList(JsonNode? node, string mapId)
        {
            var mounts = new List<TriggerGraphMount>();
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

                string context = $"Map '{mapId}' {FieldName}[{i}]";
                RejectMapConfigFamilyOnlyFields(obj, context);
                TriggerGraphMount mount = ParseObject(obj, context);
                if (mount.Domain == TriggerGraphMountDomain.Mod)
                {
                    throw new InvalidOperationException(
                        $"{context} domain 'mod' is not valid here; mod-domain mounts are declared by mods in GAS/map_trigger_mounts.json.");
                }

                mounts.Add(mount);
            }

            return mounts;
        }

        private static void RejectMapConfigFamilyOnlyFields(JsonObject obj, string context)
        {
            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, IdField, StringComparison.Ordinal)
                    || string.Equals(kvp.Key, PriorityField, StringComparison.Ordinal)
                    || string.Equals(kvp.Key, EnabledField, StringComparison.Ordinal)
                    || string.Equals(kvp.Key, ReplacesField, StringComparison.Ordinal)
                    || string.Equals(kvp.Key, MapField, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{context} has unknown field '{kvp.Key}'. Allowed fields: '{GraphField}', '{ScopeInstanceIdField}', '{DomainField}'.");
                }
            }
        }

        public static TriggerGraphMount ParseObject(JsonObject obj, string context)
        {
            foreach (var kvp in obj)
            {
                if (!IsKnownField(kvp.Key))
                {
                    throw new InvalidOperationException(
                        $"{context} has unknown field '{kvp.Key}'. Allowed fields: "
                        + $"'{GraphField}', '{ScopeInstanceIdField}', '{DomainField}', '{IdField}', '{PriorityField}', "
                        + $"'{EnabledField}', '{ReplacesField}', '{MapField}'.");
                }
            }

            string graph = ReadRequiredTrimmedString(obj, GraphField, context);
            string? scopeInstanceId = null;
            if (obj.TryGetPropertyValue(ScopeInstanceIdField, out JsonNode? scopeNode) && scopeNode != null)
            {
                scopeInstanceId = ReadRequiredTrimmedString(obj, ScopeInstanceIdField, context);
            }

            TriggerGraphMountDomain domain = TriggerGraphMountDomain.Map;
            if (obj.TryGetPropertyValue(DomainField, out JsonNode? domainNode) && domainNode != null)
            {
                if (domainNode is not JsonValue domainValue || !domainValue.TryGetValue<string>(out string? domainText))
                {
                    throw new InvalidOperationException(
                        $"{context} field '{DomainField}' must be a string.");
                }

                domain = ParseDomain(domainText, context);
            }

            if (domain == TriggerGraphMountDomain.Entity && scopeInstanceId == null)
            {
                throw new InvalidOperationException(
                    $"{context} domain 'entity' requires '{ScopeInstanceIdField}'; the entity-domain mount scope is the referenced entity.");
            }

            string? id = null;
            if (obj.TryGetPropertyValue(IdField, out JsonNode? idNode) && idNode != null)
            {
                id = ReadRequiredTrimmedString(obj, IdField, context);
            }

            int priority = 0;
            if (obj.TryGetPropertyValue(PriorityField, out JsonNode? priorityNode) && priorityNode != null)
            {
                if (priorityNode is not JsonValue priorityValue || !priorityValue.TryGetValue<int>(out int priorityInt))
                {
                    throw new InvalidOperationException(
                        $"{context} field '{PriorityField}' must be an integer.");
                }

                priority = priorityInt;
            }

            bool enabled = true;
            if (obj.TryGetPropertyValue(EnabledField, out JsonNode? enabledNode) && enabledNode != null)
            {
                if (enabledNode is not JsonValue enabledValue || !enabledValue.TryGetValue<bool>(out bool enabledBool))
                {
                    throw new InvalidOperationException(
                        $"{context} field '{EnabledField}' must be a boolean.");
                }

                enabled = enabledBool;
            }

            string? replaces = null;
            if (obj.TryGetPropertyValue(ReplacesField, out JsonNode? replacesNode) && replacesNode != null)
            {
                replaces = ReadRequiredTrimmedString(obj, ReplacesField, context);
            }

            string? mapFilter = null;
            if (obj.TryGetPropertyValue(MapField, out JsonNode? mapNode) && mapNode != null)
            {
                mapFilter = ReadRequiredTrimmedString(obj, MapField, context);
            }

            return new TriggerGraphMount(graph, scopeInstanceId, domain, id ?? string.Empty, priority, enabled, replaces ?? string.Empty, mapFilter ?? string.Empty);
        }

        private static bool IsKnownField(string key)
        {
            return string.Equals(key, GraphField, StringComparison.Ordinal)
                || string.Equals(key, ScopeInstanceIdField, StringComparison.Ordinal)
                || string.Equals(key, DomainField, StringComparison.Ordinal)
                || string.Equals(key, IdField, StringComparison.Ordinal)
                || string.Equals(key, PriorityField, StringComparison.Ordinal)
                || string.Equals(key, EnabledField, StringComparison.Ordinal)
                || string.Equals(key, ReplacesField, StringComparison.Ordinal)
                || string.Equals(key, MapField, StringComparison.Ordinal);
        }

        private static TriggerGraphMountDomain ParseDomain(string text, string context)
        {
            if (string.Equals(text, "map", StringComparison.Ordinal))
            {
                return TriggerGraphMountDomain.Map;
            }

            if (string.Equals(text, "entity", StringComparison.Ordinal))
            {
                return TriggerGraphMountDomain.Entity;
            }

            if (string.Equals(text, "mod", StringComparison.Ordinal))
            {
                return TriggerGraphMountDomain.Mod;
            }

            if (string.Equals(text, "ability", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{context} domain 'ability' is not mountable yet; ability-domain mounts land with the ability-domain slice.");
            }

            throw new InvalidOperationException(
                $"{context} field 'domain' value '{text}' is not a mount domain; expected \"map\", \"entity\", or \"mod\".");
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
