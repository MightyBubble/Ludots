using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Strict loader for reusable item templates. An item never declares parent
    /// container semantics (no collectionKey / list / grid / filter / sort).
    /// </summary>
    public static class PanelItemTemplateLoader
    {
        private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
        {
            "id", "fields", "layout"
        };
        private static readonly HashSet<string> FieldFields = new(StringComparer.Ordinal)
        {
            "name", "kind", "attribute", "tag"
        };
        private static readonly HashSet<string> ControlFields = new(StringComparer.Ordinal)
        {
            "type", "class", "text", "bind", "prefix", "current", "max", "showWhen"
        };

        public static PanelItemTemplate Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Item template JSON is empty.");
            }

            JsonNode root;
            try
            {
                root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Item template JSON parsed to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Item template JSON is malformed: {ex.Message}");
            }

            if (root is not JsonObject rootObject)
            {
                throw new InvalidOperationException("Item template root must be a JSON object.");
            }

            return Load(rootObject);
        }

        public static PanelItemTemplate Load(JsonObject rootObject)
        {
            ArgumentNullException.ThrowIfNull(rootObject);
            RejectUnknownFields(rootObject, RootFields, "item template root");

            string id = RequireString(rootObject, "id", "item template");
            if (rootObject["fields"] is not JsonArray fieldsNode || fieldsNode.Count == 0)
            {
                throw new InvalidOperationException($"Item template '{id}' must declare a non-empty 'fields' array.");
            }

            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            var fields = new List<PanelItemField>(fieldsNode.Count);
            foreach (JsonNode? fieldNode in fieldsNode)
            {
                if (fieldNode is not JsonObject fieldObject)
                {
                    throw new InvalidOperationException($"Item template '{id}' fields entries must be objects.");
                }

                RejectUnknownFields(fieldObject, FieldFields, $"item template '{id}' field");
                string fieldName = RequireString(fieldObject, "name", $"item template '{id}' field");
                if (!fieldNames.Add(fieldName))
                {
                    throw new InvalidOperationException($"Item template '{id}' declares duplicate field '{fieldName}'.");
                }

                string kindText = RequireString(fieldObject, "kind", $"item template '{id}' field '{fieldName}'");
                PanelItemFieldKind kind = kindText switch
                {
                    "attribute" => PanelItemFieldKind.Attribute,
                    "attributeBase" => PanelItemFieldKind.AttributeBase,
                    "tag" => PanelItemFieldKind.Tag,
                    "name" => PanelItemFieldKind.Name,
                    _ => throw new InvalidOperationException(
                        $"Item template '{id}' field '{fieldName}' kind '{kindText}' is unknown."),
                };

                string? symbol = kind switch
                {
                    PanelItemFieldKind.Attribute or PanelItemFieldKind.AttributeBase =>
                        RequireString(fieldObject, "attribute", $"item template '{id}' field '{fieldName}'"),
                    PanelItemFieldKind.Tag =>
                        RequireString(fieldObject, "tag", $"item template '{id}' field '{fieldName}'"),
                    _ => null,
                };

                fields.Add(new PanelItemField(fieldName, kind, symbol));
            }

            if (rootObject["layout"] is not JsonObject layoutObject)
            {
                throw new InvalidOperationException($"Item template '{id}' requires a 'layout' object.");
            }

            if (layoutObject["controls"] is not JsonArray controlsNode || controlsNode.Count == 0)
            {
                throw new InvalidOperationException($"Item template '{id}' layout.controls must be a non-empty array.");
            }

            var controls = new List<PanelLayoutControl>(controlsNode.Count);
            foreach (JsonNode? controlNode in controlsNode)
            {
                controls.Add(ParseControl(id, controlNode, fieldNames));
            }

            return new PanelItemTemplate(id, fields, new PanelLayout(controls));
        }

        private static PanelLayoutControl ParseControl(string templateId, JsonNode? controlNode, HashSet<string> fieldNames)
        {
            if (controlNode is not JsonObject controlObject)
            {
                throw new InvalidOperationException($"Item template '{templateId}' layout controls must be objects.");
            }

            RejectUnknownFields(controlObject, ControlFields, $"item template '{templateId}' layout control");
            string typeText = RequireString(controlObject, "type", $"item template '{templateId}' layout control");
            PanelLayoutControlType type = typeText switch
            {
                "label" => PanelLayoutControlType.Label,
                "progressBar" => PanelLayoutControlType.ProgressBar,
                "badge" => PanelLayoutControlType.Badge,
                "list" or "grid" => throw new InvalidOperationException(
                    $"Item template '{templateId}' cannot declare arrangement control '{typeText}' (list/grid belong on container panels)."),
                _ => throw new InvalidOperationException(
                    $"Item template '{templateId}' layout control type '{typeText}' is unknown."),
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

            switch (type)
            {
                case PanelLayoutControlType.Label:
                case PanelLayoutControlType.Badge:
                    if (!string.IsNullOrWhiteSpace(bind) && !fieldNames.Contains(bind))
                    {
                        throw new InvalidOperationException(
                            $"Item template '{templateId}' control bind '{bind}' is not a declared field.");
                    }

                    break;
                case PanelLayoutControlType.ProgressBar:
                    if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(max) ||
                        !fieldNames.Contains(current) || !fieldNames.Contains(max))
                    {
                        throw new InvalidOperationException(
                            $"Item template '{templateId}' progressBar requires current/max bound to declared fields.");
                    }

                    break;
            }

            return new PanelLayoutControl(type, className, text, bind, prefix, current, max, showWhen);
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

        private static string? OptionalString(JsonObject obj, string field) => obj[field]?.GetValue<string>();

        private static void RejectUnknownFields(JsonObject obj, HashSet<string> allowed, string context)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                if (!allowed.Contains(property.Key))
                {
                    throw new InvalidOperationException(
                        $"{context} has unknown field '{property.Key}' (allowed: {string.Join(", ", allowed)}).");
                }
            }
        }
    }
}
