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
        private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal) { "id", "skin", "graph", "pins", "events", "intents" };
        private static readonly HashSet<string> PinFields = new(StringComparer.Ordinal) { "name", "source", "key", "record", "path", "mode", "default" };

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
            string? skin = null;
            if (rootObject["skin"] is JsonValue skinValue && skinValue.TryGetValue<string>(out string? skinText))
            {
                PanelHosting.PanelSkinIds.ToId(skinText);
                skin = skinText.Trim();
            }
            string? graph = OptionalString(rootObject, "graph");
            if (rootObject["pins"] is not JsonArray pinsNode || pinsNode.Count == 0)
            {
                throw new InvalidOperationException($"Panel template '{id}' must declare a non-empty 'pins' array.");
            }

            var pins = new List<PanelPin>(pinsNode.Count);
            foreach (JsonNode? pinNode in pinsNode)
            {
                if (pinNode is not JsonObject pinObject)
                {
                    throw new InvalidOperationException($"Panel template '{id}' pins entries must be objects.");
                }

                RejectUnknownFields(pinObject, PinFields, $"panel template '{id}' pin");
                string pinName = RequireString(pinObject, "name", $"panel template '{id}' pin");
                string sourceText = OptionalString(pinObject, "source") ?? "graph";
                string modeText = OptionalString(pinObject, "mode") ?? "realtime";
                if (!string.Equals(modeText, "realtime", StringComparison.Ordinal) &&
                    !string.Equals(modeText, "snapshot", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Panel template '{id}' pin '{pinName}' mode must be realtime or snapshot, got '{modeText}'.");
                }

                float defaultValue = 0f;
                if (pinObject["default"] is { } defaultValueNode)
                {
                    if (defaultValueNode is not JsonValue defaultValueValue ||
                        !defaultValueValue.TryGetValue<double>(out double defaultValueRaw))
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{id}' pin '{pinName}' default must be a number.");
                    }

                    defaultValue = (float)defaultValueRaw;
                }

                bool realtime = string.Equals(modeText, "realtime", StringComparison.Ordinal);
                if (string.Equals(sourceText, "graph", StringComparison.Ordinal))
                {
                    if (pinObject.ContainsKey("record") || pinObject.ContainsKey("path"))
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{id}' graph pin '{pinName}' cannot declare data record/path.");
                    }

                    string pinKey = RequireString(pinObject, "key", $"panel template '{id}' pin '{pinName}'");
                    pins.Add(new PanelPin(pinName, pinKey, realtime, defaultValue));
                }
                else if (string.Equals(sourceText, "data", StringComparison.Ordinal))
                {
                    if (pinObject.ContainsKey("key"))
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{id}' data pin '{pinName}' cannot declare graph key.");
                    }

                    string recordId = RequireString(pinObject, "record", $"panel template '{id}' data pin '{pinName}'");
                    string path = RequireString(pinObject, "path", $"panel template '{id}' data pin '{pinName}'");
                    pins.Add(new PanelPin(pinName, PanelPinSourceKind.Data, recordId, recordId, path, realtime, defaultValue));
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Panel template '{id}' pin '{pinName}' source must be graph or data, got '{sourceText}'.");
                }
            }

            List<PanelTemplateEvent> events = ParseEvents(id, rootObject);
            List<PanelIntentMapEntry> intents = ParseIntents(id, rootObject, events);

            return new PanelTemplate(id, graph, pins, events, intents, skin);
        }

        private static List<PanelTemplateEvent> ParseEvents(string templateId, JsonObject rootObject)
        {
            var events = new List<PanelTemplateEvent>();
            if (rootObject["events"] is null)
            {
                return events;
            }

            if (rootObject["events"] is not JsonArray eventsNode)
            {
                throw new InvalidOperationException($"Panel template '{templateId}' events must be an array.");
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
                if (eventObject["payload"] is { } payloadPresent && payloadPresent is not JsonObject)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' event '{eventId}' payload must be an object.");
                }

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
            if (rootObject["intents"] is null)
            {
                return intents;
            }

            if (rootObject["intents"] is not JsonArray intentsNode)
            {
                throw new InvalidOperationException($"Panel template '{templateId}' intents must be an array.");
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
