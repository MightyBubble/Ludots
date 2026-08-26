using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Strict JSON loader for panel templates (#1010). Unknown fields, unknown source
    /// kinds, and cross-field violations fail at load time with the offending id named.
    /// Collection rows reference reusable item templates by id (resolved after catalog load).
    /// </summary>
    public static class PanelTemplateLoader
    {
        private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
        {
            "id", "skin", "graph", "pins", "events", "intents", "inputs", "collections", "layout", "subject"
        };
        private static readonly HashSet<string> PinFields = new(StringComparer.Ordinal) { "name", "key", "mode", "default" };
        private static readonly HashSet<string> InputFields = new(StringComparer.Ordinal)
        {
            "name", "from", "type"
        };
        private static readonly HashSet<string> InputFromFields = new(StringComparer.Ordinal)
        {
            "space", "output"
        };
        private static readonly HashSet<string> CollectionFields = new(StringComparer.Ordinal)
        {
            "name", "source", "collectionKey", "input", "template"
        };
        private static readonly HashSet<string> ClosedInputTypes = new(StringComparer.Ordinal)
        {
            "EntityCollection",
            "EffectInstanceCollection",
            "EffectTemplateCollection",
            "AbilitySlotCollection",
            "AbilityDefinitionCollection",
            "ItemInstanceCollection",
            "ItemDefinitionCollection",
            "TagIdCollection",
            "TaskInstanceCollection",
            "ActivityInstanceCollection",
            "ProgressionNodeCollection",
            "Bool",
            "Int",
            "Float",
            "Entity"
        };
        private static readonly HashSet<string> ControlFields = new(StringComparer.Ordinal)
        {
            "type", "class", "text", "bind", "prefix", "current", "max", "showWhen",
            "viewportHeight", "itemExtent", "virtualize", "overscan", "present",
            "columns", "aggregate"
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

            PanelSubjectKind subject = PanelSubjectKind.None;
            if (rootObject["subject"] is not null)
            {
                subject = PanelSubjectKinds.Parse(
                    RequireString(rootObject, "subject", $"panel template '{id}'"),
                    $"panel template '{id}'");
            }

            List<PanelTemplateEvent> events = ParseEvents(id, rootObject);
            List<PanelIntentMapEntry> intents = ParseIntents(id, rootObject, events);
            List<PanelInputBinding> inputs = ParseInputs(id, rootObject);
            List<PanelCollectionBinding> collections = ParseCollections(id, rootObject, inputs);
            PanelLayout? layout = ParseLayout(id, rootObject, pins, collections, subject);

            return new PanelTemplate(id, graph, pins, events, intents, skin, collections, layout, subject, inputs);
        }

        private static List<PanelInputBinding> ParseInputs(string templateId, JsonObject rootObject)
        {
            var inputs = new List<PanelInputBinding>();
            if (rootObject["inputs"] is null)
            {
                return inputs;
            }

            if (rootObject["inputs"] is not JsonArray inputsNode)
            {
                throw new InvalidOperationException($"Panel template '{templateId}' inputs must be an array.");
            }

            foreach (JsonNode? inputNode in inputsNode)
            {
                if (inputNode is not JsonObject inputObject)
                {
                    throw new InvalidOperationException($"Panel template '{templateId}' inputs entries must be objects.");
                }

                RejectUnknownFields(inputObject, InputFields, $"panel template '{templateId}' input");
                string name = RequireString(inputObject, "name", $"panel template '{templateId}' input");
                if (inputObject["from"] is not JsonObject fromObject)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' input '{name}' requires object 'from'.");
                }

                RejectUnknownFields(fromObject, InputFromFields, $"panel template '{templateId}' input '{name}' from");
                string space = RequireString(fromObject, "space", $"panel template '{templateId}' input '{name}' from");
                if (!string.Equals(space, "parent", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' input '{name}' from.space must be 'parent', got '{space}'.");
                }

                string output = RequireString(fromObject, "output", $"panel template '{templateId}' input '{name}' from");
                string type = RequireString(inputObject, "type", $"panel template '{templateId}' input '{name}'");
                if (!ClosedInputTypes.Contains(type))
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' input '{name}' type '{type}' is unknown.");
                }

                inputs.Add(new PanelInputBinding(name, space, output, type));
            }

            return inputs;
        }

        private static List<PanelCollectionBinding> ParseCollections(
            string templateId,
            JsonObject rootObject,
            IReadOnlyList<PanelInputBinding> inputs)
        {
            var collections = new List<PanelCollectionBinding>();
            if (rootObject["collections"] is null)
            {
                return collections;
            }

            if (rootObject["collections"] is not JsonArray collectionsNode)
            {
                throw new InvalidOperationException($"Panel template '{templateId}' collections must be an array.");
            }

            var inputNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelInputBinding input in inputs)
            {
                inputNames.Add(input.Name);
            }

            foreach (JsonNode? collectionNode in collectionsNode)
            {
                if (collectionNode is not JsonObject collectionObject)
                {
                    throw new InvalidOperationException($"Panel template '{templateId}' collections entries must be objects.");
                }

                RejectUnknownFields(collectionObject, CollectionFields, $"panel template '{templateId}' collection");
                string name = RequireString(collectionObject, "name", $"panel template '{templateId}' collection");
                string sourceText = RequireString(
                    collectionObject, "source", $"panel template '{templateId}' collection '{name}'");
                PanelCollectionSourceKind source = PanelCollectionSources.Parse(
                    sourceText, $"panel template '{templateId}' collection '{name}'");
                string elementTemplateId = RequireString(
                    collectionObject, "template", $"panel template '{templateId}' collection '{name}'");

                string collectionKey;
                string? inputName = null;
                if (source == PanelCollectionSourceKind.SelfGraph)
                {
                    collectionKey = RequireString(
                        collectionObject, "collectionKey", $"panel template '{templateId}' collection '{name}'");
                    if (collectionObject["input"] is not null)
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{templateId}' collection '{name}' source=selfGraph must not declare input.");
                    }
                }
                else
                {
                    inputName = RequireString(
                        collectionObject, "input", $"panel template '{templateId}' collection '{name}'");
                    if (!inputNames.Contains(inputName))
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{templateId}' collection '{name}' input '{inputName}' is not declared in inputs.");
                    }

                    string? aliasKey = OptionalString(collectionObject, "collectionKey");
                    collectionKey = string.IsNullOrWhiteSpace(aliasKey) ? inputName : aliasKey;
                    if (!string.IsNullOrWhiteSpace(aliasKey) &&
                        !string.Equals(aliasKey, inputName, StringComparison.Ordinal))
                    {
                        // Alias must match the input name until parent-output remapping is wired at catalog bind.
                        throw new InvalidOperationException(
                            $"Panel template '{templateId}' collection '{name}' collectionKey alias '{aliasKey}' must equal input '{inputName}'.");
                    }
                }

                collections.Add(new PanelCollectionBinding(name, collectionKey, elementTemplateId, source, inputName));
            }

            return collections;
        }

        private static PanelLayout? ParseLayout(
            string templateId,
            JsonObject rootObject,
            IReadOnlyList<PanelPin> pins,
            IReadOnlyList<PanelCollectionBinding> collections,
            PanelSubjectKind subject)
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

            if (PanelSubjectKinds.IsEntityBagSubject(subject) || PanelSubjectKinds.IsIntIdBagSubject(subject))
            {
                pinNames.Add(PanelSubjectKinds.EntityDisplayName);
            }

            var collectionNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelCollectionBinding collection in collections)
            {
                collectionNames.Add(collection.Name);
            }

            var controls = new List<PanelLayoutControl>(controlsNode.Count);
            foreach (JsonNode? controlNode in controlsNode)
            {
                controls.Add(ParseControl(templateId, controlNode, pinNames, collectionNames, subject));
            }

            return new PanelLayout(controls);
        }

        private static PanelLayoutControl ParseControl(
            string templateId,
            JsonNode? controlNode,
            HashSet<string> pinNames,
            HashSet<string> collectionNames,
            PanelSubjectKind subject)
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

            if (type == PanelLayoutControlType.List)
            {
                if (string.IsNullOrWhiteSpace(bind) || !collectionNames.Contains(bind))
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list control requires bind to a declared collection name.");
                }
            }

            float? viewportHeight = OptionalPositiveFloat(controlObject, "viewportHeight", templateId);
            float? itemExtent = OptionalPositiveFloat(controlObject, "itemExtent", templateId);
            bool virtualize = false;
            if (controlObject["virtualize"] is JsonValue virtNode && virtNode.TryGetValue<bool>(out bool virtValue))
            {
                virtualize = virtValue;
            }

            int overscan = 2;
            if (controlObject["overscan"] is JsonValue overscanNode)
            {
                if (!overscanNode.TryGetValue<int>(out overscan) || overscan < 0)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list overscan must be a non-negative int.");
                }
            }

            if (virtualize)
            {
                if (type != PanelLayoutControlType.List)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' virtualize is only valid on list controls.");
                }

                if (!viewportHeight.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list '{bind}' virtualize requires viewportHeight.");
                }

                if (!itemExtent.HasValue)
                {
                    itemExtent = 56f;
                }
            }
            else if (itemExtent.HasValue && type != PanelLayoutControlType.List)
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' itemExtent is only valid on list controls.");
            }

            PanelPresentMode present = PanelPresentMode.List;
            if (controlObject["present"] is not null)
            {
                if (type != PanelLayoutControlType.List)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' present is only valid on list controls.");
                }

                string presentText = RequireString(
                    controlObject,
                    "present",
                    $"panel template '{templateId}' list '{bind}'");
                present = PanelPresentModes.Parse(presentText, $"panel template '{templateId}' list '{bind}'");
            }

            int? columns = null;
            if (controlObject["columns"] is not null)
            {
                if (type != PanelLayoutControlType.List)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' columns is only valid on list controls.");
                }

                if (controlObject["columns"] is not JsonValue columnsNode ||
                    !columnsNode.TryGetValue<int>(out int columnsValue) ||
                    columnsValue < 1)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list '{bind}' columns must be an int >= 1.");
                }

                columns = columnsValue;
            }

            PanelAggregateCountSpec? aggregateCount = null;
            if (controlObject["aggregate"] is not null)
            {
                if (type != PanelLayoutControlType.List)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' aggregate is only valid on list controls.");
                }

                aggregateCount = ParseAggregateCount(templateId, bind!, controlObject["aggregate"]);
            }

            if (present == PanelPresentMode.Grid)
            {
                if (!columns.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list '{bind}' present=grid requires columns.");
                }

                if (virtualize)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list '{bind}' cannot combine present=grid with virtualize.");
                }
            }
            else if (columns.HasValue)
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' list '{bind}' columns is only valid when present=grid.");
            }

            if (present == PanelPresentMode.Column && virtualize)
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' list '{bind}' cannot combine present=column with virtualize.");
            }

            if (present == PanelPresentMode.Aggregate)
            {
                if (virtualize)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list '{bind}' cannot combine present=aggregate with virtualize.");
                }

                if (aggregateCount == null)
                {
                    throw new InvalidOperationException(
                        $"Panel template '{templateId}' list '{bind}' present=aggregate requires aggregate.count.");
                }
            }
            else if (aggregateCount != null)
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' list '{bind}' aggregate is only valid when present=aggregate.");
            }

            ValidateControlBindings(templateId, type, bind, current, max, pinNames);

            return new PanelLayoutControl(
                type, className, text, bind, prefix, current, max, showWhen,
                viewportHeight, itemExtent, virtualize, overscan, present, columns, aggregateCount);
        }

        private static PanelAggregateCountSpec ParseAggregateCount(
            string templateId,
            string bind,
            JsonNode? aggregateNode)
        {
            if (aggregateNode is not JsonObject aggregateObject)
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' list '{bind}' aggregate must be an object.");
            }

            RejectUnknownFields(
                aggregateObject,
                new HashSet<string>(StringComparer.Ordinal) { "count" },
                $"panel template '{templateId}' list '{bind}' aggregate");

            if (aggregateObject["count"] is not JsonObject countObject)
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' list '{bind}' aggregate.count must be an object.");
            }

            RejectUnknownFields(
                countObject,
                new HashSet<string>(StringComparer.Ordinal) { "from", "prefix" },
                $"panel template '{templateId}' list '{bind}' aggregate.count");

            string from = RequireString(
                countObject,
                "from",
                $"panel template '{templateId}' list '{bind}' aggregate.count");
            if (!string.Equals(from, "totalCount", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' list '{bind}' aggregate.count.from '{from}' is unknown (allowed: totalCount).");
            }

            if (countObject["prefix"] is not JsonValue prefixNode ||
                !prefixNode.TryGetValue<string>(out string? prefixText) ||
                prefixText == null)
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' list '{bind}' aggregate.count.prefix must be a string (empty allowed).");
            }

            return new PanelAggregateCountSpec(from, prefixText);
        }

        private static float? OptionalPositiveFloat(JsonObject controlObject, string field, string templateId)
        {
            if (controlObject[field] is null)
            {
                return null;
            }

            if (controlObject[field] is not JsonValue valueNode ||
                !valueNode.TryGetValue<double>(out double raw) ||
                raw <= 0d)
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' {field} must be a positive number.");
            }

            return (float)raw;
        }

        private static void ValidateControlBindings(
            string templateId,
            PanelLayoutControlType type,
            string? bind,
            string? current,
            string? max,
            HashSet<string> pinNames)
        {
            switch (type)
            {
                case PanelLayoutControlType.Label:
                case PanelLayoutControlType.Badge:
                    if (!string.IsNullOrWhiteSpace(bind) && !pinNames.Contains(bind))
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{templateId}' control bind '{bind}' is not a known pin.");
                    }

                    break;
                case PanelLayoutControlType.ProgressBar:
                    if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(max) ||
                        !pinNames.Contains(current) || !pinNames.Contains(max))
                    {
                        throw new InvalidOperationException(
                            $"Panel template '{templateId}' progressBar requires current/max bound to pins.");
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
