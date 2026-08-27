using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Mod-declared custom event vocabulary (<c>Events/custom_events.json</c>, ArrayById by
    /// "id"). Graph entries may name any engine-known event or any declared custom event;
    /// anything else fails closed at mount time with the full vocabulary listed. Mods fire
    /// their declared events through <see cref="TriggerManager.FireMapCustomEvent"/>.
    /// </summary>
    public sealed class CustomEventNameRegistry
    {
        public const string ConfigPath = "Events/custom_events.json";
        public const string GasEventPrefix = "Gas.Event.";

        private readonly HashSet<string> _custom = new(StringComparer.Ordinal);
        private static readonly HashSet<string> EngineKnown = BuildEngineKnownSet();

        public IReadOnlyCollection<string> CustomNames => _custom;

        public void Register(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Custom event id must be a non-empty string.");
            }

            if (!TryValidateNameShape(name, out string shapeError))
            {
                throw new InvalidOperationException($"Custom event '{name}' is invalid: {shapeError}");
            }

            if (!_custom.Add(name.Trim()))
            {
                throw new InvalidOperationException($"Duplicate custom event declaration '{name}'.");
            }
        }

        public bool IsDeclaredCustom(string name)
        {
            return _custom.Contains(name);
        }

        /// <summary>Engine events ∪ declared custom events ∪ GAS tag bridge pattern.</summary>
        public bool IsKnownEntryEvent(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return EngineKnown.Contains(name) ||
                _custom.Contains(name) ||
                name.StartsWith(GasEventPrefix, StringComparison.Ordinal);
        }

        public string DescribeVocabulary()
        {
            var engine = string.Join(", ", EngineKnown.OrderBy(n => n, StringComparer.Ordinal));
            var custom = _custom.Count == 0
                ? "(none declared)"
                : string.Join(", ", _custom.OrderBy(n => n, StringComparer.Ordinal));
            return $"engine: {engine}; custom: {custom}; dynamic: {GasEventPrefix}*";
        }

        private static bool TryValidateNameShape(string name, out string error)
        {
            error = string.Empty;
            string trimmed = name.Trim();
            if (trimmed.Length < 3)
            {
                error = "ids need at least 3 characters.";
                return false;
            }

            foreach (char c in trimmed)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                {
                    error = $"character '{c}' is not allowed (letters, digits, '.', '_', '-').";
                    return false;
                }
            }

            return true;
        }

        private static HashSet<string> BuildEngineKnownSet()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldInfo field in typeof(GameEvents).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is EventKey key && !string.IsNullOrEmpty(key.Value))
                {
                    names.Add(key.Value);
                }
            }

            return names;
        }
    }

    /// <summary>Loaded custom event catalog: the name vocabulary plus the schema registry
    /// seeded with built-ins and extended by mod-declared parameter schemas.</summary>
    public sealed record CustomEventCatalog(CustomEventNameRegistry Names, EventSchemaRegistry Schemas);

    /// <summary>Config-pipeline loader for <c>Events/custom_events.json</c> (ArrayById, id field).</summary>
    public sealed class CustomEventCatalogLoader
    {
        private readonly ConfigPipeline _configs;

        public CustomEventCatalogLoader(ConfigPipeline configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public CustomEventCatalog Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            Ludots.Core.Scripting.EnumCatalog? enums = null)
        {
            var names = new CustomEventNameRegistry();
            var schemas = new EventSchemaRegistry();
            if (catalog == null || !catalog.TryGet(CustomEventNameRegistry.ConfigPath, out var entry))
            {
                // No mod declares custom events: the vocabulary is simply empty and
                // entry-name validation still covers engine events.
                return new CustomEventCatalog(names, schemas);
            }
            IReadOnlyList<MergedConfigEntry> merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject node ||
                    node["id"]?.GetValue<string>() is not { } name)
                {
                    throw new InvalidOperationException(
                        $"{CustomEventNameRegistry.ConfigPath} entry #{i} must be an object with a non-empty 'id'.");
                }

                names.Register(name);
                if (CustomEventSchemaParser.TryParse(node, name, $"{CustomEventNameRegistry.ConfigPath} entry '{name}'") is { } schema)
                {
                    if (schema.Params.Count > Ludots.Core.NodeLibraries.GASGraph.GraphEntryPayloadTable.Capacity)
                    {
                        throw new InvalidOperationException(
                            $"{CustomEventNameRegistry.ConfigPath} entry '{name}' declares {schema.Params.Count} params; " +
                            $"TriggerGraph entry payload capture supports at most {Ludots.Core.NodeLibraries.GASGraph.GraphEntryPayloadTable.Capacity}.");
                    }

                    ValidateEnumAnnotations(schema, enums);
                    schemas.RegisterCustom(schema);
                }
            }

            return new CustomEventCatalog(names, schemas);
        }

        private static void ValidateEnumAnnotations(EventSchema schema, Ludots.Core.Scripting.EnumCatalog? enums)
        {
            for (int i = 0; i < schema.Params.Count; i++)
            {
                string? enumType = schema.Params[i].EnumType;
                if (enumType == null)
                {
                    continue;
                }

                if (enums == null || !enums.TryGet(enumType, out _))
                {
                    throw new InvalidOperationException(
                        $"{CustomEventNameRegistry.ConfigPath} entry '{schema.EventName}' param '{schema.Params[i].Name}' " +
                        $"annotates enumType '{enumType}' which is not registered in {Ludots.Core.Scripting.EnumCatalogLoader.ConfigPath}.");
                }
            }
        }
    }

    /// <summary>
    /// Strict parser for the optional schema fields of a custom event entry:
    /// <c>scope</c> ("map" default / "entity" / "global") and <c>params[]</c> of
    /// <c>{ name, type, key, optional?, enumType? }</c>. Unknown fields, out-of-whitelist types
    /// (bool / region / team wait on the map variable type contract), and malformed
    /// shapes fail closed. <c>enumType</c> annotates int params with an
    /// <see cref="Ludots.Core.Scripting.EnumCatalog"/> type name; registration is validated by the
    /// loader, which owns the catalog. Entries without <c>params</c> yield a parameterless schema
    /// that still carries the authored scope.
    /// </summary>
    public static class CustomEventSchemaParser
    {
        private static readonly string[] EntryFields = { "id", "description", "scope", "params" };
        private static readonly string[] ParamFields = { "name", "type", "key", "optional", "enumType" };

        public static EventSchema? TryParse(JsonObject node, string eventName, string context)
        {
            foreach (KeyValuePair<string, JsonNode?> field in node)
            {
                if (!EntryFields.Contains(field.Key, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{context} has unknown field '{field.Key}'; allowed: {string.Join(", ", EntryFields)}.");
                }
            }

            EventScope scope = EventScope.Map;
            if (node.ContainsKey("scope"))
            {
                scope = ParseScope(node["scope"], context);
            }

            if (!node.ContainsKey("params"))
            {
                // Parameterless entries still carry their scope; dropping it here would
                // silently re-route Global-scope events into the map table (#1123).
                return new EventSchema(eventName, scope, Array.Empty<EventParamSchema>());
            }

            if (node["params"] is not JsonArray paramsArray)
            {
                throw new InvalidOperationException($"{context} 'params' must be an array.");
            }

            var parsed = new EventParamSchema[paramsArray.Count];
            for (int i = 0; i < paramsArray.Count; i++)
            {
                parsed[i] = ParseParam(paramsArray[i], context, i);
            }

            return new EventSchema(eventName, scope, parsed);
        }

        private static EventScope ParseScope(JsonNode? node, string context)
        {
            string? value = node is JsonValue v && v.TryGetValue<string>(out string? text) ? text : null;
            switch (value)
            {
                case "map": return EventScope.Map;
                case "entity": return EventScope.Entity;
                case "global": return EventScope.Global;
                default:
                    throw new InvalidOperationException(
                        $"{context} 'scope' must be \"map\", \"entity\", or \"global\" (got '{value ?? "null"}').");
            }
        }

        private static EventParamSchema ParseParam(JsonNode? node, string context, int index)
        {
            string label = $"{context} params[{index}]";
            if (node is not JsonObject param)
            {
                throw new InvalidOperationException($"{label} must be an object.");
            }

            foreach (KeyValuePair<string, JsonNode?> field in param)
            {
                if (!ParamFields.Contains(field.Key, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{label} has unknown field '{field.Key}'; allowed: {string.Join(", ", ParamFields)}.");
                }
            }

            string? name = param["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"{label} requires a non-empty 'name'.");
            }

            EventParamType type = ParseType(param["type"], label);
            string? key = param["key"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException($"{label} requires a non-empty 'key'.");
            }

            bool optional = false;
            if (param.ContainsKey("optional"))
            {
                if (param["optional"] is not JsonValue optionalValue || !optionalValue.TryGetValue<bool>(out bool parsedOptional))
                {
                    throw new InvalidOperationException($"{label} 'optional' must be a boolean.");
                }

                optional = parsedOptional;
            }

            string? enumType = null;
            if (param.ContainsKey("enumType"))
            {
                enumType = param["enumType"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(enumType))
                {
                    throw new InvalidOperationException($"{label} 'enumType' must be a non-empty string.");
                }

                if (type != EventParamType.Int)
                {
                    throw new InvalidOperationException(
                        $"{label} 'enumType' annotates int parameters only (got type '{type}'); enum members lower to ints at compile time.");
                }

                enumType = enumType.Trim();
            }

            return new EventParamSchema(name, type, key, optional, enumType);
        }

        private static EventParamType ParseType(JsonNode? node, string label)
        {
            string? value = node is JsonValue v && v.TryGetValue<string>(out string? text) ? text : null;
            switch (value)
            {
                case "entity": return EventParamType.Entity;
                case "int": return EventParamType.Int;
                case "float": return EventParamType.Float;
                case "string": return EventParamType.String;
                case "bool":
                case "region":
                case "team":
                    throw new InvalidOperationException(
                        $"{label} type '{value}' waits on the map variable type contract and fails closed for now; " +
                        "use entity / int / float / string.");
                default:
                    throw new InvalidOperationException(
                        $"{label} has unknown type '{value ?? "null"}'; allowed: entity, int, float, string.");
            }
        }
    }
}
