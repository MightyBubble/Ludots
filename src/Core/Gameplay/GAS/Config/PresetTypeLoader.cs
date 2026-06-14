using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.GAS.Config
{
    /// <summary>
    /// Loads PresetTypeDefinition entries from preset_types.json.
    /// Each entry declares components, active phases, lifetime constraints,
    /// and default phase handlers. All semantic strings must use exact casing.
    /// </summary>
    public sealed class PresetTypeLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly PresetTypeRegistry _registry;

        public PresetTypeLoader(ConfigPipeline pipeline, PresetTypeRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/preset_types.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                var def = ParseDefinition(merged[i].Node);
                _registry.Register(in def);
            }
        }

        public static void LoadFromJson(PresetTypeRegistry registry, string json)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var array = JsonNode.Parse(json)?.AsArray()
                ?? throw new InvalidOperationException("PresetTypeLoader: JSON root must be an array.");

            foreach (var node in array)
            {
                if (node == null)
                {
                    throw new InvalidOperationException("PresetTypeLoader: preset type entry must not be null.");
                }

                var def = ParseDefinition(node.AsObject());
                registry.Register(in def);
            }
        }

        private static PresetTypeDefinition ParseDefinition(JsonObject? obj)
        {
            if (obj == null)
            {
                throw new InvalidOperationException("PresetTypeLoader: preset type entry must be an object.");
            }

            string idStr = RequireString(obj, "id", "PresetTypeLoader");
            var def = new PresetTypeDefinition
            {
                Type = GasEnumParser.ParsePresetType(idStr)
            };

            JsonArray comps = RequireArray(obj, "components", idStr);
            foreach (var c in comps)
            {
                string name = RequireArrayString(c, idStr, "components");
                def.Components |= GasEnumParser.ParseComponentFlag(name);
            }

            JsonArray phases = RequireArray(obj, "activePhases", idStr);
            foreach (var p in phases)
            {
                string name = RequireArrayString(p, idStr, "activePhases");
                def.ActivePhases |= GasEnumParser.ParsePhaseFlag(name);
            }

            JsonArray lifetimes = RequireArray(obj, "allowedLifetimes", idStr);
            foreach (var l in lifetimes)
            {
                string name = RequireArrayString(l, idStr, "allowedLifetimes");
                def.AllowedLifetimes |= GasEnumParser.ParseLifetimeFlag(name);
            }

            JsonObject handlersObj = obj["defaultPhaseHandlers"]?.AsObject()
                ?? throw new InvalidOperationException($"Preset type '{idStr}' must explicitly define defaultPhaseHandlers.");
            foreach (var kvp in handlersObj)
            {
                if (!GasEnumParser.TryParsePhaseId(kvp.Key, out var phaseId))
                {
                    throw new InvalidOperationException($"Preset type '{idStr}' defaultPhaseHandlers has unsupported phase '{kvp.Key}'.");
                }

                var handler = ParsePhaseHandler(idStr, kvp.Key, kvp.Value?.AsObject());
                def.DefaultPhaseHandlers[phaseId] = handler;
            }

            return def;
        }

        private static PhaseHandler ParsePhaseHandler(string presetTypeId, string phaseName, JsonObject? obj)
        {
            if (obj == null)
            {
                throw new InvalidOperationException($"Preset type '{presetTypeId}' handler '{phaseName}' must be an object.");
            }

            string type = RequireString(obj, "type", $"Preset type '{presetTypeId}' handler '{phaseName}'");
            string id = RequireString(obj, "id", $"Preset type '{presetTypeId}' handler '{phaseName}'");

            if (type == "builtin")
            {
                return PhaseHandler.Builtin(GasEnumParser.ParseBuiltinHandlerId(id));
            }

            if (type == "graph")
            {
                if (int.TryParse(id, out int graphId) && graphId > 0)
                {
                    return PhaseHandler.Graph(graphId);
                }

                throw new InvalidOperationException($"Preset type '{presetTypeId}' handler '{phaseName}' graph id '{id}' must be a positive numeric graph id.");
            }

            throw new InvalidOperationException($"Preset type '{presetTypeId}' handler '{phaseName}' has unsupported type '{type}'. Supported: builtin, graph.");
        }

        private static JsonArray RequireArray(JsonObject obj, string propertyName, string presetTypeId)
        {
            return obj[propertyName]?.AsArray()
                ?? throw new InvalidOperationException($"Preset type '{presetTypeId}' must explicitly define {propertyName}.");
        }

        private static string RequireArrayString(JsonNode? node, string presetTypeId, string propertyName)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"Preset type '{presetTypeId}' {propertyName} contains a null entry.");
            }

            string value = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Preset type '{presetTypeId}' {propertyName} contains an empty entry.");
            }

            return value;
        }

        private static string RequireString(JsonObject obj, string propertyName, string context)
        {
            JsonNode? node = obj[propertyName]
                ?? throw new InvalidOperationException($"{context}: required property '{propertyName}' is missing.");
            string value = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context}: required property '{propertyName}' is empty.");
            }

            return value;
        }
    }
}
