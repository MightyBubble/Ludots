using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.GAS.Config
{
    /// <summary>
    /// Loads PresetTypeDefinition entries from preset_types.json.
    /// Each entry declares components, active phases, lifetime constraints,
    /// and default phase handlers (builtin or graph).
    /// Must be called before EffectTemplateLoader.Load().
    /// </summary>
    public sealed class PresetTypeLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly PresetTypeRegistry _registry;

        public PresetTypeLoader(ConfigPipeline pipeline, PresetTypeRegistry registry)
        {
            _pipeline = pipeline;
            _registry = registry;
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/preset_types.json")
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                var def = ParseDefinition(merged[i].Node);
                _registry.Register(in def);
            }
        }

        /// <summary>
        /// Load preset type definitions from a raw JSON string.
        /// Used by tests and scenarios without a ConfigPipeline.
        /// </summary>
        public static void LoadFromJson(PresetTypeRegistry registry, string json)
        {
            var array = JsonNode.Parse(json)?.AsArray();
            if (array == null) throw new System.InvalidOperationException("PresetTypeLoader: JSON root must be an array.");

            foreach (var node in array)
            {
                if (node == null) continue;
                var obj = node.AsObject();
                var def = ParseDefinition(obj);
                registry.Register(in def);
            }
        }

        private static PresetTypeDefinition ParseDefinition(JsonObject obj)
        {
            var def = new PresetTypeDefinition();

            // id → EffectPresetType enum
            string idStr = obj["id"]?.GetValue<string>() ?? "";
            def.Type = GasEnumParser.ParsePresetType(idStr);

            // components → ComponentFlags
            var comps = obj["components"]?.AsArray();
            if (comps != null)
            {
                foreach (var c in comps)
                {
                    string name = c?.GetValue<string>() ?? "";
                    def.Components |= GasEnumParser.ParseComponentFlag(name);
                }
            }

            // activePhases → PhaseFlags
            var phases = obj["activePhases"]?.AsArray();
            if (phases != null)
            {
                foreach (var p in phases)
                {
                    string name = p?.GetValue<string>() ?? "";
                    def.ActivePhases |= GasEnumParser.ParsePhaseFlag(name);
                }
            }

            // allowedLifetimes → LifetimeFlags
            var lifetimes = obj["allowedLifetimes"]?.AsArray();
            if (lifetimes != null)
            {
                foreach (var l in lifetimes)
                {
                    string name = l?.GetValue<string>() ?? "";
                    def.AllowedLifetimes |= GasEnumParser.ParseLifetimeFlag(name);
                }
            }

            // defaultPhaseHandlers → PhaseHandlerMap
            var handlersObj = obj["defaultPhaseHandlers"]?.AsObject();
            if (handlersObj != null)
            {
                foreach (var kvp in handlersObj)
                {
                    if (!GasEnumParser.TryParsePhaseId(kvp.Key, out var phaseIdEnum)) continue;
                    var phaseId = (int)phaseIdEnum;
                    var handler = ParsePhaseHandler(kvp.Value?.AsObject());
                    def.DefaultPhaseHandlers[(EffectPhaseId)phaseId] = handler;
                }
            }

            return def;
        }

        private static PhaseHandler ParsePhaseHandler(JsonObject obj)
        {
            if (obj == null) return PhaseHandler.None;

            string type = obj["type"]?.GetValue<string>() ?? "";
            string id = obj["id"]?.GetValue<string>() ?? "";

            if (type == "builtin")
            {
                var builtinId = GasEnumParser.ParseBuiltinHandlerId(id);
                return PhaseHandler.Builtin(builtinId);
            }

            if (type == "graph")
            {
                if (int.TryParse(id, out int graphId))
                    return PhaseHandler.Graph(graphId);
                throw new System.InvalidOperationException($"PresetTypeLoader: Cannot resolve named graph '{id}'. Graph names must be pre-registered as numeric IDs.");
            }

            return PhaseHandler.None;
        }
    }
}
