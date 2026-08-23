using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Gameplay.MapTriggers
{
    public enum TriggerGraphMountDomain
    {
        Map = 0,
        Entity = 1,
        Ability = 2,
        Mod = 3,
    }

    public enum TriggerGraphMountRoute
    {
        Local = 0,
        Global = 1,
    }

    public sealed class TriggerGraphMount
    {
        public const string FieldName = "TriggerGraphs";
        private const string GraphField = "graph";
        private const string ScopeInstanceIdField = "scopeInstanceId";
        private const string DomainField = "domain";
        private const string RouteField = "route";

        public string Graph { get; }
        public string ScopeInstanceId { get; }

        /// <summary>Mount domain; "map" unless authored otherwise.</summary>
        public TriggerGraphMountDomain Domain { get; }
        public TriggerGraphMountRoute Route { get; }

        private TriggerGraphMount(string graph, string scopeInstanceId, TriggerGraphMountDomain domain, TriggerGraphMountRoute route)
        {
            Graph = graph;
            ScopeInstanceId = scopeInstanceId;
            Domain = domain;
            Route = route;
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

                mounts.Add(ParseObject(obj, $"Map '{mapId}' {FieldName}[{i}]"));
            }

            return mounts;
        }

        public static TriggerGraphMount ParseObject(JsonObject obj, string context)
        {
            foreach (var kvp in obj)
            {
                if (!string.Equals(kvp.Key, GraphField, StringComparison.Ordinal) &&
                    !string.Equals(kvp.Key, ScopeInstanceIdField, StringComparison.Ordinal) &&
                    !string.Equals(kvp.Key, DomainField, StringComparison.Ordinal) &&
                    !string.Equals(kvp.Key, RouteField, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{context} has unknown field '{kvp.Key}'. Allowed fields: '{GraphField}', '{ScopeInstanceIdField}', '{DomainField}', '{RouteField}'.");
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

            TriggerGraphMountRoute route = TriggerGraphMountRoute.Local;
            if (obj.TryGetPropertyValue(RouteField, out JsonNode? routeNode) && routeNode != null)
            {
                if (routeNode is not JsonValue routeValue || !routeValue.TryGetValue<string>(out string? routeText))
                {
                    throw new InvalidOperationException($"{context} field '{RouteField}' must be a string.");
                }

                route = ParseRoute(routeText, context);
            }

            if (domain == TriggerGraphMountDomain.Entity && scopeInstanceId == null)
            {
                throw new InvalidOperationException(
                    $"{context} domain 'entity' requires '{ScopeInstanceIdField}'; the entity-domain mount scope is the referenced entity.");
            }

            if (domain is TriggerGraphMountDomain.Ability or TriggerGraphMountDomain.Mod)
            {
                throw new InvalidOperationException(
                    $"{context} domain '{domain.ToString().ToLowerInvariant()}' is declared by its owning catalog; use the ability or mod TriggerGraphs field.");
            }

            if (domain == TriggerGraphMountDomain.Entity && route == TriggerGraphMountRoute.Global)
            {
                throw new InvalidOperationException($"{context} entity-domain mounts cannot declare a global map route; entity scope is map-local.");
            }

            return new TriggerGraphMount(graph, scopeInstanceId, domain, route);
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

            if (string.Equals(text, "ability", StringComparison.Ordinal)) return TriggerGraphMountDomain.Ability;
            if (string.Equals(text, "mod", StringComparison.Ordinal)) return TriggerGraphMountDomain.Mod;

            throw new InvalidOperationException(
                $"{context} field 'domain' value '{text}' is not a mount domain; expected \"map\", \"entity\", \"ability\" or \"mod\".");
        }

        private static TriggerGraphMountRoute ParseRoute(string text, string context)
        {
            if (string.Equals(text, "local", StringComparison.Ordinal)) return TriggerGraphMountRoute.Local;
            if (string.Equals(text, "global", StringComparison.Ordinal)) return TriggerGraphMountRoute.Global;
            throw new InvalidOperationException(
                $"{context} field '{RouteField}' value '{text}' is not a route; expected \"local\" or \"global\".");
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
