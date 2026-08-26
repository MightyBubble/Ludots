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
        private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
        {
            "id", "skin", "graph", "pins", "events", "intents", "lists", "layout"
        };
        private static readonly HashSet<string> PinFields = new(StringComparer.Ordinal) { "name", "key", "mode", "default" };
        private static readonly HashSet<string> ListFields = new(StringComparer.Ordinal)
        {
            "name", "collectionKey", "item"
        };
        private static readonly HashSet<string> ItemFieldFields = new(StringComparer.Ordinal)
        {
            "name", "kind", "attribute", "tag"
        };
        private static readonly HashSet<string> ControlFields = new(StringComparer.Ordinal)
        {
            "type", "class", "text", "bind", "prefix", "current", "max", "showWhen", "itemControls"
        };

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
            string graph = RequireString(rootObject, "graph", "panel template");
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
                string pinKey = RequireString(pinObject, "key", $"panel template '{id}' pin '{pinName}'");
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

                pins.Add(new PanelPin(pinName, pinKey, realtime: string.Equals(modeText, "realtime", StringComparison.Ordinal), defaultValue));
            }

            List<PanelTemplateEvent> events = ParseEvents(id, rootObject);
            List<PanelIntentMapEntry> intents = ParseIntents(id, rootObject, events);
            List<PanelListDeclaration> lists = ParseLists(id, rootObject);
            PanelLayout? layout = ParseLayout(id, rootObject, pins, lists);

            return new PanelTemplate(id, graph, pins, events, intents, skin, lists, layout);
        }

        private static List<PanelListDeclaration> ParseLists(string templateId, JsonObject rootObject)
        {
            var lists = new List<PanelListDeclaration>();
            if (rootObject["lists"] is null)
            {
                return lists;
            }

            if (rootObject["lists"] is not JsonArray listsNode)
            {
                throw new InvalidOperationException($"Panel template '{templateId}' lists must be an array.");
            }

            foreach (JsonNode? listNode in listsNode)
            {
                if (listNode is not JsonObject listObject)
                {
                    throw new InvalidOperationException($"Panel template '{templateId}' lists entries must be objects.");
                }

                RejectUnknownFields(listObject, ListFields, $"panel template '{templateId}' list");
                string name = RequireString(listObject, "name", $"panel template '{templateId}' list");
                string collectionKey = RequireString(listObject, "collectionKey", $"panel template '{templateId}' list '{name}'");

                if (listObject["item"] is not JsonObject itemObject)
                {
                    throw new InvalidOperationException($"Panel template '{templateId}' list '{name}' requires an 'item' object.");
                }

                if (itemObject["fields"] is not JsonArray fieldsNode || fieldsNode.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list '{name}' item.fields must be a non-empty array.");
                }

                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                var fields = new List<PanelItemField>(fieldsNode.Count);
                foreach (JsonNode? fieldNode in fieldsNode)
                {
                    if (fieldNode is not JsonObject fieldObject)
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{templateId}' list '{name}' item.fields entries must be objects.");
                    }

                    RejectUnknownFields(fieldObject, ItemFieldFields, $"panel template '{templateId}' list '{name}' field");
                    string fieldName = RequireString(fieldObject, "name", $"panel template '{templateId}' list '{name}' field");
                    if (!fieldNames.Add(fieldName))
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{templateId}' list '{name}' declares duplicate field '{fieldName}'.");
                    }

                    string kindText = RequireString(fieldObject, "kind", $"panel template '{templateId}' list '{name}' field '{fieldName}'");
                    PanelItemFieldKind kind = kindText switch
                    {
                        "attribute" => PanelItemFieldKind.Attribute,
                        "attributeBase" => PanelItemFieldKind.AttributeBase,
                        "tag" => PanelItemFieldKind.Tag,
                        "name" => PanelItemFieldKind.Name,
                        _ => throw new InvalidOperationException(
                            $"Panel template '{templateId}' list '{name}' field '{fieldName}' kind '{kindText}' is unknown."),
                    };

                    string? symbol = kind switch
                    {
                        PanelItemFieldKind.Attribute or PanelItemFieldKind.AttributeBase =>
                            RequireString(fieldObject, "attribute", $"panel template '{templateId}' list '{name}' field '{fieldName}'"),
                        PanelItemFieldKind.Tag =>
                            RequireString(fieldObject, "tag", $"panel template '{templateId}' list '{name}' field '{fieldName}'"),
                        _ => null,
                    };

                    fields.Add(new PanelItemField(fieldName, kind, symbol));
                }

                lists.Add(new PanelListDeclaration(name, collectionKey, fields));
            }

            return lists;
        }

        private static PanelLayout? ParseLayout(
            string templateId,
            JsonObject rootObject,
            IReadOnlyList<PanelPin> pins,
            IReadOnlyList<PanelListDeclaration> lists)
        {
            if (rootObject["layout"] is null)
            {
                return null;
            }

            if (rootObject["layout"] is not JsonObject layoutObject)
            {
                throw new InvalidOperationException($"Panel template '{templateId}' layout must be an object.");
            }

            if (layoutObject["controls"] is not JsonArray controlsNode)
            {
                throw new InvalidOperationException($"Panel template '{templateId}' layout.controls must be an array.");
            }

            var pinNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelPin pin in pins)
            {
                pinNames.Add(pin.Name);
            }

            var listNames = new HashSet<string>(StringComparer.Ordinal);
            var listFields = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (PanelListDeclaration list in lists)
            {
                listNames.Add(list.Name);
                var fieldSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (PanelItemField field in list.Fields)
                {
                    fieldSet.Add(field.Name);
                }

                listFields[list.Name] = fieldSet;
            }

            var controls = new List<PanelLayoutControl>(controlsNode.Count);
            foreach (JsonNode? controlNode in controlsNode)
            {
                controls.Add(ParseControl(templateId, controlNode, pinNames, listNames, listFields, itemScope: null));
            }

            return new PanelLayout(controls);
        }

        private static PanelLayoutControl ParseControl(
            string templateId,
            JsonNode? controlNode,
            HashSet<string> pinNames,
            HashSet<string> listNames,
            Dictionary<string, HashSet<string>> listFields,
            string? itemScope)
        {
            if (controlNode is not JsonObject controlObject)
            {
                throw new InvalidOperationException($"Panel template '{templateId}' layout controls must be objects.");
            }

            RejectUnknownFields(controlObject, ControlFields, $"panel template '{templateId}' layout control");
            string typeText = RequireString(controlObject, "type", $"panel template '{templateId}' layout control");
            PanelLayoutControlType type = typeText switch
            {
                "label" => PanelLayoutControlType.Label,
                "progressBar" => PanelLayoutControlType.ProgressBar,
                "badge" => PanelLayoutControlType.Badge,
                "list" => PanelLayoutControlType.List,
                _ => throw new InvalidOperationException(
                    $"Panel template '{templateId}' layout control type '{typeText}' is unknown."),
            };

            string? className = OptionalString(controlObject, "class");
            string? text = OptionalString(controlObject, "text");
            string? bind = OptionalString(controlObject, "bind");
            string? prefix = OptionalString(controlObject, "prefix");
            string? current = OptionalString(controlObject, "current");
            string? max = OptionalString(controlObject, "max");
            bool? showWhen = null;
            if (controlObject["showWhen"] is JsonValue showNode && showNode.TryGetValue<bool>(out bool showValue))
            {
                showWhen = showValue;
            }

            List<PanelLayoutControl>? itemControls = null;
            if (type == PanelLayoutControlType.List)
            {
                if (string.IsNullOrWhiteSpace(bind) || !listNames.Contains(bind))
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list control requires bind to a declared list name.");
                }

                if (controlObject["itemControls"] is not JsonArray itemControlsNode || itemControlsNode.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list control '{bind}' requires non-empty itemControls.");
                }

                itemControls = new List<PanelLayoutControl>(itemControlsNode.Count);
                foreach (JsonNode? child in itemControlsNode)
                {
                    itemControls.Add(ParseControl(templateId, child, pinNames, listNames, listFields, itemScope: bind));
                }
            }
            else if (controlObject["itemControls"] is not null)
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' control type '{typeText}' cannot declare itemControls.");
            }

            ValidateControlBindings(templateId, type, bind, current, max, pinNames, listFields, itemScope);

            return new PanelLayoutControl(type, className, text, bind, prefix, current, max, showWhen, itemControls);
        }

        private static void ValidateControlBindings(
            string templateId,
            PanelLayoutControlType type,
            string? bind,
            string? current,
            string? max,
            HashSet<string> pinNames,
            Dictionary<string, HashSet<string>> listFields,
            string? itemScope)
        {
            bool InScope(string name)
            {
                if (itemScope != null && listFields.TryGetValue(itemScope, out HashSet<string>? fields))
                {
                    return fields.Contains(name);
                }

                return pinNames.Contains(name);
            }

            switch (type)
            {
                case PanelLayoutControlType.Label:
                case PanelLayoutControlType.Badge:
                    if (!string.IsNullOrWhiteSpace(bind) && !InScope(bind))
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{templateId}' control bind '{bind}' is not a known pin/field in scope.");
                    }

                    break;
                case PanelLayoutControlType.ProgressBar:
                    if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(max) ||
                        !InScope(current) || !InScope(max))
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{templateId}' progressBar requires current/max bound to fields in scope.");
                    }

                    break;
            }
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
