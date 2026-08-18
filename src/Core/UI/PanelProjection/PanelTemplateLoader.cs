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
        private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal) { "id", "variables", "binds" };
        private static readonly HashSet<string> VariableFields = new(StringComparer.Ordinal) { "name", "kind", "source" };
        private static readonly HashSet<string> SourceFields = new(StringComparer.Ordinal) { "sourceKind", "attributeId", "graphOutputKey" };
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

            return new PanelTemplate(id, variables, binds);
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

            return new PanelTemplateVariable(
                name,
                kind,
                sourceKind,
                attributeId: OptionalString(sourceObject, "attributeId"),
                graphOutputKey: OptionalString(sourceObject, "graphOutputKey"));
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
