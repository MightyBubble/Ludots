using System.Text.Json.Nodes;

namespace Ludots.Core.Gameplay.GAS.Config
{
    /// <summary>
    /// Loads PresetTypeDefinition entries from preset_types.json.
    /// Each entry declares components, active phases, lifetime constraints,
    /// and default phase handlers (builtin or graph).
    /// Must be called before EffectTemplateLoader.Load().
    /// </summary>
    public static class PresetTypeLoader
    {
        public static void Load(PresetTypeRegistry registry, string json)
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

        // All enum parsing delegated to GasEnumParser (single source of truth)

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
                // Graph id resolution happens at load time — store 0 for now,
                // the actual graph program ID will be resolved from the graph name
                // by the caller who has access to GraphProgramRegistry.
                // For preset_types.json, graph IDs are typically numeric.
                if (int.TryParse(id, out int graphId))
                    return PhaseHandler.Graph(graphId);
                // Named graph: fail-fast since graph must be resolved at load time
                throw new System.InvalidOperationException($"PresetTypeLoader: Cannot resolve named graph '{id}'. Graph names must be pre-registered as numeric IDs.");
            }

            return PhaseHandler.None;
        }

        // Removed local ParseBuiltinHandlerId — use GasEnumParser.ParseBuiltinHandlerId (single source of truth)
    }
}
