using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    /// <summary>
    /// Single compile front door for GAS L1 authoring: Kind → ControlFlow schema → GraphControlFlowCompiler.
    /// </summary>
    public static class GraphProgramAuthoringFrontDoor
    {
        private static readonly string[] RequiredTriggerGraphEntryFields = { "label", "start" };
        private static readonly string[] HookEntryFields = { "hookAnchor", "hookNodeBefore", "hookNodeAfter" };

        public static (GraphProgramPackage? Package, GraphOutputSchema OutputSchema, List<GraphDiagnostic> Diagnostics)
        CompileJsonObject(JsonObject obj, string graphId, JsonSerializerOptions options)
        {
            GraphControlFlowCompileResult result = CompileJsonObjectFull(obj, graphId, options);
            return (result.Package, result.OutputSchema, result.Diagnostics);
        }

        public static GraphControlFlowCompileResult CompileJsonObjectFull(
            JsonObject obj,
            string graphId,
            JsonSerializerOptions options,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas = null,
            Ludots.Core.Scripting.EnumCatalog? enums = null)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrWhiteSpace(graphId)) throw new ArgumentException("graphId is required.", nameof(graphId));
            if (options == null) throw new ArgumentNullException(nameof(options));

            GraphKind kind = RequireKind(obj, graphId);
            RequireControlFlowAuthoringShape(obj, graphId, kind);
            RequireTriggerGraphEntryShape(obj, graphId, kind);

            GraphControlFlowDocument? doc;
            try
            {
                doc = obj.Deserialize<GraphControlFlowDocument>(options);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Strict JSON rejected ControlFlow graph '{graphId}': {ex.Message}",
                    ex);
            }

            if (doc == null)
            {
                throw new InvalidOperationException($"Failed to deserialize ControlFlow graph '{graphId}'.");
            }

            if (string.IsNullOrWhiteSpace(doc.Id))
            {
                doc.Id = graphId;
            }

            if (!string.Equals(doc.Id, graphId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Graph id mismatch: '{graphId}' vs '{doc.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(doc.Kind))
            {
                doc.Kind = kind.ToString();
            }

            return GraphControlFlowCompiler.Compile(doc, eventSchemas, enums);
        }

        public static GraphKind RequireKind(JsonObject obj, string graphId)
        {
            string? kindText = null;
            if (obj.TryGetPropertyValue("kind", out JsonNode? kindNode) && kindNode is JsonValue kindValue)
            {
                kindText = kindValue.GetValue<string>();
            }

            if (!GraphKindParser.TryParse(kindText, out GraphKind kind) || kind == GraphKind.None)
            {
                throw new InvalidOperationException(
                    $"Graph '{graphId}' requires an authored kind ({GraphAuthoringKindPolicy.DescribeSupportedKinds()}).");
            }

            if (!GraphAuthoringKindPolicy.IsControlFlowAuthoringKind(kind))
            {
                throw new InvalidOperationException(
                    $"Graph '{graphId}' kind '{kind}' is not a ControlFlow authoring kind.");
            }

            return kind;
        }

        public static void RequireControlFlowAuthoringShape(JsonObject obj, string graphId, GraphKind kind)
        {
            if (HasLegacyNextChain(obj))
            {
                throw new InvalidOperationException(
                    $"Graph '{graphId}' kind '{kind}' uses nodes[].next. " +
                    "L1 authoring SSOT requires controlEdges/valueEdges only (issue #861).");
            }

            if (!obj.ContainsKey("controlEdges") || !obj.ContainsKey("valueEdges"))
            {
                throw new InvalidOperationException(
                    $"Graph '{graphId}' kind '{kind}' must author controlEdges and valueEdges. " +
                    "Loader no longer selects a compiler from JSON shape.");
            }
        }

        public static bool HasLegacyNextChain(JsonObject obj)
        {
            if (obj["nodes"] is not JsonArray nodes)
            {
                return false;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is JsonObject node && node.ContainsKey("next"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// TriggerGraph replaces the single entry start node with a top-level entries table;
        /// both fields are kind-exclusive and validated before strict deserialize for actionable errors.
        /// </summary>
        public static void RequireTriggerGraphEntryShape(JsonObject obj, string graphId, GraphKind kind)
        {
            if (kind != GraphKind.TriggerGraph)
            {
                if (obj.ContainsKey("entries"))
                {
                    throw new InvalidOperationException(
                        $"Graph '{graphId}' kind '{kind}' must not declare top-level 'entries'; the entry table is TriggerGraph-only.");
                }

                return;
            }

            if (obj.ContainsKey("entry"))
            {
                throw new InvalidOperationException(
                    $"TriggerGraph graph '{graphId}' must not declare top-level 'entry'; author the 'entries' table instead.");
            }

            if (obj["entries"] is not JsonArray entries || entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"TriggerGraph graph '{graphId}' requires a non-empty top-level 'entries' array.");
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is not JsonObject entry)
                {
                    throw new InvalidOperationException(
                        $"TriggerGraph graph '{graphId}' entries[{i}] must be an object.");
                }

                foreach (KeyValuePair<string, JsonNode?> field in entry)
                {
                    switch (field.Key)
                    {
                        case "label":
                        case "event":
                        case "action":
                        case "start":
                            if (field.Value is not JsonValue value || !value.TryGetValue<string>(out _))
                            {
                                throw new InvalidOperationException(
                                    $"TriggerGraph graph '{graphId}' entries[{i}] field '{field.Key}' must be a string.");
                            }

                            break;
                        case "priority":
                            if (field.Value is not JsonValue priorityValue || !priorityValue.TryGetValue<int>(out _))
                            {
                                throw new InvalidOperationException(
                                    $"TriggerGraph graph '{graphId}' entries[{i}] field 'priority' must be an integer.");
                            }

                            break;
                        case "once":
                            if (field.Value is not JsonValue onceValue || !onceValue.TryGetValue<bool>(out _))
                            {
                                throw new InvalidOperationException(
                                    $"TriggerGraph graph '{graphId}' entries[{i}] field 'once' must be a boolean.");
                            }

                            break;
                        case "refire":
                            if (field.Value is not JsonValue refireValue || !refireValue.TryGetValue<string>(out _))
                            {
                                throw new InvalidOperationException(
                                    $"TriggerGraph graph '{graphId}' entries[{i}] field 'refire' must be a string.");
                            }

                            break;
                        case "filters":
                            RequireTriggerGraphEntryFiltersShape(obj, graphId, i, field.Value);
                            break;
                        case "hookAnchor":
                        case "hookNodeBefore":
                        case "hookNodeAfter":
                            RequireTriggerGraphEntryHookShape(obj, graphId, i, field.Key, field.Value);
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"TriggerGraph graph '{graphId}' entries[{i}] has unknown field '{field.Key}'; allowed fields are label, event, action, start, once, refire, priority, filters, hookAnchor, hookNodeBefore, hookNodeAfter.");
                    }
                }

                foreach (string required in RequiredTriggerGraphEntryFields)
                {
                    if (!entry.ContainsKey(required))
                    {
                        throw new InvalidOperationException(
                            $"TriggerGraph graph '{graphId}' entries[{i}] is missing required field '{required}'.");
                    }
                }

                bool hasEvent = entry.ContainsKey("event");
                bool hasAction = entry.ContainsKey("action");
                if (hasEvent == hasAction)
                {
                    throw new InvalidOperationException(
                        $"TriggerGraph graph '{graphId}' entries[{i}] must declare exactly one of 'event' or 'action' (got event={hasEvent}, action={hasAction}).");
                }

                if (hasAction &&
                    entry["filters"] is JsonObject filtersObj &&
                    filtersObj.ContainsKey("action"))
                {
                    throw new InvalidOperationException(
                        $"TriggerGraph graph '{graphId}' entries[{i}] binds top-level 'action' and must not also declare filters.action.");
                }
            }
        }


        /// <summary>
        /// Strict shape of the #1124 hook blocks: hookAnchor needs string graphId /
        /// anchor and position "before"|"after"; hookNodeBefore/After need string
        /// graphId / nodeId. At most one hook block per entry.
        /// </summary>
        private static void RequireTriggerGraphEntryHookShape(JsonObject obj, string graphId, int entryIndex, string fieldKey, JsonNode? hookNode)
        {
            int hookBlockCount = 0;
            foreach (string hookField in HookEntryFields)
            {
                if (obj.ContainsKey(hookField))
                {
                    hookBlockCount++;
                }
            }

            if (hookBlockCount > 1)
            {
                throw new InvalidOperationException(
                    $"TriggerGraph graph '{graphId}' entries[{entryIndex}] declares more than one hook block; " +
                    "combine exactly one of hookAnchor / hookNodeBefore / hookNodeAfter.");
            }

            if (hookNode is not JsonObject hook)
            {
                throw new InvalidOperationException(
                    $"TriggerGraph graph '{graphId}' entries[{entryIndex}] field '{fieldKey}' must be an object.");
            }

            foreach (KeyValuePair<string, JsonNode?> field in hook)
            {
                switch (field.Key)
                {
                    case "graphId":
                    case "anchor":
                    case "nodeId":
                        if (field.Value is not JsonValue value || !value.TryGetValue<string>(out string? text) ||
                            string.IsNullOrWhiteSpace(text))
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraph graph '{graphId}' entries[{entryIndex}] {fieldKey} field '{field.Key}' must be a non-empty string.");
                        }

                        break;
                    case "position":
                        if (field.Value is not JsonValue positionValue || !positionValue.TryGetValue<string>(out string? position) ||
                            (position != "before" && position != "after"))
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraph graph '{graphId}' entries[{entryIndex}] {fieldKey} field 'position' must be \"before\" or \"after\".");
                        }

                        break;
                    default:
                        throw new InvalidOperationException(
                            $"TriggerGraph graph '{graphId}' entries[{entryIndex}] {fieldKey} has unknown field '{field.Key}'.");
                }
            }

            if (!hook.ContainsKey("graphId"))
            {
                throw new InvalidOperationException(
                    $"TriggerGraph graph '{graphId}' entries[{entryIndex}] {fieldKey} is missing required field 'graphId'.");
            }

            bool anchorShape = fieldKey == "hookAnchor";
            string requiredIdField = anchorShape ? "anchor" : "nodeId";
            if (!hook.ContainsKey(requiredIdField))
            {
                throw new InvalidOperationException(
                    $"TriggerGraph graph '{graphId}' entries[{entryIndex}] {fieldKey} is missing required field '{requiredIdField}'.");
            }
        }

        private static void RequireTriggerGraphEntryFiltersShape(JsonObject obj, string graphId, int entryIndex, JsonNode? filtersNode)
        {
            if (filtersNode is not JsonObject filters)
            {
                throw new InvalidOperationException(
                    $"TriggerGraph graph '{graphId}' entries[{entryIndex}] field 'filters' must be an object.");
            }

            foreach (KeyValuePair<string, JsonNode?> field in filters)
            {
                switch (field.Key)
                {
                    case "region":
                    case "tag":
                    case "direction":
                        if (field.Value is not JsonValue textValue || !textValue.TryGetValue<string>(out _))
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraph graph '{graphId}' entries[{entryIndex}] filters field '{field.Key}' must be a string.");
                        }

                        break;
                    case "team":
                        if (field.Value is not JsonValue teamValue || !teamValue.TryGetValue<int>(out _))
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraph graph '{graphId}' entries[{entryIndex}] filters field 'team' must be an integer.");
                        }

                        break;
                    case "threshold":
                        if (field.Value is not JsonValue thresholdValue || !thresholdValue.TryGetValue<float>(out _))
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraph graph '{graphId}' entries[{entryIndex}] filters field 'threshold' must be a number.");
                        }

                        break;
                    case "action":
                        if (field.Value is not JsonValue actionValue || !actionValue.TryGetValue<string>(out _) ||
                            string.IsNullOrWhiteSpace(field.Value.GetValue<string>()))
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraph graph '{graphId}' entries[{entryIndex}] filters field 'action' must be a non-empty string.");
                        }

                        break;
                    case "instanceId":
                        if (field.Value is not JsonValue instanceValue || !instanceValue.TryGetValue<string>(out _) ||
                            string.IsNullOrWhiteSpace(field.Value.GetValue<string>()))
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraph graph '{graphId}' entries[{entryIndex}] filters field 'instanceId' must be a non-empty placed instance id.");
                        }

                        break;
                    case "varName":
                        if (field.Value is not JsonValue varNameValue || !varNameValue.TryGetValue<string>(out _) ||
                            string.IsNullOrWhiteSpace(field.Value.GetValue<string>()))
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraph graph '{graphId}' entries[{entryIndex}] filters field 'varName' must be a non-empty map variable name.");
                        }

                        break;
                    default:
                        throw new InvalidOperationException(
                            $"TriggerGraph graph '{graphId}' entries[{entryIndex}] filters has unknown field '{field.Key}'; allowed fields are region, tag, team, threshold, direction, action, instanceId, varName.");
                }
            }
        }
    }
}
