using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Strict JSON loader for panel templates (#1010). Unknown fields, unknown source
    /// kinds, and cross-field violations fail at load time with the offending id named.
    /// </summary>
    public static class PanelTemplateLoader
    {
        private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal) { "id", "variables", "binds", "events", "intents" };
        private static readonly HashSet<string> VariableFields = new(StringComparer.Ordinal) { "name", "kind", "source", "realtime" };
        private static readonly HashSet<string> SourceFields = new(StringComparer.Ordinal) { "sourceKind", "attributeId", "graphOutputKey", "lookupTable", "lookupField", "keyAttribute" };
        private static readonly HashSet<string> BindFields = new(StringComparer.Ordinal) { "control", "variable" };

        public static PanelTemplate Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Panel template JSON is empty.");
            }

            JsonNode root;
            try
            {
                root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Panel template JSON parsed to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Panel template JSON is malformed: {ex.Message}");
            }

            if (root is not JsonObject rootObject)
            {
                throw new InvalidOperationException("Panel template root must be a JSON object.");
            }

            return Load(rootObject);
        }

        /// <summary>Loads one template from an already-parsed object (config-catalog merge path).</summary>
        public static PanelTemplate Load(JsonObject rootObject)
        {
            ArgumentNullException.ThrowIfNull(rootObject);

            RejectUnknownFields(rootObject, RootFields, "panel template root");

            string id = RequireString(rootObject, "id", "panel template");
            if (rootObject["variables"] is not JsonArray variablesNode || variablesNode.Count == 0)
            {
                throw new InvalidOperationException($"Panel template '{id}' must declare a non-empty 'variables' array.");
            }

            var variables = new List<PanelTemplateVariable>(variablesNode.Count);
            foreach (JsonNode? variableNode in variablesNode)
            {
                if (variableNode is not JsonObject variableObject)
                {
                    throw new InvalidOperationException($"Panel template '{id}' variables entries must be objects.");
                }

                RejectUnknownFields(variableObject, VariableFields, $"panel template '{id}' variable");
                variables.Add(ParseVariable(id, variableObject));
            }

            var binds = new List<PanelTemplateBind>();
            if (rootObject["binds"] is JsonArray bindsNode)
            {
                foreach (JsonNode? bindNode in bindsNode)
                {
                    if (bindNode is not JsonObject bindObject)
                    {
                        throw new InvalidOperationException($"Panel template '{id}' binds entries must be objects.");
                    }

                    RejectUnknownFields(bindObject, BindFields, $"panel template '{id}' bind");
                    binds.Add(new PanelTemplateBind(
                        RequireString(bindObject, "control", $"panel template '{id}' bind"),
                        RequireString(bindObject, "variable", $"panel template '{id}' bind")));
                }
            }

            List<PanelTemplateEvent> events = ParseEvents(id, rootObject);
            List<PanelIntentMapEntry> intents = ParseIntents(id, rootObject, events);

            return new PanelTemplate(id, variables, binds, events, intents);
        }

        private static List<PanelTemplateEvent> ParseEvents(string templateId, JsonObject rootObject)
        {
            var events = new List<PanelTemplateEvent>();
            if (rootObject["events"] is not JsonArray eventsNode)
            {
                return events;
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal) { "eventId", "control", "gesture", "payload" };
            foreach (JsonNode? eventNode in eventsNode)
            {
                if (eventNode is not JsonObject eventObject)
                {
                    throw new InvalidOperationException($"Panel template '{templateId}' events entries must be objects.");
                }

                RejectUnknownFields(eventObject, allowed, $"panel template '{templateId}' event");
                string eventId = RequireString(eventObject, "eventId", $"panel template '{templateId}' event");
                string gesture = RequireString(eventObject, "gesture", $"panel template '{templateId}' event '{eventId}'");
                string? control = OptionalString(eventObject, "control");

                var payload = new Dictionary<string, PanelEventPayloadKind>(StringComparer.Ordinal);
                if (eventObject["payload"] is JsonObject payloadNode)
                {
                    foreach (KeyValuePair<string, JsonNode?> field in payloadNode)
                    {
                        string? kindText = field.Value?.GetValue<string>();
                        if (!Enum.TryParse<PanelEventPayloadKind>(kindText, ignoreCase: false, out PanelEventPayloadKind kind) ||
                            !Enum.IsDefined(typeof(PanelEventPayloadKind), kind))
                        {
                            throw new InvalidOperationException(
                                $"Panel template '{templateId}' event '{eventId}' payload field '{field.Key}' has unknown kind '{kindText}' (allowed: String, Int, Float, Bool).");
                        }

                        payload[field.Key] = kind;
                    }
                }

                events.Add(new PanelTemplateEvent(eventId, control, gesture, payload));
            }

            return events;
        }

        private static List<PanelIntentMapEntry> ParseIntents(string templateId, JsonObject rootObject, List<PanelTemplateEvent> events)
        {
            var intents = new List<PanelIntentMapEntry>();
            if (rootObject["intents"] is not JsonArray intentsNode)
            {
                return intents;
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal) { "event", "intent", "args", "playerSource", "actorSource" };
            foreach (JsonNode? intentNode in intentsNode)
            {
                if (intentNode is not JsonObject intentObject)
                {
                    throw new InvalidOperationException($"Panel template '{templateId}' intents entries must be objects.");
                }

                RejectUnknownFields(intentObject, allowed, $"panel template '{templateId}' intent");
                string eventId = RequireString(intentObject, "event", $"panel template '{templateId}' intent");
                string intent = RequireString(intentObject, "intent", $"panel template '{templateId}' intent for '{eventId}'");
                string playerSource = RequireString(intentObject, "playerSource", $"panel template '{templateId}' intent '{intent}'");
                string actorSource = RequireString(intentObject, "actorSource", $"panel template '{templateId}' intent '{intent}'");

                var args = new Dictionary<string, string>(StringComparer.Ordinal);
                if (intentObject["args"] is JsonObject argsNode)
                {
                    foreach (KeyValuePair<string, JsonNode?> mapping in argsNode)
                    {
                        string? reference = mapping.Value?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(reference))
                        {
                            throw new InvalidOperationException(
                                $"Panel template '{templateId}' intent '{intent}' arg '{mapping.Key}' must be a $payload.* reference string.");
                        }

                        args[mapping.Key] = reference;
                    }
                }

                intents.Add(new PanelIntentMapEntry(eventId, intent, args, playerSource, actorSource));
            }

            return intents;
        }

        private static PanelTemplateVariable ParseVariable(string templateId, JsonObject variableObject)
        {
            string name = RequireString(variableObject, "name", $"panel template '{templateId}' variable");
            string kindText = RequireString(variableObject, "kind", $"panel template '{templateId}' variable '{name}'");
            if (!Enum.TryParse<PanelTemplateVariableKind>(kindText, ignoreCase: false, out PanelTemplateVariableKind kind) ||
                !Enum.IsDefined(typeof(PanelTemplateVariableKind), kind))
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' variable '{name}' has unknown kind '{kindText}' (allowed: Float, Int).");
            }

            if (variableObject["source"] is not JsonObject sourceObject)
            {
                throw new InvalidOperationException($"Panel template '{templateId}' variable '{name}' requires a 'source' object.");
            }

            RejectUnknownFields(sourceObject, SourceFields, $"panel template '{templateId}' variable '{name}' source");
            string sourceKindText = RequireString(sourceObject, "sourceKind", $"panel template '{templateId}' variable '{name}' source");
            if (!Enum.TryParse<PanelBindingSourceKind>(sourceKindText, ignoreCase: false, out PanelBindingSourceKind sourceKind) ||
                !Enum.IsDefined(typeof(PanelBindingSourceKind), sourceKind))
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' variable '{name}' has unknown sourceKind '{sourceKindText}'.");
            }

            bool realtime = false;
            if (variableObject["realtime"] is JsonNode realtimeNode &&
                (realtimeNode is not JsonValue realtimeValue || !realtimeValue.TryGetValue(out realtime)))
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' variable '{name}' field 'realtime' must be a boolean.");
            }

            return new PanelTemplateVariable(
                name,
                kind,
                sourceKind,
                attributeId: OptionalString(sourceObject, "attributeId"),
                graphOutputKey: OptionalString(sourceObject, "graphOutputKey"),
                lookupTable: OptionalString(sourceObject, "lookupTable"),
                lookupField: OptionalString(sourceObject, "lookupField"),
                keyAttribute: OptionalString(sourceObject, "keyAttribute"),
                realtime: realtime);
        }

        private static string RequireString(JsonObject obj, string field, string context)
        {
            string? value = obj[field]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} is missing required '{field}'.");
            }

            return value;
        }

        private static string? OptionalString(JsonObject obj, string field)
        {
            return obj[field]?.GetValue<string>();
        }

        private static void RejectUnknownFields(JsonObject obj, HashSet<string> allowed, string context)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                if (!allowed.Contains(property.Key))
                {
                    throw new InvalidOperationException($"{context} has unknown field '{property.Key}' (allowed: {string.Join(", ", allowed)}).");
                }
            }
        }
    }
}
