using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Platform.Abstractions;

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
        private readonly BuiltinHandlerRegistry _builtinHandlers;

        public PresetTypeLoader(
            ConfigPipeline pipeline,
            PresetTypeRegistry registry,
            BuiltinHandlerRegistry builtinHandlers)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _builtinHandlers = builtinHandlers ?? throw new ArgumentNullException(nameof(builtinHandlers));
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/preset_types.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var ordered = new List<MergedConfigEntry>(merged);
            ordered.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));

            for (int i = 0; i < ordered.Count; i++)
            {
                var def = ParseDefinition(ordered[i].Node, _registry, _builtinHandlers);
                _registry.Register(in def);
            }
        }

        public static void LoadFromJson(
            PresetTypeRegistry registry,
            string json,
            BuiltinHandlerRegistry builtinHandlers)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (builtinHandlers == null) throw new ArgumentNullException(nameof(builtinHandlers));
            var array = JsonNode.Parse(json)?.AsArray()
                ?? throw new InvalidOperationException("PresetTypeLoader: JSON root must be an array.");

            var ordered = new List<JsonObject>(array.Count);
            foreach (var node in array)
            {
                if (node == null)
                {
                    throw new InvalidOperationException("PresetTypeLoader: preset type entry must not be null.");
                }

                ordered.Add(node.AsObject());
            }

            ordered.Sort((left, right) =>
                StringComparer.Ordinal.Compare(
                    RequireString(left, "id", "PresetTypeLoader"),
                    RequireString(right, "id", "PresetTypeLoader")));

            for (int i = 0; i < ordered.Count; i++)
            {
                var def = ParseDefinition(ordered[i], registry, builtinHandlers);
                registry.Register(in def);
            }
        }

        private static PresetTypeDefinition ParseDefinition(
            JsonObject? obj,
            PresetTypeRegistry registry,
            BuiltinHandlerRegistry builtinHandlers)
        {
            if (obj == null)
            {
                throw new InvalidOperationException("PresetTypeLoader: preset type entry must be an object.");
            }

            string idStr = RequireString(obj, "id", "PresetTypeLoader");
            int typeId = registry.RegisterKey(idStr);
            EffectPresetType builtinType = EffectPresetType.None;
            if (Enum.TryParse(idStr, ignoreCase: false, out EffectPresetType parsedBuiltin) &&
                parsedBuiltin != EffectPresetType.None &&
                Enum.IsDefined(typeof(EffectPresetType), parsedBuiltin))
            {
                builtinType = parsedBuiltin;
            }

            var def = new PresetTypeDefinition
            {
                Type = builtinType,
                TypeId = typeId,
                TypeKey = idStr,
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

                var handler = ParsePhaseHandler(idStr, kvp.Key, kvp.Value?.AsObject(), builtinHandlers);
                def.DefaultPhaseHandlers[phaseId] = handler;
            }

            return def;
        }

        private static PhaseHandler ParsePhaseHandler(
            string presetTypeId,
            string phaseName,
            JsonObject? obj,
            BuiltinHandlerRegistry builtinHandlers)
        {
            if (obj == null)
            {
                throw new InvalidOperationException($"Preset type '{presetTypeId}' handler '{phaseName}' must be an object.");
            }

            string type = RequireString(obj, "type", $"Preset type '{presetTypeId}' handler '{phaseName}'");
            string id = RequireString(obj, "id", $"Preset type '{presetTypeId}' handler '{phaseName}'");

            if (type == "builtin")
            {
                int handlerId = builtinHandlers.GetId(id);
                if (handlerId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Preset type '{presetTypeId}' handler '{phaseName}' references unknown builtin handler '{id}'.");
                }

                return PhaseHandler.Builtin(handlerId);
            }

            if (type == "graph")
            {
                int graphId = GraphIdRegistry.GetId(id);
                if (graphId > 0)
                {
                    return PhaseHandler.Graph(graphId);
                }

                throw new InvalidOperationException($"Preset type '{presetTypeId}' handler '{phaseName}' references unknown graph '{id}'.");
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
